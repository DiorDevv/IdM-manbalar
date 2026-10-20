#!/usr/bin/env bash
# SimpleIdServer uchun PostgreSQL'ni Docker konteynerida ishga tushiradi (bir marta; qayta ishga tushirish xavfsiz).
# Ma'lumotlar "bank-idm-pgdata" Docker volume'ida saqlanadi: serverlar qayta ishga tushsa ham yo'qolmaydi.
# Parol .secrets/postgres.env faylida (git'ga tushmaydi). start-all.sh shu fayl bo'lsa PostgreSQL'dan foydalanadi.
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
NAME=bank-idm-postgres
IMAGE=postgres:16
SECRETS="$HERE/.secrets"; ENV="$SECRETS/postgres.env"

command -v docker >/dev/null || { echo "docker o'rnatilmagan"; exit 1; }
mkdir -p "$SECRETS"; chmod 700 "$SECRETS"
if [ ! -f "$ENV" ]; then
  umask 077
  cat > "$ENV" <<EOF
PG_USER=idm
PG_PASSWORD=$(openssl rand -hex 24)
PG_DB=idserver
PG_PORT=5432
EOF
  echo "parol yaratildi: $ENV"
fi
. "$ENV"

if ! docker inspect "$NAME" >/dev/null 2>&1; then
  docker run -d --name "$NAME" --restart unless-stopped \
    -p "127.0.0.1:$PG_PORT:5432" \
    -e POSTGRES_USER="$PG_USER" -e POSTGRES_PASSWORD="$PG_PASSWORD" -e POSTGRES_DB="$PG_DB" \
    -v bank-idm-pgdata:/var/lib/postgresql/data "$IMAGE" >/dev/null
  echo "konteyner yaratildi: $NAME (faqat 127.0.0.1:$PG_PORT)"
else
  docker start "$NAME" >/dev/null
  echo "konteyner bor: $NAME"
fi

for _ in $(seq 1 30); do
  docker exec "$NAME" pg_isready -U "$PG_USER" -d "$PG_DB" >/dev/null 2>&1 && { echo "PostgreSQL tayyor"; exit 0; }
  sleep 1
done
echo "PostgreSQL 30 soniyada tayyor bo'lmadi: docker logs $NAME"; exit 1
