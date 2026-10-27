#!/usr/bin/env bash
# Bank IdM bazasining zaxira nusxasi (pg_dump, bitta izchil snapshot). Cron yoki systemd taymer bilan, masalan har 6 soatda.
#
#   IDM_PG="host=db.bank.uz port=5432 dbname=idm user=idm_owner sslmode=verify-full" \
#   IDM_BACKUP_GPG_RECIPIENT=idm-backup@bank.uz ./backup.sh /var/backups/idm
#
# Parol — PGPASSFILE (~/.pgpass, 0600) orqali, buyruq qatorida emas.
# Natija: idm-<vaqt>.dump[.gpg] va idm-<vaqt>.manifest (SHA-256, oxirgi audit yozuvi va xeshi — tiklashni tekshirish uchun).
# IDM_BACKUP_KEEP_DAYS (standart 35) kundan eski nusxalar o'chiriladi. Nusxalar boshqa serverga/saqlovchiga ham ko'chirilishi kerak.
set -euo pipefail

DIR=${1:?zaxira katalogi kerak}
: "${IDM_PG:?IDM_PG (libpq ulanish qatori) kerak}"
KEEP=${IDM_BACKUP_KEEP_DAYS:-35}
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
umask 077
mkdir -p "$DIR"
DUMP="$DIR/idm-$STAMP.dump"
MANIFEST="$DIR/idm-$STAMP.manifest"
trap 'rm -f "$DUMP.partial"' EXIT

# Nusxa olinishidan oldingi oxirgi audit yozuvi: nusxada u albatta bor (pg_dump snapshot'i undan keyin olinadi)
MARKER=$(psql "$IDM_PG" -XAtqc "select id || ' ' || hash from audit_events order by id desc limit 1")

# --no-owner --no-acl: tiklashda egalik va huquqlar deploy/db-roles.sql bilan qayta beriladi
pg_dump --dbname="$IDM_PG" --format=custom --compress=9 --no-owner --no-acl --file="$DUMP.partial"
mv "$DUMP.partial" "$DUMP"

if [ -n "${IDM_BACKUP_GPG_RECIPIENT:-}" ]; then
  gpg --batch --yes --trust-model always --recipient "$IDM_BACKUP_GPG_RECIPIENT" --output "$DUMP.gpg" --encrypt "$DUMP"
  rm -f "$DUMP"
  DUMP="$DUMP.gpg"
fi

{
  echo "file=$(basename "$DUMP")"
  echo "sha256=$(sha256sum "$DUMP" | cut -d' ' -f1)"
  echo "created=$STAMP"
  echo "audit_marker=$MARKER"
} > "$MANIFEST"

find "$DIR" -maxdepth 1 -name 'idm-*' -type f -mtime "+$KEEP" -delete
echo "Zaxira nusxa: $DUMP ($(du -h "$DUMP" | cut -f1)), audit #${MARKER%% *}"
