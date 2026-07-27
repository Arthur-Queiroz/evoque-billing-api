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
Sincronização do catálogo            Prévia de faturamento
CompanyCatalogSpreadsheetReader      BillingSpreadsheetReader
→ exige empresa com CNPJ válido      → exige empresa, pessoa e valor > 0
→ aceita valor vazio/zero/inválido   → recusa a linha sem valor positivo
→ agrupa pessoas por CNPJ            → agrupa itens por CNPJ
→ upsert em `companies`              → BillingDraft pendente de revisão
→ enriquecimento cadastral           → aprovação humana
                                     → lote Asaas Sandbox
```

`BillingSpreadsheetReader.Read` não pode ser enfraquecido para servir ao
catálogo. Nenhum dos dois fluxos cria cobrança: a mutação no Asaas continua
protegida pela aprovação, confirmação textual e política de ambiente.

## Camadas do catálogo

```text
CompaniesController            → CompanyCatalogService
CompanyCatalogImportsController → CompanyCatalogImportService
                                 ↘ CompanyRegistryEnrichmentService
                                    ↘ ICompanyRegistryGateway
ICompanyRepository / ICompanyCatalogImportRepository → MySQL ou InMemory
```

Os dois repositories têm implementação MySQL e InMemory, registradas
explicitamente em `Program.cs`. O schema do catálogo é a migration
`006_add_company_catalog`; migrations já aplicadas nunca são editadas.

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
- Migrations próprias são aplicadas pelo inicializador do schema; nunca aplicar
  alterações ao banco legado da Evoque.

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
