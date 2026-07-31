# Arquitetura da API

### Corporate linkage composition from EVO

To identify a payer company without inference, the API reads
`v3/membermembership` and then `v2/sales/{idSale}` for each sale identifier.
A row is returned as corporate only when the sale explicitly supplies a
corporate partnership identifier and name. The operation is paginated and
read-only; it does not replace the CNPJ-to-Asaas mapping.

Essa composição é tratada como diagnóstico porque a validação de julho/2026
encontrou contratos corporativos reais sem recebíveis, sem valores e sem
parceria nas vendas retornadas pela API. Ela não é usada para listar empresas.

## Dois fluxos de planilha, deliberadamente separados

O catálogo descobre empresas; o fechamento calcula valores. Eles compartilham
apenas o parsing estrutural do XLSX, em `SpreadsheetWorkbookReader`.

```text
Vínculo de colaboradores             Prévia de faturamento
CompanyCatalogSpreadsheetReader      BillingSpreadsheetReader
→ exige empresa com CNPJ válido      → exige empresa, pessoa e valor > 0
→ aceita valor vazio/zero/inválido   → recusa a linha sem valor positivo
→ agrupa IdCliente e contratos/CNPJ  → agrupa itens por CNPJ
→ NUNCA cria empresa                 → BillingDraft pendente de revisão
→ vincula só a empresa cadastrada
→ CNPJ desconhecido vira pendência
→ compara snapshot de colaboradores  → aprovação humana
                                     → lote Asaas Sandbox
```

A separação entre valor positivo e valor vazio existe porque as duas planilhas
descrevem populações opostas. Na exportação real, as 181 linhas com contrato
`EVOQUE CORPORATIVO` têm valor zero — são os colaboradores de verdade — e as 389
linhas com valor são assinantes `EVOPASS` que pagam a própria academia. Aceitar
valor no fluxo de catálogo e exigir valor no de fechamento é deliberado.

`BillingSpreadsheetReader.Read` não pode ser enfraquecido para servir ao
catálogo. Nenhum dos dois fluxos cria cobrança: a mutação no Asaas continua
protegida pela aprovação, confirmação textual e política de ambiente.

## Camadas do catálogo

```text
CompaniesController            → CompanyCatalogService
CompanyCatalogImportsController → CompanyCatalogImportService
CorporateMembersController      → CorporateMemberService
                                 ↘ CompanyRegistryEnrichmentService
                                    ↘ ICompanyRegistryGateway
ICompanyRepository / ICompanyCatalogImportRepository /
ICorporateMemberRepository → MySQL ou InMemory
```

Os dois repositories têm implementação MySQL e InMemory, registradas
explicitamente em `Program.cs`. O schema do catálogo é a migration
`006_add_company_catalog`; migrations já aplicadas nunca são editadas.
O CRM de colaboradores usa a migration `007_add_corporate_member_crm`, com
`corporate_members` e `corporate_member_contracts`.

## Componentes

```text
Next.js (repositório web)
        ↓ HTTPS /api
Nginx da stack
        ├── API ASP.NET Core :8080
        └── Next.js :3000
API → Evo (GET) / Asaas (Sandbox ou Produção autorizada)
API → MySQL próprio do faturamento
```

O Nginx, Compose e arquivos de ambiente de exemplo estão em `infra/`. A VPS
mantém segredos em `/opt/evoque/production.env`, fora do Git.

## Camadas

```text
Controllers → Services → Repositories → MySQL
                         ↘ Gateways Evo/Asaas
```

- DTOs em `Contracts/` definem contratos HTTP.
- Domínio em `Domain/` representa competências, prévias, agendas e lotes.
- Gateways encapsulam HTTP externo e não conhecem controllers.
- Migrations próprias são aplicadas pelo inicializador no database
  `evoque_billing`, em uma instância MySQL 8.4 exclusiva do produto na KVM2.
  O serviço não publica a porta 3306 e só é acessível pela rede interna do
  Compose. Os dados ficam no volume persistente `mysql_data`.
- O Azure MySQL da Evoque e o banco legado anterior não são destinos de
  persistência deste produto. Uma futura migração ou integração com eles exige
  decisão explícita, credenciais próprias e plano de reconciliação.

## Integrações

Evo usa Basic Auth com usuário e token do servidor. Endpoints confirmados para
leitura incluem membros, colaboradores, grupos/filiais e consultas de
partnership. O Evo permanece leitura somente e não é fonte da empresa pagadora.

O cadastro público de CNPJ é acessado por `ICompanyRegistryGateway`, com a
implementação `BrasilApiCompanyRegistryGateway` sobre
`GET {CompanyRegistry:BaseUrl}cnpj/v1/{cnpj}`. O gateway nunca lança por falha
externa: ele devolve `Found`, `NotFound` ou `Unavailable`, e o service decide o
que persistir. Nunca é chamado durante um simples `GET /api/companies`.

Asaas é acessado apenas pelo backend. O client recebe estado de integração,
prévia e resultado, nunca tokens ou URLs autenticadas.

O cliente Asaas é ligado à empresa pelo CNPJ, sem entrada manual de IDs. A
sincronização Sandbox pode criar apenas um cliente espelho de teste com e-mail
controlado. A sincronização Production executa somente `GET /customers` e
persiste localmente o vínculo quando encontra um único resultado. Ela não
possui caminho de criação ou atualização de cliente no Asaas.
