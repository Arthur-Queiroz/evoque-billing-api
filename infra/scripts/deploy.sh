#!/usr/bin/env sh
set -eu

release_directory="$1"
environment_file="${EVOQUE_ENV_FILE:-/opt/evoque/production.env}"
compose_file="infra/docker-compose.production.yml"

if [ ! -f "$environment_file" ]; then
  echo "Arquivo de ambiente não encontrado: $environment_file" >&2
  exit 1
fi

cd "$release_directory"
sh infra/scripts/backup-mysql.sh "$release_directory"
docker compose --env-file "$environment_file" -f "$compose_file" pull
docker compose --env-file "$environment_file" -f "$compose_file" up -d --remove-orphans
docker compose --env-file "$environment_file" -f "$compose_file" ps

health_port="$(sed -n 's/^EVOQUE_HTTP_PORT=//p' "$environment_file" | tail -n 1)"
health_port="${health_port:-8085}"
health_url="http://127.0.0.1:${health_port}/health"

attempt=1
while [ "$attempt" -le 12 ]; do
  if wget -q -O /dev/null "$health_url"; then
    echo "Health check concluído: $health_url"
    exit 0
  fi

  sleep 5
  attempt=$((attempt + 1))
done

echo "A aplicação não respondeu ao health check: $health_url" >&2
docker compose --env-file "$environment_file" -f "$compose_file" ps >&2
docker compose --env-file "$environment_file" -f "$compose_file" logs \
  --tail 200 api nginx >&2
exit 1
