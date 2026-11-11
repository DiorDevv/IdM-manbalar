#!/usr/bin/env bash
# IdServer'da (master realm) foydalanuvchi parolini o'rnatadi; foydalanuvchi bo'lmasa yaratadi.
# Parol faylga yozilmaydi: USER_PASSWORD muhit o'zgaruvchisidan yoki so'rab olinadi.
#   ./bootstrap/set-user-password.sh axmadjonov "Ism" "Familiya"
set -euo pipefail
HERE=$(cd "$(dirname "$0")/.." && pwd)
LOGIN=${1:?login kerak}; FIRST=${2:-$LOGIN}; LAST=${3:-}
# Login SQL so'roviga qo'yiladi: faqat xavfsiz belgilar
[[ $LOGIN =~ ^[A-Za-z0-9._-]{1,64}$ ]] || { echo "XATO: login faqat harf, raqam, '.', '_', '-' dan iborat bo'lishi kerak"; exit 1; }
IDS=https://localhost:5001/master
. "$HERE/.secrets/postgres.env"; . "$HERE/.secrets/idserver.env"
[ -n "${USER_PASSWORD:-}" ] || { read -rsp "Yangi parol ($LOGIN): " USER_PASSWORD; echo; }
psql_q() { docker exec -i bank-idm-postgres psql -v ON_ERROR_STOP=1 -U "$PG_USER" -d "$PG_DB" -qtA "$@"; }

T=$(curl -sk -X POST "$IDS/token" --data-urlencode "client_secret=$IDS_MANAGER_SECRET" \
  -d "grant_type=client_credentials&client_id=SIDS-manager&scope=users" \
  | python3 -c 'import sys,json; print(json.load(sys.stdin).get("access_token",""))')
[ -n "$T" ] || { echo "XATO: SIDS-manager tokeni olinmadi"; exit 1; }
api() {  # metod yo'l json
  local out; out=$(mktemp); local code
  code=$(curl -sk -o "$out" -w '%{http_code}' -X "$1" "$IDS$2" -H "Authorization: Bearer $T" -H "Content-Type: application/json" -d "$3")
  [[ $code == 2* ]] || { echo "XATO: $1 $2 → $code $(head -c 300 "$out")"; rm -f "$out"; return 1; }
  rm -f "$out"
}
json() { python3 -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$1"; }

USER_ID=$(psql_q -c "select \"Id\" from \"Users\" where \"Name\" = '$LOGIN'")
if [ -z "$USER_ID" ]; then
  api POST /users "{\"name\":$(json "$LOGIN"),\"firstname\":$(json "$FIRST"),\"lastname\":$(json "$LAST")}"
  USER_ID=$(psql_q -c "select \"Id\" from \"Users\" where \"Name\" = '$LOGIN'")
  echo "foydalanuvchi yaratildi: $LOGIN"
fi
CRED_ID=$(psql_q -c "select \"Id\" from \"UserCredential\" where \"UserId\" = '$USER_ID' and \"CredentialType\" = 'pwd' and \"IsActive\" limit 1")
if [ -n "$CRED_ID" ]; then
  api PUT "/users/$USER_ID/credentials/$CRED_ID" "{\"value\":$(json "$USER_PASSWORD")}"
else
  api POST "/users/$USER_ID/credentials" "{\"active\":true,\"credential\":{\"type\":\"pwd\",\"value\":$(json "$USER_PASSWORD"),\"active\":true}}"
fi
echo "parol o'rnatildi: $LOGIN"
