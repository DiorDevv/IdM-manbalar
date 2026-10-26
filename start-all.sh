#!/usr/bin/env bash
# Barcha serverlarni fonda ishga tushiradi va LDAP'dan sinxronlaydi. To'xtatish: ./stop-all.sh
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
SRC="$HERE/manbalar"
SID="$SRC/01-provisioning-va-scim/SimpleIdServer/src"
P5="$SRC/05-portal-va-avtorizatsiya"
LOGS="$HERE/.logs"; RUN="$HERE/.run"
mkdir -p "$LOGS" "$RUN"
[ -d "$SID" ] || { echo "Avval ./setup.sh ni ishga tushiring."; exit 1; }

export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ROLL_FORWARD=Major           # .NET 3.1/6 namunalari .NET 10 runtime'da ishlaydi
# SimpleIdServer standart sozlamasi trace'larni muallifning Honeycomb hisobiga yuboradi: o'chiramiz.
export OpenTelemetryOptions__EnableOtpExported=false
export OpenTelemetryOptions__EnableConsoleExporter=false
# Login sahifasi tili: foydalanuvchi tanlamaguncha o'zbekcha (ui/apply-ui.sh bazaga "uz" tilini qo'shadi)
export DefaultLanguage=uz

# Xodimlar manbai: "ad" (sinov Active Directory, sinov-ad/install-ad.sh) yoki "openldap" (sinov-ldap/install-ldap.sh).
# Standart: sinov AD o'rnatilgan bo'lsa "ad". Tanlash: LDAP_SOURCE=openldap ./start-all.sh
LDAP_SOURCE=${LDAP_SOURCE:-$([ -f "$HERE/.secrets/ad.env" ] && echo ad || echo openldap)}
P=LDAP__LDAPRepresentationsExtractionJobOptions__
export ${P}BatchSize=50 ${P}MaxDeprovisioningPercentage=20 ${P}IsGroupSyncEnabled=true
case "$LDAP_SOURCE" in
  ad)
    . "$HERE/.secrets/ad.env"
    docker start bank-idm-ad >/dev/null 2>&1 || true
    # LDAPS: libldap sertifikatni shu CA bilan tekshiradi (sertifikatda 127.0.0.1 bor)
    export LDAPTLS_CACERT="$HERE/.secrets/ad-ca.pem"
    export ${P}Server=127.0.0.1 ${P}Port="$AD_LDAPS_PORT" ${P}UseSsl=true
    export ${P}BindDN="$AD_SVC_DN" ${P}BindCredentials="$AD_SVC_PASSWORD"
    export ${P}UsersDN="OU=Xodimlar,DC=bank,DC=test" ${P}UserObjectClasses=user
    export ${P}GroupsDN="OU=Guruhlar,DC=bank,DC=test" ${P}GroupObjectClasses=group
    export ${P}RetrievingStrategies=LOAD_FROM_USER_MEMBEROF_ATTRIBUTE ${P}MemberOfAttribute=memberOf
    export ${P}UserIdLDAPAttribute=objectGUID ${P}GroupIdLDAPAttribute=objectGUID ${P}ModificationDateAttribute=whenChanged
    export ${P}DisabledUserFilter="(userAccountControl:1.2.840.113556.1.4.803:=2)"
    export LDAP_LOGIN_ATTRIBUTE=sAMAccountName LDAP_DEPARTMENT_ATTRIBUTE=department
    # IdM AD konnektori (idm/): standart sozlamalar AD uchun, faqat ulanish va OU'lar beriladi
    idm_connector_env() {
      local C=Idm__Connectors__AD__
      export ${C}Name="Active Directory" ${C}Description="Sinov AD (BANK.TEST)" ${C}PrivilegedGroups__0=it-adminlar
      export ${C}Ldap__Server=127.0.0.1 ${C}Ldap__Port="$AD_LDAPS_PORT" ${C}Ldap__UseSsl=true
      export ${C}Ldap__BindDn="$AD_SVC_DN" ${C}Ldap__BindPassword="$AD_SVC_PASSWORD"
      export ${C}Ldap__UsersDn="OU=Xodimlar,DC=bank,DC=test" ${C}Ldap__GroupsDn="OU=Guruhlar,DC=bank,DC=test"
    } ;;
  openldap)
    OPENLDAP_PORT=${OPENLDAP_PORT:-389}
    export ${P}Server=localhost ${P}Port="$OPENLDAP_PORT" ${P}UseSsl=false ${P}BindDN="cn=admin,dc=bank,dc=local" ${P}BindCredentials=TestAdmin123
    export ${P}UsersDN="ou=people,dc=bank,dc=local" ${P}UserObjectClasses=inetOrgPerson
    export ${P}GroupsDN="ou=groups,dc=bank,dc=local" ${P}GroupObjectClasses=posixGroup
    export ${P}MembershipLDAPAttribute=memberUid ${P}MembershipUserLDAPAttribute=uid ${P}RetrievingStrategies=LOAD_BY_MEMBER_ATTRIBUTE
    export ${P}UserIdLDAPAttribute=uidNumber ${P}GroupIdLDAPAttribute=gidNumber ${P}ModificationDateAttribute=modifyTimestamp
    # Sinov LDAP'ida ishdan ketgan xodim: employeeType=disabled
    export ${P}DisabledUserFilter="(employeeType=disabled)"
    export LDAP_LOGIN_ATTRIBUTE=uid LDAP_DEPARTMENT_ATTRIBUTE=departmentNumber
    # IdM konnektori OpenLDAP uchun: atributlar va posixGroup a'zoligi (memberUid = login)
    idm_connector_env() {
      local C=Idm__Connectors__AD__
      export ${C}Name="Sinov LDAP (OpenLDAP)" ${C}PrivilegedGroups__0=it-adminlar
      export ${C}Ldap__Server=127.0.0.1 ${C}Ldap__Port="$OPENLDAP_PORT" ${C}Ldap__UseSsl=false
      export ${C}Ldap__BindDn="cn=admin,dc=bank,dc=local" ${C}Ldap__BindPassword=TestAdmin123
      export ${C}Ldap__UsersDn="ou=people,dc=bank,dc=local" ${C}Ldap__UserFilter="(objectClass=inetOrgPerson)" ${C}Ldap__DisabledFilter="(employeeType=disabled)"
      export ${C}Ldap__GroupsDn="ou=groups,dc=bank,dc=local" ${C}Ldap__GroupFilter="(objectClass=posixGroup)"
      export ${C}Ldap__UserIdAttribute=entryUUID ${C}Ldap__GroupIdAttribute=entryUUID ${C}Ldap__LoginAttribute=uid
      export ${C}Ldap__DepartmentAttribute=departmentNumber ${C}Ldap__GroupMemberAttribute=memberUid ${C}Ldap__GroupMemberIsDn=false
      # Tizimga yozish (G) OpenLDAP uchun: yangi akkaunt inetOrgPerson, bloklash — employeeType=disabled
      export ${C}Ldap__CreateObjectClasses__0=inetOrgPerson ${C}Ldap__RdnAttribute=uid ${C}Ldap__DisableAttribute=employeeType
    } ;;
  *) echo "LDAP_SOURCE noma'lum: $LDAP_SOURCE (ad yoki openldap)"; exit 1 ;;
