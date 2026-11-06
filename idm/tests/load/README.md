# Bir vaqtdagi yuklama testi (k6)

`LoadTests` ma'lumot hajmini o'lchaydi: 50 000 xodimni sinxronlash, hisobot. Bu test esa boshqa narsani o'lchaydi:
**ko'p odam portalni bir vaqtda ishlatganda** API qanday javob beradi va qayerda sekinlashadi.

## Nimalar ishga tushadi
- **API** — Release yig'ilgan nusxa, alohida portda (7200). Ishlab turgan 7100'ga tegmaydi.
- **Soxta SSO** (`mock-sso.py`) — OIDC discovery, JWKS va imzolangan access token'lar beradi.
  API tokenni oddiy tartibda tekshiradi: imzo, `iss`, `aud`, muddat va `idm` scope. API kodi o'zgartirilmaydi.
  SimpleIdServer'ning o'zi bu testda o'lchanmaydi.
- **k6** (`portal.js`) — uch xil foydalanuvchi, portaldagi kabi o'ylash vaqti bilan:
  - **xodim (75%)** — bosh sahifa, arizalari, katalog, ariza oldindan ko'rish va yaratish;
  - **rahbar (18%)** — attestatsiya navbati, qaror (auditga yoziladi), tasdiqlash navbati, jamoasi;
  - **administrator (7%)** — bosh sahifa, qidiruv, xodim kartochkasi, audit, SoD, hisobotlar, ba'zan Excel eksport.

## Tayyorlash (bir marta)
1. k6: <https://grafana.com/docs/k6/latest/set-up/install-k6/>. Binar `PATH`da yoki `.local/tools/k6` da bo'lishi kerak
   (`K6=/yo'l/k6` bilan ham ko'rsatish mumkin).
2. Ma'lumot — 10 000 xodimli `idm_load` bazasi. `LoadTests` haqiqiy kod orqali to'ldiradi: HR, AD, rollar, SoD va butun bank
   kampaniyasi. Taxminan 30 soniya:
   ```bash
   . .secrets/postgres.env
   IDM_TEST_POSTGRES="Host=127.0.0.1;Port=5432;Database=postgres;Username=$PG_USER;Password=$PG_PASSWORD" \
   IDM_LOAD_TEST=10000 IDM_LOAD_KEEP_DB=idm_load dotnet test idm/tests/Bank.Idm.Tests -c Release -- --filter-class '*LoadTests'
   ```
   Test ariza va qarorlar yozadi. Shuning uchun natijalarni solishtirishdan oldin bazani shu buyruq bilan qayta yarating.

## Ishga tushirish
```bash
idm/tests/load/run.sh                                  # 300 foydalanuvchi, 10 daqiqa
USERS=1000 DURATION=15m idm/tests/load/run.sh          # katta bank
USERS=500 DURATION=5m PROFILE=spike idm/tests/load/run.sh   # hamma 30 soniyada kiradi (attestatsiyaning oxirgi kuni)
API_ENV="Idm__Limits__Enabled=false" idm/tests/load/run.sh  # chegarasiz (solishtirish uchun)
```
Natijalar `.local/load/results/<vaqt>-<profil>-<son>/` papkasiga yoziladi:
- `k6.txt` va `summary.json` — har bir so'rov turi bo'yicha p95/p99;
- `resources.csv` — har 5 soniyada API xotirasi, CPU va bazaga ulanishlar soni;
- `api.log` — API logi.

## Chegaralar (k6 thresholds)
Bulardan biri buzilsa, `run.sh` xato kodi bilan tugaydi.

| O'lchov | Chegara |
|---|---|
| Oddiy sahifa so'rovi (`kind:page`) | p95 < 500 ms, p99 < 1.5 s |
| Yozish (ariza, qaror) | p95 < 1 s |
| Og'ir hisobot (Excel eksport) | p95 < 15 s |
| 5xx va ulanish xatolari | 0 |
| Kutilmagan javob kodi | < 1% |

429 (so'rovlar chegarasi) alohida `throttled` hisoblagichida ko'rsatiladi. Oddiy foydalanuvchida u bo'lmasligi kerak.
