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

- GitHub Secrets: `KVM2_DEPLOY_SSH_PRIVATE_KEY` e `MYSQL_PASSWORD`, ambos no
  ambiente `production`;
- GitHub Variables: `DEPLOY_ENABLED`, `KVM2_DEPLOY_HOST`,
  `KVM2_CLOUDFLARE_SSH_HOST`;
- VPS: Docker, Docker Compose, Cloudflared e `/opt/evoque/production.env`;
- Proxy HTTPS externo: encaminha o domínio para `127.0.0.1:8085`.

O arquivo completo `production.env` nunca é enviado pelo workflow. A pipeline
atualiza somente `MYSQL_PASSWORD`, recebida do GitHub Environment Secret; as
demais configurações permanecem sob controle da VPS. Durante o MVP, o arquivo
usa somente credenciais Sandbox do Asaas. Uma alteração para Produção exige
revisão humana das variáveis e das regras de negócio.

## Banco e recuperação

- O Compose mantém MySQL 8.4 no serviço interno `mysql`, database
  `evoque_billing`, sem publicar a porta 3306.
- Os dados persistem no volume Docker `mysql_data`; recriar containers não
  remove o catálogo nem os colaboradores.
- Antes de atualizar os containers, o deploy gera um dump comprimido em
  `/opt/evoque/backups/mysql`.
- Os dumps recebem permissão `0600` e são retidos por 30 dias.
- O volume não substitui backup. A restauração deve ser ensaiada antes de
  qualquer migração futura para outra instância MySQL.
- `MYSQL_PASSWORD` inicializa o usuário na primeira criação do volume. Sua
  rotação exige alterar a senha dentro do MySQL e o Secret no mesmo
  procedimento; trocar somente o Secret interrompe a conexão da API.