esac
echo "== Xodimlar manbai: $LDAP_SOURCE"
# Jadval bo'yicha sinxronlash (Hangfire cron). Admin panel: Recurring jobs → IdentityProvisioningSyncJob.
export IdentityProvisioningSync__Cron="${SYNC_CRON:-*/15 * * * *}"
export IdentityProvisioningSync__Definitions=LDAP   # demo SCIM provisioning sinxronlanmaydi

# Doimiy saqlash: ./install-postgres.sh ishga tushirilgan bo'lsa IdServer ma'lumotlari (foydalanuvchilar, sessiyalar,
# data-protection kalitlari, Hangfire) PostgreSQL'da saqlanadi. Aks holda xotirada (INMEMORY).
if [ -f "$HERE/.secrets/postgres.env" ]; then
  . "$HERE/.secrets/postgres.env"
  docker start bank-idm-postgres >/dev/null 2>&1 || true
  export StorageConfiguration__Type=POSTGRE
  export StorageConfiguration__ConnectionString="Host=127.0.0.1;Port=$PG_PORT;Database=$PG_DB;Username=$PG_USER;Password=$PG_PASSWORD"
  echo "== Baza: PostgreSQL (127.0.0.1:$PG_PORT/$PG_DB)"
else
  echo "== Baza: INMEMORY (doimiy saqlash uchun: ./install-postgres.sh)"
