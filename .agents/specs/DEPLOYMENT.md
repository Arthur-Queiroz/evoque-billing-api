# Deploy e CI/CD

O deploy ocorre exclusivamente pelo GitHub Actions. A VPS não faz `git clone`
e nenhum operador executa `docker compose up` manualmente para publicar código.

## Pipeline da API

`.github/workflows/deploy.yml`:

1. compila a imagem `ghcr.io/arthur-queiroz/evoque-billing-api`;
2. publica tags `main` e SHA no GHCR;
3. torna a imagem disponível para pull da VPS;
4. conecta como `root` pela chave de deploy e Cloudflare Access;
5. autentica o Docker da VPS no GHCR com o token efêmero do workflow;
6. envia a release de infraestrutura para `/opt/evoque/releases/api-<sha>`;
7. executa o script versionado de deploy, aguarda `/health` e atualiza o link
   `/opt/evoque/current` somente após sucesso.

Se o healthcheck falhar, o script inclui no log do Actions o estado dos
containers e as últimas linhas apenas da API e do proxy. Logs do MySQL não são
impressos porque a imagem oficial informa uma senha root aleatória na primeira
inicialização.

## Configuração externa obrigatória

- GitHub Secret de repositório: `KVM2_DEPLOY_SSH_PRIVATE_KEY`;
- GitHub Environment Secrets em `production`: `MYSQL_PASSWORD`,
  `ASAAS_API_KEY`, `ASAAS_PRODUCTION_API_KEY`, `EVO_USERNAME` e `EVO_API_KEY`;
- GitHub Variables: `DEPLOY_ENABLED`, `KVM2_DEPLOY_HOST`,
  `KVM2_CLOUDFLARE_SSH_HOST`;
- VPS: Docker, Docker Compose, Cloudflared e `/opt/evoque/production.env`;
- Cloudflare Tunnel: encaminha `evoque.devarthur.com.br` para
  `127.0.0.1:8088`.

O arquivo completo `production.env` nunca é enviado pelo workflow. A pipeline
atualiza somente as cinco credenciais recebidas dos GitHub Environment
Secrets. O Compose permite mutações somente no Asaas Sandbox. A credencial de
Produção habilita consultas e vínculo de clientes por CNPJ, mas
`Asaas__Production__AllowChargeCreation` permanece fixado como `false`.

O script grava os valores entre aspas simples no arquivo de ambiente. Isso é
obrigatório para tokens que contêm `$`, pois o Compose não pode interpretá-los
como referência a outra variável.

Web e API são servidos pelo mesmo proxy e domínio. Por isso o CORS fica fechado
por padrão em produção. `Cors:AllowedOrigins` só deve ser configurado quando
existir um consumidor web em uma origem HTTPS diferente.

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
