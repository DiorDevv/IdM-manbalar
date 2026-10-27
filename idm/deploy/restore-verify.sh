#!/usr/bin/env bash
# Zaxira nusxani tekshirish: vaqtinchalik bazaga tiklaydi, nazorat summasini, audit belgisini va butun audit xesh zanjirini tekshiradi,
# keyin vaqtinchalik bazani o'chiradi. Haftada kamida bir marta (zaxira nusxa faqat tiklab ko'rilgandagina ishonchli).
#
#   IDM_PG_ADMIN="host=db-test.bank.uz port=5432 user=postgres dbname=postgres sslmode=verify-full" \
#   IDM_NPGSQL_ADMIN="Host=db-test.bank.uz;Port=5432;Username=postgres;SSL Mode=VerifyFull;Password=..." \
#   ./restore-verify.sh /var/backups/idm/idm-20261027T020000Z.manifest
#
# IDM_API_DLL — Bank.Idm.Api.dll yo'li (audit zanjiri tekshiruvi: "verify-audit" buyrug'i). Natija: 0 — nusxa yaroqli.
set -euo pipefail

MANIFEST=${1:?manifest fayli kerak}
: "${IDM_PG_ADMIN:?IDM_PG_ADMIN (libpq, baza yaratish huquqi bilan) kerak}"
# Eslatma: ${...:?xabar} ichida apostrof bo'lmasin — bash uni qo'shtirnoq deb o'qiydi
: "${IDM_NPGSQL_ADMIN:?IDM_NPGSQL_ADMIN (Npgsql ulanish qatori, Database qismisiz) kerak}"
API=${IDM_API_DLL:-$(dirname "$0")/../src/Bank.Idm.Api/bin/Release/net10.0/Bank.Idm.Api.dll}
DIR=$(dirname "$MANIFEST")
value() { sed -n "s/^$1=//p" "$MANIFEST"; }
FILE="$DIR/$(value file)"
TMPDB="idm_restore_check_$(date -u +%Y%m%d%H%M%S)"
WORK=$(mktemp -d)
CREATED=0
cleanup() {
  rm -rf "$WORK"
  if [ "$CREATED" = 1 ]; then psql "$IDM_PG_ADMIN" -XAtqc "drop database if exists $TMPDB with (force)" >/dev/null || true; fi
}
trap cleanup EXIT

echo "1. Nazorat summasi"
echo "$(value sha256)  $FILE" | sha256sum --check --quiet

DUMP="$FILE"
if [[ "$FILE" == *.gpg ]]; then
  DUMP="$WORK/idm.dump"
  gpg --batch --quiet --output "$DUMP" --decrypt "$FILE"
fi

echo "2. Vaqtinchalik bazaga tiklash: $TMPDB"
CREATED=1   # yaratish yarim yo'lda uzilsa ham tozalashga urinadi ("if exists")
psql "$IDM_PG_ADMIN" -XAtqc "create database $TMPDB"
pg_restore --dbname="$IDM_PG_ADMIN dbname=$TMPDB" --no-owner --no-acl --exit-on-error "$DUMP"

echo "3. Audit belgisi (nusxa olinishidan oldingi oxirgi yozuv) o'z xeshi bilan"
read -r MARK_ID MARK_HASH <<< "$(value audit_marker)" || true
if [ -n "${MARK_ID:-}" ]; then
  [[ "$MARK_ID" =~ ^[0-9]+$ ]] || { echo "XATO: manifestdagi audit belgisi noto'g'ri"; exit 2; }
  FOUND=$(psql "$IDM_PG_ADMIN dbname=$TMPDB" -XAtqc "select hash from audit_events where id = $MARK_ID")
  if [ "$FOUND" != "$MARK_HASH" ]; then echo "XATO: audit #$MARK_ID xeshi mos emas"; exit 2; fi
else
  echo "   nusxa olinganda audit jurnali bo'sh edi"
fi

echo "4. Butun audit zanjiri"
ConnectionStrings__Idm="$IDM_NPGSQL_ADMIN;Database=$TMPDB" dotnet "$API" verify-audit

echo "Zaxira nusxa yaroqli: $(basename "$FILE")"
