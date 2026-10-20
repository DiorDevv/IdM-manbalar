#!/usr/bin/env bash
# Sinov Active Directory'ni (Samba AD DC, domen BANK.TEST) Docker'da ishga tushiradi va sinov xodimlarini yaratadi.
# Qayta ishga tushirish xavfsiz: mavjud domen, xodim va guruhlar qayta yaratilmaydi.
#   LDAPS: ldaps://127.0.0.1:1636 (sertifikat CA'si: .secrets/ad-ca.pem, SAN: 127.0.0.1, localhost, dc1.bank.test).
#   Oddiy LDAP (1389) simple bind'ni rad etadi ("Transport encryption required"), xuddi bankdagi AD kabi.
#   Xizmat akkaunti (IdM uchun, faqat o'qish): .secrets/ad.env dagi AD_SVC_DN / AD_SVC_PASSWORD
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(dirname "$HERE")
NAME=bank-idm-ad; IMAGE=bank-idm-samba-ad:1
SECRETS="$ROOT/.secrets"; ENV="$SECRETS/ad.env"
BASE="DC=bank,DC=test"

command -v docker >/dev/null || { echo "docker o'rnatilmagan"; exit 1; }
mkdir -p "$SECRETS"; chmod 700 "$SECRETS"
if [ ! -f "$ENV" ]; then
  umask 077
  cat > "$ENV" <<EOF
AD_ADMIN_PASSWORD=Adm-$(openssl rand -hex 12)-9aZ
AD_SVC_DN=CN=svc-idm,CN=Users,$BASE
AD_SVC_PASSWORD=Svc-$(openssl rand -hex 12)-7bY
AD_LDAPS_PORT=1636
EOF
  echo "parollar yaratildi: $ENV"
fi
. "$ENV"

docker image inspect "$IMAGE" >/dev/null 2>&1 || docker build -q -t "$IMAGE" "$HERE" >/dev/null
if ! docker inspect "$NAME" >/dev/null 2>&1; then
  docker run -d --name "$NAME" --hostname dc1 --restart unless-stopped \
    -p 127.0.0.1:1389:389 -p "127.0.0.1:$AD_LDAPS_PORT:636" \
    -e AD_ADMIN_PASSWORD="$AD_ADMIN_PASSWORD" -v bank-idm-addata:/var/lib/samba "$IMAGE" >/dev/null
  echo "konteyner yaratildi: $NAME"
else
  docker start "$NAME" >/dev/null; echo "konteyner bor: $NAME"
fi

for _ in $(seq 1 90); do
  [ "$(docker inspect -f '{{.State.Running}}' "$NAME")" = true ] || { echo "AD konteyneri to'xtadi:"; docker logs --tail 20 "$NAME"; exit 1; }
  docker exec "$NAME" test -f /var/lib/samba/private/tls-idm/ca.pem 2>/dev/null \
    && docker exec "$NAME" ldbsearch -H /var/lib/samba/private/sam.ldb -b "$BASE" -s base dn >/dev/null 2>&1 && break
  sleep 2
done
docker cp -q "$NAME:/var/lib/samba/private/tls-idm/ca.pem" "$SECRETS/ad-ca.pem"
ready=0
for _ in $(seq 1 30); do
  LDAPTLS_CACERT="$SECRETS/ad-ca.pem" ldapsearch -x -H "ldaps://127.0.0.1:$AD_LDAPS_PORT" -D "Administrator@bank.test" -w "$AD_ADMIN_PASSWORD" \
    -b "$BASE" -s base dn >/dev/null 2>&1 && { ready=1; break; }
  sleep 2
done
[ $ready = 1 ] || { echo "AD LDAPS'da javob bermadi (sertifikat yoki parol?)"; exit 1; }
echo "AD tayyor: ldaps://127.0.0.1:$AD_LDAPS_PORT ($BASE)"

st() { docker exec "$NAME" samba-tool "$@"; }
exists() { docker exec "$NAME" ldbsearch -H /var/lib/samba/private/sam.ldb -b "$BASE" "$1" dn 2>/dev/null | grep -q '^dn:'; }

# Xizmat akkaunti: oddiy domen foydalanuvchisi (AD'da o'qish uchun yetarli), paroli muddatsiz.
if ! exists "(sAMAccountName=svc-idm)"; then
  st user create svc-idm "$AD_SVC_PASSWORD" --description="IdM sinxronlash (faqat o'qish)" >/dev/null
  st user setexpiry svc-idm --noexpiry >/dev/null
  echo "xizmat akkaunti: svc-idm"
fi
exists "(&(objectClass=organizationalUnit)(ou=Xodimlar))" || st ou create "OU=Xodimlar,$BASE" >/dev/null
exists "(&(objectClass=organizationalUnit)(ou=Guruhlar))" || st ou create "OU=Guruhlar,$BASE" >/dev/null

# login | ism | familiya | tabel | bo'lim | lavozim   (OpenLDAP sinov ma'lumotlari bilan bir xil xodimlar)
while IFS='|' read -r login given sn tabel dept title; do
  [ -n "$login" ] || continue
  if ! exists "(sAMAccountName=$login)"; then
    st user create "$login" 'Parol123!' --given-name="$given" --surname="$sn" --mail-address="$login@bank.test" \
      --department="$dept" --job-title="$title" --userou="OU=Xodimlar" >/dev/null
    dn=$(docker exec "$NAME" ldbsearch -H /var/lib/samba/private/sam.ldb -b "$BASE" "(sAMAccountName=$login)" dn | sed -n 's/^dn: //p')
    printf 'dn: %s\nchangetype: modify\nadd: employeeNumber\nemployeeNumber: %s\n' "$dn" "$tabel" \
      | docker exec -i "$NAME" ldbmodify -H /var/lib/samba/private/sam.ldb >/dev/null
    echo "xodim: $login"
  fi
done <<'EOF'
a.karimov|Aziz|Karimov|TAB-10045|Kredit bo'limi|Kredit mutaxassisi
d.rahimova|Dilnoza|Rahimova|TAB-10046|Kredit bo'limi|Bo'lim boshlig'i
s.toshmatov|Sardor|Toshmatov|TAB-10047|Buxgalteriya|Bosh buxgalter
n.yusupova|Nodira|Yusupova|TAB-10048|Buxgalteriya|Buxgalter
j.aliyev|Jasur|Aliyev|TAB-10049|IT departamenti|Tizim administratori
m.qodirov|Murod|Qodirov|TAB-10050|IT departamenti|Dasturchi
z.ergasheva|Zarina|Ergasheva|TAB-10051|Operatsion bo'lim|Operator
b.xolmatov|Bekzod|Xolmatov|TAB-10052|Axborot xavfsizligi|AX mutaxassisi
EOF

# guruh | a'zolar
while IFS='|' read -r group members; do
  [ -n "$group" ] || continue
  if ! exists "(&(objectClass=group)(sAMAccountName=$group))"; then
    st group add "$group" --groupou="OU=Guruhlar" >/dev/null
    st group addmembers "$group" "$members" >/dev/null
    echo "guruh: $group ($members)"
  fi
done <<'EOF'
kredit-bolimi|a.karimov,d.rahimova
buxgalteriya|s.toshmatov,n.yusupova
it-adminlar|j.aliyev
abs-operatorlar|z.ergasheva,a.karimov,n.yusupova
EOF
echo "Tayyor."
