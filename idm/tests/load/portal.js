// IdM API: bir vaqtda ko'p foydalanuvchi (k6). Ishga tushirish: tests/load/run.sh (README.md).
// Uch xil foydalanuvchi, haqiqiy portal ishlatganidek: oddiy xodim (ko'pchilik), rahbar (attestatsiya, tasdiqlash), administrator/auditor.
// O'zgaruvchilar: BASE_URL, TOKENS (mock-sso.py yozgan JSON), USERS (eng ko'p bir vaqtdagi foydalanuvchi), DURATION, PROFILE (load | spike).
import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import { Counter, Rate, Trend } from 'k6/metrics';
import exec from 'k6/execution';

const BASE = __ENV.BASE_URL || 'http://127.0.0.1:7200';
const USERS = parseInt(__ENV.USERS || '300', 10);
const DURATION = __ENV.DURATION || '10m';
const PROFILE = __ENV.PROFILE || 'load';
const tokens = JSON.parse(open(__ENV.TOKENS));
// Brauzer kabi siqilgan javob so'raladi (k6 o'zi ochadi; data_received — tarmoqdagi haqiqiy hajm)
const ACCEPT_ENCODING = 'br, gzip';
const employees = new SharedArray('employees', () => Object.keys(tokens).filter((l) => /^login\d+$/.test(l) && +l.slice(5) >= 40));
const managers = new SharedArray('managers', () => Object.keys(tokens).filter((l) => /^login\d+$/.test(l) && +l.slice(5) < 40));
const admins = new SharedArray('admins', () => Object.keys(tokens).filter((l) => l.startsWith('load-admin')));

const serverErrors = new Counter('server_errors');       // 5xx va ulanish xatolari — bo'lmasligi kerak
const throttled = new Counter('throttled');              // 429 (so'rovlar chegarasi)
const unexpected = new Rate('unexpected_status');
const heavy = new Trend('heavy_duration', true);         // hisobot/eksport

function stages(share) {
  const peak = Math.max(1, Math.round(USERS * share));
  if (PROFILE === 'spike') {
    // Ertalab 9:00: hamma birdan kiradi (attestatsiya oxirgi kuni)
    return [{ duration: '30s', target: peak }, { duration: DURATION, target: peak }, { duration: '30s', target: 0 }];
  }
  return [{ duration: '2m', target: peak }, { duration: DURATION, target: peak }, { duration: '1m', target: 0 }];
}

