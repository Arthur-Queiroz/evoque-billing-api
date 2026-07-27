# Backend de faturamento Evoque

API ASP.NET Core responsável pelo ciclo de faturamento corporativo: competência,
prévia por empresa, aprovação, auditoria e, no ambiente de produção autorizado,
criação de cobranças no Asaas.

## Estrutura

```text
Controllers → Services → Repositories → MySQL
```

Em desenvolvimento, sem `ConnectionStrings:BillingDatabase`, os repositórios
usam memória para permitir testar as regras sem uma instância MySQL. Em produção a
connection string é obrigatória; a API não inicia sem ela.

## Segurança do Asaas e envio do boleto

Antes de criar uma cobrança, a API consulta o cliente no Asaas e exige:

1. Ao menos um e-mail no cadastro (`email` ou `additionalEmails`).
2. A notificação `PAYMENT_CREATED` ativa para o e-mail do cliente.
3. Prévia e competência aprovadas, além de confirmação explícita do operador.

Assim, o próprio Asaas envia a comunicação de cobrança ao pagador. O Gmail API
fica reservado para uma futura comunicação complementar, caso seja necessária.

O gateway de cobrança só executa um `POST /payments` se as condições abaixo
forem atendidas:

1. `Asaas:AllowChargeCreation` está como `true`.
2. A chave é fornecida por configuração segura, nunca pelo Git.
3. O ambiente configurado e a URL correspondem entre si:
   - `Sandbox` → `https://api-sandbox.asaas.com/v3/`;
   - `Production` → `https://api.asaas.com/v3/` e processo hospedado em produção.

A prévia guarda o identificador da cobrança criada. Uma nova solicitação para a
mesma prévia devolve esse identificador sem criar um segundo boleto.

Sandbox é o padrão seguro em `appsettings.json`. O endpoint e o ambiente de
Produção não são usados até que sejam configurados explicitamente em uma
implantação de produção autorizada.

## Lotes e retentativas

`POST /api/charge-batches` cria um lote somente após a confirmação textual
`CONFIRMAR`. O lote e cada resultado por prévia ficam persistidos, incluindo o
identificador Asaas, URL do boleto ou motivo de falha.

- `GET /api/billing-periods/{ano}/{mês}/charge-batches` lista os lotes da competência;
- `POST /api/charge-batches/{id}/retry-failed` cria um novo lote apenas com os
  itens que falharam no lote informado. Também exige `CONFIRMAR`.

Essa retentativa nunca recria cobranças de itens já concluídos: a criação da
cobrança continua idempotente pela prévia e versão aprovada.

## Lotes, ambientes e agenda recorrente

O fluxo de lote novo é deliberadamente dividido em três etapas:

1. `POST /api/charge-batches/previews` cria uma prévia, sem chamar o Asaas.
   O corpo informa `asaasEnvironment` como `Sandbox` ou `Production`.
2. `POST /api/charge-batches/{id}/approve` registra quem aprovou a prévia.
3. `POST /api/charge-batches/{id}/execute`, com `CONFIRMAR`, cria as cobranças.

O ambiente, a aprovação e o resultado de cada item ficam persistidos. Uma
cobrança de Sandbox não marca a prévia como cobrada definitivamente; a mesma
prévia aprovada pode ser validada em Sandbox e depois executada em Produção.

Para ciclos recorrentes, configure cada empresa em
`PUT /api/company-billing-schedules/{externalCompanyId}`. Os únicos dias
aceitos são 02, 18, 20 e 25. A rota
`POST /api/billing-periods/{ano}/{mês}/scheduled-charge-batches/previews`
seleciona somente as empresas ativas naquele dia que possuem prévia aprovada.

## Homologação Asaas local

A configuração de sandbox deve ficar no User Secrets, nunca em `appsettings`:

```powershell
dotnet user-secrets set "Asaas:IntegrationEnvironment" "Sandbox"
dotnet user-secrets set "Asaas:BaseUrl" "https://api-sandbox.asaas.com/v3/"
dotnet user-secrets set "Asaas:AllowChargeCreation" "true"
dotnet user-secrets set "Asaas:ApiKey" "<chave sandbox>"
```

O Sandbox exige CPF/CNPJ no cliente e, para boleto, um valor mínimo de R$ 5,00.
O arquivo `Evoque.Billing.Api.http` começa com três requisições somente de
leitura. As duas últimas são mutáveis, exigem IDs explícitos de prévia/lote e
devem ser executadas apenas quando o processo estiver apontando para Sandbox.

Enquanto a homologação estiver em curso, a API bloqueia qualquer chamada ao
Asaas configurada como `Production` fora de um processo ASP.NET em produção.
Isso inclui consultas de clientes e notificações, não somente `POST /payments`.

## Configuração local

```powershell
dotnet run --project .\Evoque.Billing.Api
```

O armazenamento em memória é intencional no ambiente de desenvolvimento.

## Integração Evo

As consultas de colaboradores, empresas/convênios e grupos de filiais usam a
API oficial do Evo e somente requisições `GET`. Configure as credenciais locais
fora do Git:

```powershell
dotnet user-secrets set "Evo:BaseUrl" "https://evo-integracao-api.w12app.com.br/"
dotnet user-secrets set "Evo:Username" "<dns-da-evo>"
dotnet user-secrets set "Evo:ApiKey" "<token-da-evo>"
```

As rotas disponíveis são `GET /api/evo/employees`, `GET /api/evo/members`,
`GET /api/evo/companies` e `GET /api/evo/branch-groups`. Membros retornam
matrículas, valores e vigências informados pelo Evo. Empresas são derivadas de
parcerias/convênios; quando o Evo não retornar nenhuma, a API devolve uma lista
vazia.

## Configuração de banco

Defina a connection string fora do controle de versão:

```powershell
dotnet user-secrets set "ConnectionStrings:BillingDatabase" "Server=localhost;Port=3307;Database=evoque_billing;User ID=evoque_billing_app;Password=...;SslMode=None;AllowPublicKeyRetrieval=True"
```

Em produção, o MySQL 8.4 roda como serviço interno do Compose, sem porta
pública, com dados no volume persistente `mysql_data`. O workflow recebe a
senha e as credenciais das integrações pelos Environment Secrets
`MYSQL_PASSWORD`, `ASAAS_API_KEY`, `EVO_USERNAME` e `EVO_API_KEY`, e atualiza o
arquivo protegido da VPS antes do deploy, sem registrar os valores nos logs.

No primeiro uso, a aplicação registra migrations em `schema_migrations` e cria
apenas as tabelas do novo produto, inclusive `charge_batches`,
`charge_batch_items`, `companies` e `corporate_members`.

## Saúde e configuração de produção

`GET /health` responde somente se a API estiver disponível e, quando há banco
configurado, se a consulta ao banco funcionar. Em produção, a API valida na
inicialização a connection string, chave do Asaas e URL HTTPS do Asaas. O CORS
fica fechado por padrão e aceita somente origens HTTPS quando explicitamente
configurado; configuração insegura impede o processo de iniciar.

## Verificação

```powershell
dotnet build .\Evoque.Billing.Api\Evoque.Billing.Api.csproj
dotnet test .\Evoque.Billing.Api.Tests\Evoque.Billing.Api.Tests.csproj
dotnet format .\Evoque.Billing.Api\Evoque.Billing.Api.csproj --verify-no-changes
```
