# Deploy na KVM2

O repositório da API mantém a composição de produção. API e web são imagens
independentes publicadas no GitHub Container Registry (GHCR); a VPS não clona
nenhum repositório.

## Preparação única na VPS

Com acesso administrativo, execute:

```bash
docker --version
docker compose version
install -d -m 700 /opt/evoque/releases
install -m 600 /dev/null /opt/evoque/production.env
```

Como alternativa, use o script idempotente incluído no repositório:

```bash
sh infra/scripts/bootstrap-kvm2.sh '<chave-publica-ssh-do-github-actions>'
```

Ele não sobrescreve `production.env` e só adiciona a chave pública caso ela
ainda não esteja em `/root/.ssh/authorized_keys`.

Preencha `/opt/evoque/production.env` a partir de
`infra/env/production.env.example`. Durante o MVP, mantenha obrigatoriamente:

```text
MYSQL_PASSWORD=preenchido-pelo-github-actions
ASAAS__INTEGRATIONENVIRONMENT=Sandbox
ASAAS__BASEURL=https://api-sandbox.asaas.com/v3/
ASAAS__ALLOWCHARGECREATION=true
```

`MYSQL_PASSWORD` vem do Secret de ambiente do GitHub e é atualizado pela
pipeline. Não copie a senha para o repositório nem a informe no terminal da
VPS. Depois que o volume do MySQL existe, não troque somente esse Secret: a
rotação precisa atualizar o usuário interno e o Secret no mesmo procedimento.

`true` autoriza somente boletos de teste no Sandbox. Para produção, a troca de
ambiente e a autorização operacional devem ocorrer juntas e de forma explícita.

O proxy HTTPS da KVM2 deve encaminhar o domínio para `127.0.0.1:8085`.

## GitHub Actions

Em **ambos** os repositórios, configure:

- Secret `KVM2_DEPLOY_SSH_PRIVATE_KEY`;
- Variable `DEPLOY_ENABLED=true`;
- Variable `KVM2_DEPLOY_HOST`;
- Variable `KVM2_CLOUDFLARE_SSH_HOST`, por exemplo `ssh.devarthur.com.br`.

No repositório da API, configure também o Environment Secret
`MYSQL_PASSWORD` no ambiente `production`.

A chave pública correspondente deve estar em `/root/.ssh/authorized_keys`. O
Cloudflare Access precisa encaminhar o hostname SSH para a porta 22 da VPS.

As imagens `ghcr.io/arthur-queiroz/evoque-billing-api` e
`ghcr.io/arthur-queiroz/evoque-billing-web` devem ser configuradas como
**públicas** no GHCR, ou a VPS precisará de uma credencial somente-leitura para
executar `docker pull`.

## Ordem do primeiro deploy

1. Publique o repositório web. O workflow cria a imagem do client no GHCR.
2. Confirme que a imagem web está disponível publicamente.
3. Publique o repositório API. Ele cria sua imagem, instala o Compose na VPS e
   sobe quatro containers: MySQL, API, web e Nginx.

Nos deploys posteriores, cada repositório atualiza somente sua própria imagem.
Antes de atualizar a stack, o workflow da API salva um dump comprimido do MySQL
em `/opt/evoque/backups/mysql`, com retenção de 30 dias.
