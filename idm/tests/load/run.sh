#!/usr/bin/env bash
# Bir vaqtdagi yuklama testi: IdM API (Release) + soxta SSO + k6. Batafsil: tests/load/README.md
#   USERS=300 DURATION=10m PROFILE=load tests/load/run.sh
# Avval ma'lumot: IDM_LOAD_TEST=10000 IDM_LOAD_KEEP_DB=idm_load (README). API alohida portda (7200) ishlaydi, ishlayotgan 7100 ga tegmaydi.
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
IDM=$(cd "$HERE/../.." && pwd)
ROOT=$(cd "$IDM/.." && pwd)
WORK=${WORK:-$ROOT/.local/load}
USERS=${USERS:-300}; DURATION=${DURATION:-10m}; PROFILE=${PROFILE:-load}
DB=${LOAD_DB:-idm_load}; API_PORT=${API_PORT:-7200}; SSO_PORT=${SSO_PORT:-7443}
PSQL=${PSQL:-docker exec bank-idm-postgres psql}   # start-all.sh bilan bir xil
K6=${K6:-$(command -v k6 || echo "$ROOT/.local/tools/k6")}
export PATH=$PATH:$HOME/.dotnet
[ -x "$K6" ] || { echo "k6 topilmadi (K6=... yoki PATH). README.md: o'rnatish"; exit 1; }
[ -f "$ROOT/.secrets/postgres.env" ] && . "$ROOT/.secrets/postgres.env"
: "${PG_USER:?PG_USER kerak}" "${PG_PASSWORD:?PG_PASSWORD kerak}"
PG_HOST=${PG_HOST:-127.0.0.1}; PG_PORT=${PG_PORT:-5432}
mkdir -p "$WORK/results"
STAMP=$(date +%Y%m%d-%H%M%S)
OUT="$WORK/results/$STAMP-$PROFILE-$USERS"
mkdir -p "$OUT"

echo "== API (Release) yig'ilmoqda"
dotnet publish "$IDM/src/Bank.Idm.Api" -c Release -o "$WORK/api" --nologo -v q >/dev/null

pids=()
cleanup() { for p in "${pids[@]}"; do kill "$p" 2>/dev/null || true; done; wait 2>/dev/null || true; }
trap cleanup EXIT

# Xodim, rahbar (login0..39 — bo'lim boshliqlari) va administratorlar uchun tokenlar
users=$(seq -f 'login%g' 0 $((USERS + 40)) | paste -sd,)
admins=$(seq -f 'load-admin%g' 0 29 | paste -sd,)
python3 "$HERE/mock-sso.py" --dir "$WORK" --port "$SSO_PORT" --tokens "$WORK/tokens.json" --users "$users,$admins" > "$OUT/mock-sso.log" 2>&1 &
pids+=($!)
# Tokenlar yozilib, server tinglay boshlaguncha kutish (aks holda API SSO metama'lumotini ololmay 401 beradi)
for _ in $(seq 1 120); do grep -q "^mock-sso:" "$OUT/mock-sso.log" 2>/dev/null && break; sleep 0.5; done
grep -q "^mock-sso:" "$OUT/mock-sso.log" || { echo "Soxta SSO ishga tushmadi: $OUT/mock-sso.log"; cat "$OUT/mock-sso.log"; exit 1; }

admin_env=(); for i in $(seq 0 29); do admin_env+=("Idm__BootstrapAdmins__$i=load-admin$i"); done
( cd "$WORK/api" && exec env ASPNETCORE_ENVIRONMENT=Development \
    ConnectionStrings__Idm="Host=$PG_HOST;Port=$PG_PORT;Database=$DB;Username=$PG_USER;Password=$PG_PASSWORD" \
    Idm__Sso__Authority="https://localhost:$SSO_PORT/master" Idm__Sso__AllowUntrustedCertificate=true \
    Idm__MigrateOnStartup=true Idm__ProvisioningIntervalSeconds=0 \
    Logging__LogLevel__Default=Warning Logging__LogLevel__Bank=Information \
    "${admin_env[@]}" ${API_ENV:-} \
    dotnet Bank.Idm.Api.dll --urls "http://127.0.0.1:$API_PORT" ) > "$OUT/api.log" 2>&1 &
api=$!; pids+=($api)
for _ in $(seq 1 60); do curl -sf -m 2 "http://127.0.0.1:$API_PORT/health" >/dev/null && break; sleep 1; done
curl -sf "http://127.0.0.1:$API_PORT/health" >/dev/null || { echo "API ishga tushmadi: $OUT/api.log"; tail -20 "$OUT/api.log"; exit 1; }
# Haqiqiy tasdiqlashni tekshirish: token qabul qilinadimi
tok=$(python3 -c "import json,sys;print(json.load(open(sys.argv[1]))['load-admin0'])" "$WORK/tokens.json")
code=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $tok" "http://127.0.0.1:$API_PORT/api/me")
[ "$code" = 200 ] || { echo "Token qabul qilinmadi ($code): $OUT/api.log"; exit 1; }

# Har 5 s: API xotirasi/CPU va bazadagi ulanishlar (ulanishlar puli tugashini ko'rish uchun)
(
  echo "time,api_rss_mb,api_cpu_pct,db_connections,db_active"
  while kill -0 "$api" 2>/dev/null; do
    read -r rss cpu < <(ps -o rss=,pcpu= -p "$api" 2>/dev/null || echo "0 0")
    conn=$(PGPASSWORD=$PG_PASSWORD $PSQL -U "$PG_USER" -d postgres -tAc \
      "select count(*)||','||count(*) filter (where state='active') from pg_stat_activity where datname='$DB'" 2>/dev/null || echo ",")
    echo "$(date +%T),$((rss / 1024)),$cpu,$conn"
    sleep 5
  done
) > "$OUT/resources.csv" &
pids+=($!)

echo "== k6: $USERS foydalanuvchi, $DURATION, profil $PROFILE → $OUT"
set +e
"$K6" run --quiet --summary-export "$OUT/summary.json" \
  -e BASE_URL="http://127.0.0.1:$API_PORT" -e TOKENS="$WORK/tokens.json" -e USERS="$USERS" -e DURATION="$DURATION" -e PROFILE="$PROFILE" \
  "$HERE/portal.js" 2>&1 | tee "$OUT/k6.txt"
rc=${PIPESTATUS[0]}
set -e
echo "== Resurslar (eng yuqori): $(awk -F, 'NR>1{if($2>m)m=$2; if($4>c)c=$4; if($3>u)u=$3} END{print "xotira " m " MB, CPU " u "%, bazaga ulanish " c}' "$OUT/resources.csv")"
grep -E "fail|Exception|timeout" -i "$OUT/api.log" | sort | uniq -c | sort -rn | head -10 || true
echo "== Natija: $OUT (k6 chegaralari: $([ $rc = 0 ] && echo bajarildi || echo BUZILDI))"
exit $rc
