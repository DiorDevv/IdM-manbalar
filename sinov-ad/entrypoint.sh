#!/bin/bash
# Birinchi ishga tushishda BANK.TEST domenini yaratadi va LDAPS sertifikatini (SAN: localhost) tayyorlaydi.
# Hamma holat /var/lib/samba volume'ida: konteyner qayta yaratilsa ham domen saqlanadi.
set -euo pipefail
CONF=/var/lib/samba/etc/smb.conf
TLS=/var/lib/samba/private/tls-idm
DONE=/var/lib/samba/.provisioned   # faqat muvaffaqiyatli provision'dan keyin yoziladi
if [ ! -f "$DONE" ]; then
  : "${AD_ADMIN_PASSWORD:?AD_ADMIN_PASSWORD kerak}"
  # Yarim qolgan oldingi urinishni tozalash
  rm -rf /var/lib/samba/private/* /var/lib/samba/sysvol/* /var/lib/samba/etc /etc/samba/smb.conf
  samba-tool domain provision --realm=BANK.TEST --domain=BANK --server-role=dc --dns-backend=SAMBA_INTERNAL \
    --host-name=dc1 --adminpass="$AD_ADMIN_PASSWORD" --option="dns forwarder = 127.0.0.11" \
    --option="acl_xattr:security_acl_name = user.NTACL"   # security.* xattr imtiyozsiz konteynerda yozilmaydi
  mkdir -p "$TLS" /var/lib/samba/etc
  openssl req -x509 -newkey rsa:2048 -nodes -keyout "$TLS/ca.key" -out "$TLS/ca.pem" -days 825 -subj "/CN=Bank IdM sinov CA" 2>/dev/null
  openssl req -newkey rsa:2048 -nodes -keyout "$TLS/key.pem" -out "$TLS/req.csr" -subj "/CN=dc1.bank.test" 2>/dev/null
  printf 'subjectAltName=DNS:dc1.bank.test,DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\n' > "$TLS/ext.cnf"
  openssl x509 -req -in "$TLS/req.csr" -CA "$TLS/ca.pem" -CAkey "$TLS/ca.key" -CAcreateserial -out "$TLS/cert.pem" -days 825 -extfile "$TLS/ext.cnf" 2>/dev/null
  chmod 600 "$TLS/key.pem" "$TLS/ca.key"
  sed -i "/^\[global\]/a\\	tls enabled = yes\n\ttls keyfile = $TLS/key.pem\n\ttls certfile = $TLS/cert.pem\n\ttls cafile = $TLS/ca.pem" /etc/samba/smb.conf
  mv /etc/samba/smb.conf "$CONF"
  touch "$DONE"
fi
ln -sf "$CONF" /etc/samba/smb.conf
exec samba -i --debuglevel=1
