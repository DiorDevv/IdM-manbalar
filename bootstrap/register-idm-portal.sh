#!/usr/bin/env bash
# IdM portali uchun SimpleIdServer'da "idm" ruxsat doirasi (API) va "idm-portal" ilovasini (SPA, PKCE) ro'yxatdan o'tkazadi.
# Qayta ishga tushirish xavfsiz: mavjud bo'lsa o'zgartirmaydi.
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(dirname "$HERE")
IDS=https://localhost:5001/master
PORTAL_URL=${IDM_PORTAL_URL:-http://localhost:5173}
. "$ROOT/.secrets/postgres.env"; . "$ROOT/.secrets/idserver.env"
psql_q() { docker exec -i bank-idm-postgres psql -v ON_ERROR_STOP=1 -U "$PG_USER" -d "$PG_DB" -qtA "$@"; }

T=$(curl -sk -X POST "$IDS/token" --data-urlencode "client_secret=$IDS_MANAGER_SECRET" \
  -d "grant_type=client_credentials&client_id=SIDS-manager&scope=clients scopes" \
  | python3 -c 'import sys,json; print(json.load(sys.stdin)["access_token"])')
post() {
  local code; code=$(curl -sk -o /tmp/.idm-reg.$$ -w '%{http_code}' -X POST "$IDS$1" -H "Authorization: Bearer $T" -H "Content-Type: application/json" -d "$2")
  [[ $code == 2* ]] || { echo "XATO: POST $1 → $code $(head -c 300 /tmp/.idm-reg.$$)"; rm -f /tmp/.idm-reg.$$; exit 1; }
  rm -f /tmp/.idm-reg.$$
}

if [ -z "$(psql_q -c "select 1 from \"Scopes\" where \"Name\" = 'idm'")" ]; then
  # type 1 = APIRESOURCE, protocol 2 = OAUTH
  post /scopes '{"name":"idm","type":1,"protocol":2,"description":"Bank IdM API","is_exposed":false}'
  echo "ruxsat doirasi yaratildi: idm"
fi

if [ -z "$(psql_q -c "select 1 from \"Clients\" where \"ClientId\" = 'idm-portal'")" ]; then
  # Ochiq SPA (maxfiy kalitsiz): avtorizatsiya kodi + PKCE (ochiq ilovaga code_challenge majburiy). Ichki ilova bo'lgani uchun rozilik so'ralmaydi.
  post /clients "$(cat <<EOF
{
  "id": "$(cat /proc/sys/kernel/random/uuid)",
  "client_id": "idm-portal",
  "is_public": true,
  "grant_types": ["authorization_code"],
  "response_types": ["code"],
  "redirect_uris": ["$PORTAL_URL/callback"],
  "post_logout_redirect_uris": ["$PORTAL_URL/"],
  "scope": "openid profile role idm",
  "is_consent_disabled": true
}
EOF
)"
  # client_type API'da raqam sifatida qabul qilinmaydi (500): turini bazada belgilaymiz (0 = SPA)
  psql_q <<'EOF'
update "Clients" set "ClientType" = 0 where "ClientId" = 'idm-portal';
insert into "Translations" ("Key", "Language", "Value", "ClientId")
  select 'client_name', l, v, (select "Id" from "Clients" where "ClientId" = 'idm-portal')
  from (values ('uz', 'Bank IdM portali'), ('en', 'Bank IdM portal')) as t(l, v)
  where not exists (select 1 from "Translations" where "Key" = 'client_name' and "ClientId" = (select "Id" from "Clients" where "ClientId" = 'idm-portal'));
EOF
  echo "ilova yaratildi: idm-portal ($PORTAL_URL/callback)"
fi
