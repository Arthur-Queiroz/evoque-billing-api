# Deploy e CI/CD

O deploy ocorre exclusivamente pelo GitHub Actions. A VPS não faz `git clone`
e nenhum operador executa `docker compose up` manualmente para publicar código.

## Pipeline da API

`.github/workflows/deploy.yml`:

1. compila a imagem `ghcr.io/arthur-queiroz/evoque-billing-api`;
2. publica tags `main` e SHA no GHCR;
3. torna a imagem disponível para pull da VPS;
4. conecta como `root` pela chave de deploy e Cloudflare Access;
5. envia a release de infraestrutura para `/opt/evoque/releases/api-<sha>`;
6. executa o script versionado de deploy, aguarda `/health` e atualiza o link
   `/opt/evoque/current` somente após sucesso.

## Configuração externa obrigatória

- GitHub Secret: `KVM2_DEPLOY_SSH_PRIVATE_KEY`;
- GitHub Variables: `DEPLOY_ENABLED`, `KVM2_DEPLOY_HOST`,
  `KVM2_CLOUDFLARE_SSH_HOST`;
- VPS: Docker, Docker Compose, Cloudflared e `/opt/evoque/production.env`;
- Proxy HTTPS externo: encaminha o domínio para `127.0.0.1:8085`.

`production.env` nunca é enviado pelo workflow. Durante o MVP ele usa somente
credenciais Sandbox do Asaas; uma alteração para Produção exige revisão humana
das variáveis e das regras de negócio.

