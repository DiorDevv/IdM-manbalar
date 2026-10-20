#!/usr/bin/env bash
# IdServer'ning standart (demo) holatini bank uchun xavfsiz holatga keltiradi. Qayta ishga tushirish xavfsiz.
#   1. Admin panel ilovasi (SIDS-manager) sirini almashtiradi: "password" → tasodifiy (.secrets/idserver.env).
#   2. administrator foydalanuvchisi parolini almashtiradi: "password" → tasodifiy.
#   3. Demo ma'lumotlarni o'chiradi: demo ilovalar, administrator-ro, fastFed guruhi, SCIM provisioning.
# O'zgarishlar admin API orqali (SQL emas), faqat API'si yo'q SCIM provisioning bazadan o'chiriladi.
# Chiqish kodi 10: ilova siri almashdi, admin panel shu sir bilan qayta ishga tushirilishi kerak.
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(dirname "$HERE")
IDS=https://localhost:5001/master
SECRETS="$ROOT/.secrets"; ENV="$SECRETS/idserver.env"
. "$SECRETS/postgres.env"
psql_q() { docker exec -i bank-idm-postgres psql -v ON_ERROR_STOP=1 -U "$PG_USER" -d "$PG_DB" -qtA "$@"; }

if [ ! -f "$ENV" ]; then
  umask 077
  cat > "$ENV" <<EOF
IDS_ADMIN_PASSWORD=Adm-$(openssl rand -hex 10)-7Qz
IDS_MANAGER_SECRET=$(openssl rand -hex 24)
EOF
  echo "yangi parol va sir yaratildi: $ENV"
fi
. "$ENV"

token() {  # $1 = sir
  curl -sk -X POST "$IDS/token" --data-urlencode "client_secret=$1" \
    -d "grant_type=client_credentials&client_id=SIDS-manager&scope=clients users groups" \
    | python3 -c 'import sys,json; print(json.load(sys.stdin).get("access_token",""))' 2>/dev/null || true
}
api() {  # metod yo'l [json]
  local code; code=$(curl -sk -o /tmp/.ids-api.$$ -w '%{http_code}' -X "$1" "$IDS$2" -H "Authorization: Bearer $T" \
    -H "Content-Type: application/json" ${3:+-d "$3"})
  [[ $code == 2* ]] || { echo "XATO: $1 $2 → $code $(head -c 300 /tmp/.ids-api.$$)"; rm -f /tmp/.ids-api.$$; return 1; }
  rm -f /tmp/.ids-api.$$
}

rc=0
MANAGER_ID=$(psql_q -c "select \"Id\" from \"Clients\" where \"ClientId\" = 'SIDS-manager'")
T=$(token "$IDS_MANAGER_SECRET")
if [ -z "$T" ]; then
  T=$(token password)
  [ -n "$T" ] || { echo "SIDS-manager sirini aniqlab bo'lmadi (na yangi, na standart ishlamadi)"; exit 1; }
  # yangi sir qo'shiladi, keyin eskisi(lari) o'chiriladi: qiymatlar xeshlangan, shuning uchun "eng yangisidan boshqasi"
  api POST "/clients/$MANAGER_ID/secrets" "{\"value\":\"$IDS_MANAGER_SECRET\",\"alg\":5}"   # 5 = SHA512 (PBKDF2 ilova siri uchun ishlamaydi)
  NEWEST=$(psql_q -c "select \"Id\" from \"ClientSecret\" where \"ClientId\" = '$MANAGER_ID' order by \"CreateDateTime\" desc limit 1")
  for old in $(psql_q -c "select \"Id\" from \"ClientSecret\" where \"ClientId\" = '$MANAGER_ID' and \"Id\" <> '$NEWEST'"); do
    api DELETE "/clients/$MANAGER_ID/secrets/$old"
  done
  T=$(token "$IDS_MANAGER_SECRET")
  [ -n "$T" ] || { echo "XATO: yangi sir bilan token olinmadi"; exit 1; }
  [ -z "$(token password)" ] || { echo "XATO: eski 'password' siri hali ishlayapti"; exit 1; }
  echo "SIDS-manager siri almashtirildi"
  rc=10
fi

# administrator paroli (har safar fayldagi qiymatga keltiriladi)
ADMIN_ID=$(psql_q -c "select \"Id\" from \"Users\" where \"Name\" = 'administrator'")
CRED_ID=$(psql_q -c "select \"Id\" from \"UserCredential\" where \"UserId\" = '$ADMIN_ID' and \"CredentialType\" = 'pwd' and \"IsActive\" limit 1")
api PUT "/users/$ADMIN_ID/credentials/$CRED_ID" "{\"value\":\"$IDS_ADMIN_PASSWORD\"}" && echo "administrator paroli o'rnatildi"

# demo ma'lumotlar
for cid in applicationProvider CredentialIssuer CredentialIssuer-manager fastFed identityProvider scimClient swaggerClient; do
  id=$(psql_q -c "select \"Id\" from \"Clients\" where \"ClientId\" = '$cid'")
  [ -n "$id" ] && api DELETE "/clients/$id" && echo "demo ilova o'chirildi: $cid"
done
uid=$(psql_q -c "select \"Id\" from \"Users\" where \"Name\" = 'administrator-ro'")
[ -n "$uid" ] && api DELETE "/users/$uid" && echo "demo foydalanuvchi o'chirildi: administrator-ro"
for g in administrator-ro fastFedAdministrator; do
  [ -n "$(psql_q -c "select 1 from \"Groups\" where \"FullPath\" = '$g'")" ] && api POST "/groups/delete" "{\"full_path\":\"$g\"}" && echo "demo guruh o'chirildi: $g"
done
n=$(psql_q -c "with d as (delete from \"IdentityProvisioningLst\" where \"DefinitionName\" = 'SCIM' returning 1) select count(*) from d")
[ "$n" != 0 ] && echo "demo SCIM provisioning o'chirildi"
exit $rc
