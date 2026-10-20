#!/usr/bin/env bash
# IdM uchun ochiq C# loyihalarni yuklab oladi va build qiladi.
# Ishlatish:  ./setup.sh            (manbalar shu papkadagi manbalar/ ichiga tushadi)
# Talab:      git, .NET 10 SDK (+ eski loyihalar uchun .NET 8), openssl. npm ixtiyoriy.
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
SRC="$HERE/manbalar"
LOGS="$HERE/.logs"
mkdir -p "$SRC" "$LOGS"

say()  { printf '\n\033[1;34m== %s\033[0m\n' "$*"; }
ok()   { printf '   \033[32mOK\033[0m   %s\n' "$*"; }
fail() { printf '   \033[31mXATO\033[0m %s\n' "$*"; }

say "1. Talablarni tekshirish"
command -v git >/dev/null || { fail "git o'rnatilmagan"; exit 1; }
if ! command -v dotnet >/dev/null || ! dotnet --list-sdks | grep -q '^10\.'; then
  fail ".NET 10 SDK topilmadi. Ubuntu'da:  sudo apt install -y dotnet-sdk-10.0 dotnet-sdk-8.0"
  exit 1
fi
ok "dotnet: $(dotnet --list-sdks | awk '{print $1}' | paste -sd' ')"