fi

# nom | papka | dotnet run argumentlari. Har biri alohida jarayon guruhida (setsid): stop-all.sh butun guruhni to'xtatadi.
start() {
  local name=$1 dir=$2 args=$3
  if [ -f "$RUN/$name.pid" ] && kill -0 "$(cat "$RUN/$name.pid")" 2>/dev/null; then
    echo "   ishlayapti: $name"; return
  fi
  # args "cmd:" bilan boshlansa, dotnet emas, o'sha buyruq ishga tushiriladi (masalan portal uchun npm)
  local cmd="dotnet run --no-build $args"; [[ $args == cmd:* ]] && cmd=${args#cmd:}
  ( cd "$dir" || exit 1; eval "exec setsid $cmd" ) > "$LOGS/$name.log" 2>&1 < /dev/null &
  echo $! > "$RUN/$name.pid"
  echo "   ishga tushdi: $name"
}
wait_url() {
  local name=$1 url=$2
  for _ in $(seq 1 60); do
    curl -sk -o /dev/null -m 3 "$url" && { printf '   \033[32mtayyor\033[0m %-16s %s\n' "$name" "$url"; return 0; }
    grep -q "Unhandled exception" "$LOGS/$name.log" 2>/dev/null && break
    sleep 2
  done
  printf '   \033[31mXATO\033[0m   %-16s log: %s\n' "$name" "$LOGS/$name.log"; return 1
}

echo "== Serverlarni ishga tushirish"
idserver_was_running=0
[ -f "$RUN/idserver.pid" ] && kill -0 "$(cat "$RUN/idserver.pid")" 2>/dev/null && idserver_was_running=1
start idserver        "$SID/IdServer/SimpleIdServer.IdServer.Startup"         ""
wait_url idserver https://localhost:5001/master/.well-known/openid-configuration
# Login sahifasi: o'zbek tili, bank uchun keraksiz elementlar (faqat PostgreSQL: INMEMORY'da baza har safar yangi)
if [ -n "${StorageConfiguration__Type:-}" ]; then
  "$HERE/ui/apply-ui.sh" | sed 's/^/   /'; rc=${PIPESTATUS[0]}
  # Standart parol va demo ma'lumotlar (bootstrap/secure-defaults.sh): admin panel ilovasi siri .secrets/idserver.env da
  "$HERE/bootstrap/secure-defaults.sh" | sed 's/^/   /'; sec_rc=${PIPESTATUS[0]}
  # yangi sir faqat muvaffaqiyatli almashtirilgandan keyin ishlatiladi (aks holda admin panel va sync eski sir bilan qoladi)
  if [ "$sec_rc" = 0 ] || [ "$sec_rc" = 10 ]; then . "$HERE/.secrets/idserver.env"; export IDS_MANAGER_SECRET; fi
  if [ "$sec_rc" = 10 ] && [ -f "$RUN/website.pid" ] && kill -0 "$(cat "$RUN/website.pid")" 2>/dev/null; then
    kill -- "-$(cat "$RUN/website.pid")" 2>/dev/null; rm -f "$RUN/website.pid"; sleep 1   # yangi sir bilan qayta ishga tushadi
  fi
  if [ "$rc" = 10 ]; then   # tillar IdServer ishga tushganda o'qiladi
    kill -- "-$(cat "$RUN/idserver.pid")" 2>/dev/null; rm -f "$RUN/idserver.pid"; sleep 2
    start idserver "$SID/IdServer/SimpleIdServer.IdServer.Startup" ""
    wait_url idserver https://localhost:5001/master/.well-known/openid-configuration
  fi
fi
# INMEMORY rejimida IdServer kalitlari restartda yo'qoladi va admin panel keshlagan token yaroqsiz bo'ladi
# (/master/realms 401 → /availablerealms). PostgreSQL rejimida kalitlar saqlanadi, bu qadam kerak emas.
if [ "${StorageConfiguration__Type:-INMEMORY}" != POSTGRE ] && [ $idserver_was_running = 0 ] && [ -f "$RUN/website.pid" ] && kill -0 "$(cat "$RUN/website.pid")" 2>/dev/null; then
  kill -- "-$(cat "$RUN/website.pid")" 2>/dev/null; rm -f "$RUN/website.pid"; sleep 1
  echo "   to'xtatildi: website (IdServer yangilangani uchun qayta ishga tushadi)"
fi
ClientSecret="${IDS_MANAGER_SECRET:-password}" start website "$SID/IdServer/SimpleIdServer.IdServer.Website.Startup" "--launch-profile SimpleIdServer.IdServer.Website.Startup"
start scim-sid        "$SID/Scim/SimpleIdServer.Scim.Startup"                 ""
start scim-reference  "$SRC/01-provisioning-va-scim/SCIMReferenceCode/Microsoft.SCIM.WebHostSample" "--no-launch-profile --urls http://localhost:6001"
start authpermissions "$P5/AuthPermissions.AspNetCore/Example1.RazorPages.IndividualAccounts" "--no-launch-profile --project Example1.RazorPages.IndividualAccounts.csproj --urls http://localhost:6002"
start adauth          "$P5/ADAuth/ADAuth"                                     "--no-launch-profile"
start selfservice     "$P5/SelfService/src/DirectorySelfService"              "--no-launch-profile --urls 'http://localhost:6004;https://localhost:6014'"
start customapproval  "$P5/active-directory-dotnet-external-identities-api-connectors-approvals/CustomApproval.Web" "--no-launch-profile --urls http://localhost:6005"

# IdM yadrosi (idm/): API + portal. Faqat PostgreSQL rejimida (o'z "idm" bazasi bor).
if [ -n "${StorageConfiguration__Type:-}" ] && [ -d "$HERE/idm/src/Bank.Idm.Api/bin" ]; then
  docker exec bank-idm-postgres psql -U "$PG_USER" -d postgres -tAc "select 1 from pg_database where datname = 'idm'" | grep -q 1 \
    || docker exec bank-idm-postgres psql -U "$PG_USER" -d postgres -qc "create database idm"
  "$HERE/bootstrap/register-idm-portal.sh" | sed 's/^/   /'
  # Konnektor sozlamalari (bind paroli bilan) faqat IdM API jarayoniga beriladi
  ( idm_connector_env
  # Tizimga yozish (G bosqichi): standart o'chiq. Yoqish: PROVISIONING_MODE=Approval ./start-all.sh (dry-run, portalda tasdiq)
  # yoki Automatic. PROVISIONING_CREATE=true — akkauntsiz yangi xodimga bloklangan akkaunt yaratish
  export Idm__Connectors__AD__Provisioning__Mode="${PROVISIONING_MODE:-Off}" Idm__Connectors__AD__Provisioning__CreateAccounts="${PROVISIONING_CREATE:-false}"
  export Idm__Connectors__AD__Provisioning__ProtectedLogins__0=administrator
  # HR (haqiqat manbai): eksport fayli. HR ulanganda xodimlar faqat HR'dan yaratiladi, AD akkauntlari ularga bog'lanadi
  HR_FILE=${HR_FILE:-$HERE/sinov-hr/xodimlar.csv}
  if [ -f "$HR_FILE" ]; then
    export Idm__Connectors__HR__Type=hr-csv Idm__Connectors__HR__Name="Kadrlar tizimi" Idm__Connectors__HR__Description="HR eksport fayli (CSV)"
    export Idm__Connectors__HR__Csv__Path="$HR_FILE" Idm__Connectors__AD__CreateIdentities=false
  fi
  ConnectionStrings__Idm="Host=127.0.0.1;Port=$PG_PORT;Database=idm;Username=$PG_USER;Password=$PG_PASSWORD" \
  Idm__BootstrapAdmins__0=administrator \
    start idm-api "$HERE/idm/src/Bank.Idm.Api" "--no-launch-profile --urls http://localhost:7100" )
  [ -d "$HERE/idm/portal/node_modules" ] && start idm-portal "$HERE/idm/portal" "cmd:npm run dev"
fi

echo "== Tayyorlikni kutish"
wait_url website         https://localhost:5002/master/users
wait_url scim-sid        https://localhost:5003/Schemas
wait_url scim-reference  http://localhost:6001/scim/token
wait_url authpermissions http://localhost:6002/
wait_url adauth          https://127.0.0.1:6003/
wait_url selfservice     https://localhost:6014/
wait_url customapproval  http://localhost:6005/
[ -f "$RUN/idm-api.pid" ] && wait_url idm-api http://localhost:7100/health
[ -f "$RUN/idm-portal.pid" ] && wait_url idm-portal http://localhost:5173/

echo "== LDAP sinxronlash ($LDAP_SOURCE)"
if [ "$LDAP_SOURCE" = ad ]; then
  source_ok() { ldapsearch -x -H "ldaps://127.0.0.1:$AD_LDAPS_PORT" -D "$AD_SVC_DN" -w "$AD_SVC_PASSWORD" -b DC=bank,DC=test -s base dn >/dev/null 2>&1; }
  hint="./sinov-ad/install-ad.sh"
else
  source_ok() { ldapsearch -x -H "ldap://localhost:$OPENLDAP_PORT" -b dc=bank,dc=local -s base -D cn=admin,dc=bank,dc=local -w TestAdmin123 >/dev/null 2>&1; }
  hint="sudo ./sinov-ldap/install-ldap.sh"
fi
# Manba almashganda (masalan OpenLDAP → AD) eski manbadan import qilingan xodim va guruhlarni tozalaymiz:
# ID'lar boshqacha (uidNumber ↔ objectGUID), loginlar esa bir xil, aks holda bitta login ikki marta bo'lib qoladi.
LAST="$HERE/.secrets/ldap-source"
if [ -n "${StorageConfiguration__Type:-}" ] && [ -f "$LAST" ] && [ "$(cat "$LAST")" != "$LDAP_SOURCE" ]; then
  psql_idm() { docker exec bank-idm-postgres psql -U "$PG_USER" -d "$PG_DB" -qtAc "$1"; }
  echo "   manba o'zgardi: $(cat "$LAST") → $LDAP_SOURCE, eski import tozalanadi"
  psql_idm "delete from \"Groups\" where \"Id\" in (select \"ExternalId\" from \"ExtractedRepresentations\"); delete from \"Users\" where \"Source\" = 'LDAP'; delete from \"ExtractedRepresentationsStaging\"; delete from \"ExtractedRepresentations\";"
fi
if source_ok; then
  "$HERE/sinov-ldap/sync-ldap.sh" | tail -2
  [ -n "${StorageConfiguration__Type:-}" ] && echo "$LDAP_SOURCE" > "$LAST"
else
  echo "   Xodimlar manbai ($LDAP_SOURCE) topilmadi, o'tkazib yuborildi (o'rnatish: $hint)"
fi

cat <<'EOF'

== Brauzerda oching (sertifikat ogohlantirishini qabul qiling)
   IdM portali:       http://localhost:5173                   administrator / .secrets/idserver.env (IDS_ADMIN_PASSWORD)
   Admin panel:       https://localhost:5002/master/users     administrator / .secrets/idserver.env (IDS_ADMIN_PASSWORD)
   IdServer login:    https://localhost:5001/master
   AuthPermissions:   http://localhost:6002                   Super@g1.com / Super@g1.com
   SelfService:       https://localhost:6014
   CustomApproval:    http://localhost:6005
   SCIM (API):        https://localhost:5003/Schemas, http://localhost:6001/scim/token
EOF
