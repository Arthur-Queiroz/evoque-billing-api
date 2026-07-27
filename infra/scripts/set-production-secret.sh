#!/usr/bin/env sh
set -eu

environment_key="${1:-}"
environment_file="${EVOQUE_ENV_FILE:-/opt/evoque/production.env}"

case "$environment_key" in
  MYSQL_PASSWORD)
    ;;
  ASAAS__APIKEY | EVO__USERNAME | EVO__APIKEY)
    ;;
  *)
    echo "Chave de ambiente não permitida: $environment_key" >&2
    exit 1
    ;;
esac

if [ ! -f "$environment_file" ]; then
  echo "Arquivo de ambiente não encontrado: $environment_file" >&2
  exit 1
fi

secret_value="$(cat)"
if [ -z "$secret_value" ]; then
  echo "O valor secreto não pode ser vazio." >&2
  exit 1
fi

temporary_file="${environment_file}.tmp.$$"
trap 'rm -f "$temporary_file"' EXIT HUP INT TERM

umask 077
grep -v "^${environment_key}=" "$environment_file" > "$temporary_file" || true
printf '%s=%s\n' "$environment_key" "$secret_value" >> "$temporary_file"
chmod 600 "$temporary_file"
mv "$temporary_file" "$environment_file"
trap - EXIT HUP INT TERM

echo "Configuração protegida atualizada: $environment_key"
