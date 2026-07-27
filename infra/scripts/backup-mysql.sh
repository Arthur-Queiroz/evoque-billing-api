#!/usr/bin/env sh
set -eu

release_directory="${1:-}"
environment_file="${EVOQUE_ENV_FILE:-/opt/evoque/production.env}"
backup_directory="${EVOQUE_BACKUP_DIRECTORY:-/opt/evoque/backups/mysql}"
compose_file="infra/docker-compose.production.yml"

if [ -z "$release_directory" ] || [ ! -d "$release_directory" ]; then
  echo "Uso: sh backup-mysql.sh '<diretório-da-release>'" >&2
  exit 1
fi

if [ ! -f "$environment_file" ]; then
  echo "Arquivo de ambiente não encontrado: $environment_file" >&2
  exit 1
fi

cd "$release_directory"
if ! docker compose --env-file "$environment_file" -f "$compose_file" \
  ps --status running --services | grep -qx mysql; then
  echo "MySQL ainda não está em execução; backup anterior ao deploy dispensado."
  exit 0
fi

install -d -m 700 "$backup_directory"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
temporary_file="$backup_directory/evoque-billing-$timestamp.sql.gz.tmp"
backup_file="$backup_directory/evoque-billing-$timestamp.sql.gz"
trap 'rm -f "$temporary_file"' EXIT HUP INT TERM

docker compose --env-file "$environment_file" -f "$compose_file" exec -T mysql \
  sh -c 'exec mysqldump --single-transaction --no-tablespaces --routines --triggers --events -u"$MYSQL_USER" -p"$MYSQL_PASSWORD" "$MYSQL_DATABASE"' \
  | gzip -9 > "$temporary_file"

chmod 600 "$temporary_file"
mv "$temporary_file" "$backup_file"
trap - EXIT HUP INT TERM

find "$backup_directory" -type f -name 'evoque-billing-*.sql.gz' -mtime +30 -delete
echo "Backup MySQL concluído: $backup_file"