say "2. Repolarni yuklab olish ($SRC)"
# papka|repo — kategoriyalar MANBALAR.md dagidek
REPOS="
01-provisioning-va-scim|simpleidserver/SimpleIdServer
01-provisioning-va-scim|AzureAD/SCIMReferenceCode
01-provisioning-va-scim|Authentick/SCIM.Client
02-konnektor-freymvork|OpenIdentityPlatform/OpenICF.Net
02-konnektor-freymvork|lithnet/ecma2-framework
02-konnektor-freymvork|lithnet/googleapps-managementagent
03-mim-ekotizimi|microsoft/MIMWAL
03-mim-ekotizimi|lithnet/resourcemanagement-client
03-mim-ekotizimi|lithnet/miis-autosync
04-ad-ldap-va-xavfsizlik|dsbenghe/Novell.Directory.Ldap.NETStandard
04-ad-ldap-va-xavfsizlik|netwrix/pingcastle
04-ad-ldap-va-xavfsizlik|lithnet/ad-password-protection
05-portal-va-avtorizatsiya|Allenvh/SelfService
05-portal-va-avtorizatsiya|rcilogic/ADAuth
05-portal-va-avtorizatsiya|JonPSmith/AuthPermissions.AspNetCore
05-portal-va-avtorizatsiya|Azure-Samples/active-directory-dotnet-external-identities-api-connectors-approvals
"
for line in $REPOS; do
  cat=${line%%|*}; repo=${line#*|}; dest="$SRC/$cat/$(basename "$repo")"
  if [ -d "$dest/.git" ]; then ok "bor: $repo"; continue; fi
  mkdir -p "$SRC/$cat"
  if git clone -q --depth 1 "https://github.com/$repo.git" "$dest"; then ok "$repo"; else fail "$repo"; fi
done
# SimpleIdServer json-ld.net submodule'siz build bo'lmaydi
git -C "$SRC/01-provisioning-va-scim/SimpleIdServer" submodule update --init --depth 1 -q && ok "SimpleIdServer submodule (json-ld.net)"
# Bizning tuzatishlarimiz (patches/*.patch): har biri bir marta qo'llanadi, qayta ishga tushirishda o'tkazib yuboriladi
for p in "$HERE"/patches/simpleidserver-*.patch; do
  [ -e "$p" ] || continue
  repo="$SRC/01-provisioning-va-scim/SimpleIdServer"
  if git -C "$repo" apply --reverse --check "$p" 2>/dev/null; then ok "patch allaqachon bor: $(basename "$p")"
  elif git -C "$repo" apply "$p"; then ok "patch: $(basename "$p")"
  else fail "patch qo'llanmadi: $(basename "$p")"; fi
done
# Login sahifasi mavzusi: SimpleIdServer'ning .gitignore'i wwwroot'ni e'tiborsiz qoldiradi, shuning uchun patch'da emas
cp -r "$HERE/ui/wwwroot/." "$SRC/01-provisioning-va-scim/SimpleIdServer/src/IdServer/SimpleIdServer.IdServer.Startup/wwwroot/" && ok "login mavzusi (ui/wwwroot)"
cp -r "$HERE/ui/website-wwwroot/." "$SRC/01-provisioning-va-scim/SimpleIdServer/src/IdServer/SimpleIdServer.IdServer.Website/wwwroot/" && ok "admin panel mavzusi (ui/website-wwwroot)"

say "3. Build (bir necha daqiqa oladi, loglar: $LOGS)"
SID="$SRC/01-provisioning-va-scim/SimpleIdServer/src"
P5="$SRC/05-portal-va-avtorizatsiya"
# nom|papka|csproj (bo'sh = papkadagi yagona loyiha)
BUILDS="
idserver|$SID/IdServer/SimpleIdServer.IdServer.Startup|
website|$SID/IdServer/SimpleIdServer.IdServer.Website.Startup|
scim-sid|$SID/Scim/SimpleIdServer.Scim.Startup|
scim-reference|$SRC/01-provisioning-va-scim/SCIMReferenceCode/Microsoft.SCIM.WebHostSample|
authpermissions|$P5/AuthPermissions.AspNetCore/Example1.RazorPages.IndividualAccounts|Example1.RazorPages.IndividualAccounts.csproj
adauth|$P5/ADAuth/ADAuth|
selfservice|$P5/SelfService/src/DirectorySelfService|
customapproval|$P5/active-directory-dotnet-external-identities-api-connectors-approvals/CustomApproval.Web|
"
# libman (cdnjs) ishlamasa, frontend kutubxonalarini npm'dan olib qo'yadi
npm_libs() {
  local dir=$1 tmp; command -v npm >/dev/null || return 1
  tmp=$(mktemp -d); (cd "$tmp" && npm pack -s jquery@3.6.3 bootstrap@5.2.3 @popperjs/core@2.11.6 @fortawesome/fontawesome-free@6.5.2 >/dev/null) || return 1
  for f in "$tmp"/*.tgz; do mkdir -p "${f%.tgz}"; tar -xzf "$f" -C "${f%.tgz}"; done
  local W="$dir/wwwroot/lib"; mkdir -p "$W/jquery" "$W/bootstrap" "$W/popper.js" "$W/fontawesome"
  cp "$tmp"/jquery-3.6.3/package/dist/* "$W/jquery/"
  cp -r "$tmp"/bootstrap-5.2.3/package/dist/* "$W/bootstrap/"
  cp -r "$tmp"/popperjs-core-2.11.6/package/dist/umd "$W/popper.js/"
  cp -r "$tmp"/fortawesome-fontawesome-free-6.5.2/package/{css,webfonts} "$W/fontawesome/"
  rm -rf "$tmp"
}
for line in $BUILDS; do
  IFS='|' read -r name dir proj <<<"$line"
  log="$LOGS/build-$name.log"
  (cd "$dir" && dotnet build $proj > "$log" 2>&1)
  if grep -q "LIB002" "$log" && npm_libs "$dir"; then
    (cd "$dir" && dotnet build $proj -p:LibraryRestore=false > "$log" 2>&1)
  fi
  if grep -q "Build succeeded" "$log"; then ok "$name"; else fail "$name  ($log)"; fi
done
dotnet build-server shutdown >/dev/null 2>&1   # build tugagach MSBuild/Razor yordamchi jarayonlarini yopadi

say "4. ADAuth uchun sinov sertifikati va config.json"
CERT="$HERE/.certs"; mkdir -p "$CERT"
if [ ! -f "$CERT/adauth.pfx" ]; then
  openssl req -x509 -newkey rsa:2048 -nodes -keyout "$CERT/adauth.key" -out "$CERT/adauth.crt" -days 365 -subj "/CN=localhost" 2>/dev/null
  openssl pkcs12 -export -out "$CERT/adauth.pfx" -inkey "$CERT/adauth.key" -in "$CERT/adauth.crt" -passout pass:test 2>/dev/null
fi
cat > "$P5/ADAuth/ADAuth/config.json" <<EOF
{
  "AuthTargets": { "app1": "http://localhost:6002/" },
  "tokenExpireTimeInSecondes": "300",
  "host": "127.0.0.1",
  "port": "6003",
  "pfxPath": "$CERT/adauth.pfx",
  "pfxPassword": "test"
}
EOF
ok "config.json yozildi"

say "Tayyor. Keyingi qadam:"
echo "   sudo ./sinov-ldap/install-ldap.sh   # sinov LDAP (bir marta)"
echo "   ./start-all.sh                       # hamma serverlarni ishga tushirish"
