#!/usr/bin/env sh
set -eu

if [ "$(id -u)" -ne 0 ]; then
  echo "Execute este script como root." >&2
  exit 1
fi

if [ "$#" -ne 1 ]; then
  echo "Uso: sh bootstrap-kvm2.sh '<chave-publica-ssh-do-github-actions>'" >&2
  exit 1
fi

deploy_public_key="$1"
environment_file="/opt/evoque/production.env"

docker --version
docker compose version

if ! systemctl is-active --quiet cloudflared; then
  echo "O serviço cloudflared não está ativo. Corrija o Cloudflare Tunnel antes do deploy." >&2
  exit 1
fi

install -d -m 700 /opt/evoque/releases
install -d -m 700 /root/.ssh
touch /root/.ssh/authorized_keys
chmod 600 /root/.ssh/authorized_keys

if ! grep -qxF "$deploy_public_key" /root/.ssh/authorized_keys; then
  printf '%s\n' "$deploy_public_key" >> /root/.ssh/authorized_keys
fi

if [ ! -e "$environment_file" ]; then
  install -m 600 /dev/null "$environment_file"
  echo "Arquivo criado: $environment_file"
else
  echo "Arquivo preservado: $environment_file"
fi

echo "Bootstrap concluído. Preencha $environment_file antes de disparar o deploy."

