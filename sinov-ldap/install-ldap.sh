#!/usr/bin/env bash
# Sinov OpenLDAP serverini (dc=bank,dc=local) o'rnatadi va bank-test-data.ldif'ni yuklaydi. Ubuntu/Debian uchun.
# Ishlatish: sudo ./sinov-ldap/install-ldap.sh
# Kompyuterda boshqa sozlangan slapd bo'lsa, unga tegmaydi va to'xtaydi.
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
ADMIN_DN="cn=admin,dc=bank,dc=local"
ADMIN_PW="TestAdmin123"
[ "$(id -u)" = 0 ] || { echo "sudo bilan ishga tushiring: sudo $0"; exit 1; }

if dpkg -s slapd >/dev/null 2>&1; then
  service slapd start >/dev/null 2>&1 || true
  if ! ldapsearch -x -H ldap://localhost -b dc=bank,dc=local -D "$ADMIN_DN" -w "$ADMIN_PW" -s base >/dev/null 2>&1; then
    echo "slapd allaqachon o'rnatilgan va boshqa sozlamada. Ma'lumotlaringizni buzmaslik uchun to'xtadim."
    echo "Sinov LDAP'ni alohida mashina yoki konteynerda ko'taring."
    exit 1
  fi
  echo "slapd bor va dc=bank,dc=local sozlangan."
else
  export DEBIAN_FRONTEND=noninteractive
  debconf-set-selections <<EOF
slapd slapd/internal/generated_adminpw password $ADMIN_PW
slapd slapd/internal/adminpw password $ADMIN_PW
slapd slapd/password1 password $ADMIN_PW
slapd slapd/password2 password $ADMIN_PW
slapd slapd/domain string bank.local
slapd shared/organization string Test Bank
slapd slapd/no_configuration boolean false
EOF
  apt-get install -y slapd ldap-utils
  service slapd start
fi

# Sinov ma'lumotlari (bor bo'lsa, o'tkazib yuboriladi)
if ldapsearch -x -H ldap://localhost -b ou=people,dc=bank,dc=local -D "$ADMIN_DN" -w "$ADMIN_PW" -s base >/dev/null 2>&1; then
  echo "Sinov ma'lumotlari allaqachon yuklangan."
else
  ldapadd -x -H ldap://localhost -D "$ADMIN_DN" -w "$ADMIN_PW" -f "$HERE/bank-test-data.ldif" >/dev/null
  echo "Yuklandi: 8 xodim, 4 guruh."
fi

# .NET System.DirectoryServices.Protocols 8.0 Linux'da libldap-2.5.so.0 ni qidiradi.
# Yangi distributivlarda faqat libldap.so.2 (OpenLDAP 2.6) bo'ladi.
LIBDIR=$(dirname "$(ldconfig -p | awk '/libldap\.so\.2 /{print $NF; exit}')")
if [ -n "$LIBDIR" ] && [ ! -e "$LIBDIR/libldap-2.5.so.0" ]; then
  ln -s libldap.so.2 "$LIBDIR/libldap-2.5.so.0"
  ln -sf liblber.so.2 "$LIBDIR/liblber-2.5.so.0"
  echo "libldap-2.5 symlink yaratildi ($LIBDIR)."
fi
echo "LDAP tayyor: ldap://localhost:389  ($ADMIN_DN / $ADMIN_PW)"