export const options = {
  scenarios: {
    employees: { executor: 'ramping-vus', exec: 'employee', stages: stages(0.75), gracefulRampDown: '30s' },
    managers: { executor: 'ramping-vus', exec: 'manager', stages: stages(0.18), gracefulRampDown: '30s' },
    admins: { executor: 'ramping-vus', exec: 'admin', stages: stages(0.07), gracefulRampDown: '60s' },
  },
  thresholds: {
    // Portal uchun maqsad: oddiy sahifa so'rovi 95% hollarda 500 ms dan tez, xato yo'q
    'http_req_duration{kind:page}': ['p(95)<500', 'p(99)<1500'],
    'http_req_duration{kind:write}': ['p(95)<1000'],
    heavy_duration: ['p(95)<15000'],
    server_errors: ['count==0'],
    unexpected_status: ['rate<0.01'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function pick(list) { return list[Math.floor(Math.random() * list.length)]; }

function call(method, path, login, { body, kind = 'page', ok = [200], name } = {}) {
  const params = {
    headers: { Authorization: `Bearer ${tokens[login]}`, 'Content-Type': 'application/json', 'Accept-Encoding': ACCEPT_ENCODING },
    tags: { kind, name: name || path.replace(/[0-9a-f]{8}-[0-9a-f-]{27}/g, '{id}').replace(/\?.*/, '') },
    timeout: '60s',
  };
  const res = method === 'GET' ? http.get(BASE + path, params) : http.request(method, BASE + path, body ? JSON.stringify(body) : null, params);
  if (res.status === 0 || res.status >= 500) serverErrors.add(1, { name: params.tags.name });
  if (res.status === 429) throttled.add(1, { name: params.tags.name });
  const good = ok.includes(res.status);
  unexpected.add(!good && res.status !== 429, { name: params.tags.name });
  check(res, { [`${params.tags.name} ${ok.join('/')}`]: () => good });
  return res;
}

function json(res) { try { return res.json(); } catch { return null; } }
function think(min = 1, max = 4) { sleep(min + Math.random() * (max - min)); }

// Xodimga VU bo'yicha doimiy login: bir foydalanuvchi bir vaqtda bitta brauzerdan
function loginFor(list) { return list[(exec.vu.idInTest - 1) % list.length]; }

export function employee() {
  const login = loginFor(employees);
  group('xodim: bosh sahifa va arizalar', () => {
    call('GET', '/api/me', login);
    call('GET', '/api/requests?take=20', login);
    think();
    const catalog = json(call('GET', '/api/catalog/roles', login)) || [];
    think(2, 6);
    if (catalog.length > 0 && Math.random() < 0.3) {
      const role = pick(catalog);
      call('POST', '/api/requests/preview', login, { body: { roleId: role.id }, kind: 'write' });
      think(3, 8);
      if (Math.random() < 0.3) {
        // Takror ariza yoki allaqachon bor rol — 400/409 kutilgan javob
        call('POST', '/api/requests', login, { body: { roleId: role.id, justification: 'Yuklama testi' }, kind: 'write', ok: [201, 400, 409, 422] });
      }
    }
  });
  think(5, 15);
}

export function manager() {
  const login = loginFor(managers);
  group('rahbar: attestatsiya va tasdiqlash', () => {
    call('GET', '/api/me', login);
    call('GET', '/api/requests?scope=inbox&take=20', login);
    const page = json(call('GET', '/api/reviews/inbox?take=25', login));
    const inbox = (page && page.items) || [];
    call('GET', '/api/me/team', login);
    think(3, 10);
    if (inbox.length > 0 && Math.random() < 0.5) {
      // Bir nechta yozuvni "qoldirish" — har biri auditga yoziladi
      const ids = inbox.slice(0, 1 + Math.floor(Math.random() * 5)).map((i) => i.id);
      call('POST', '/api/reviews/decide', login, { body: { ids, keep: true, comment: 'Yuklama testi' }, kind: 'write', ok: [200, 400, 409] });
    }
  });
  think(5, 15);
}

const searches = ['Ism1', 'Familiya2', 'T0001', 'Bo', 'Ism99', 'xyz'];
const reports = ['privileged', 'sod', 'orphans', 'leavers'];

export function admin() {
  const login = loginFor(admins);
  group('administrator: ko\'rib chiqish va hisobotlar', () => {
    call('GET', '/api/me', login);
    call('GET', '/api/dashboard', login);
    call('GET', '/api/dashboard/trends?days=30', login);
    think();
    const list = json(call('GET', `/api/identities?search=${pick(searches)}&take=50`, login));
    const items = (list && list.items) || [];
    if (items.length > 0) {
      const id = pick(items).id;
      call('GET', `/api/identities/${id}`, login);
      call('GET', `/api/identities/${id}/access`, login);
      call('GET', `/api/identities/${id}/history`, login);
    }
    think();
    call('GET', `/api/identities?skip=${Math.floor(Math.random() * 9000)}&take=50`, login);
    call('GET', '/api/audit?take=50', login);
    call('GET', '/api/sod/violations?take=50', login);
    call('GET', '/api/reports/orphan-accounts?take=50', login);
    call('GET', '/api/reports/access-drift?take=50', login);
    call('GET', '/api/roles', login);
    think(2, 6);
    if (Math.random() < 0.15) {
      // Og'ir so'rov: butun bank bo'yicha hisobot (Excel)
      const key = Math.random() < 0.3 ? 'access' : pick(reports);
      const res = call('GET', `/api/reports/export/${key}?format=xlsx`, login, { kind: 'heavy', name: '/api/reports/export/{key}' });
      if (res.status === 200) heavy.add(res.timings.duration);
    }
  });
  think(5, 15);
}
