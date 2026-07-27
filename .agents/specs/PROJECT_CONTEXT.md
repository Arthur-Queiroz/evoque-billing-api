# Contexto do projeto

O Evoque Cobranças é um sistema interno para preparar, aprovar e emitir
cobranças corporativas no Asaas. A aplicação não substitui o Evo: pessoas,
contratos e matrículas continuam sendo lidos da API do Evo.

## Fontes de dados

- **Evo API:** leitura somente, para membros e contratos disponíveis. Nenhuma
  chamada mutável é permitida neste projeto. Ela não é fonte confiável da
  empresa pagadora.
- **Planilha completa do CRM 2.0 do Evo:** fonte de sincronização do catálogo
  interno de empresas. Descobre `nome no EVO + CNPJ + pessoas encontradas`. Não
  usa valores financeiros e não cria cobrança.
- **Planilha de fechamento validada:** fonte auditável da prévia financeira
  enquanto a API pública não reproduzir o relatório interno.
- **BrasilAPI:** enriquecimento e validação cadastral de um CNPJ já conhecido.
  Não é fonte obrigatória nem fonte do vínculo.
- **Banco legado da Evoque:** não é fonte do produto.
- **Banco próprio do faturamento:** persiste competência, prévia, agenda,
  catálogo de empresas, sincronizações, lote, auditoria e resultados.

## Catálogo interno de empresas

A tela `Empresas` não depende mais de `GET /api/evo/companies` nem da
descoberta de parcerias pelas vendas do Evo. O catálogo é interno e a
identidade estável de uma empresa é o CNPJ normalizado com 14 dígitos e
dígitos verificadores válidos — o mesmo identificador que a importação de
fechamento grava em `BillingDraft.ExternalCompanyId` e que a agenda usa em
`company_billing_schedules`.

## Repositórios

- `evoque-billing-api`: API ASP.NET Core, infraestrutura de produção e Compose.
- `evoque-billing-web`: Next.js/Tailwind, publicado como imagem separada.

Ambos publicam imagens no GHCR. A API sobe a stack inicial da KVM2; alterações
web posteriores atualizam apenas a imagem do client pelo GitHub Actions.

### Current EVO integration finding

The EVO API exposes the corporate partnership through
`membermembership → sale`: a sale includes the partnership identifier and
name when a membership is corporate. CNPJ still requires an explicit source
or a configured, auditable mapping in this product.

### Validação com fechamento real de julho/2026

O vínculo pela venda existe no contrato público da API, mas não é suficiente
como fonte única. Na amostra real da Web Prado:

- os quatro membros da planilha foram encontrados;
- cada membro possui uma matrícula `active` com `idSale`;
- não existem recebíveis mensais ou históricos retornados para esses membros;
- a matrícula e as vendas atual/recorrente retornam valor zero;
- a venda e seus itens não retornam parceria corporativa.

A planilha exportada pelo Portal EVO contém os quatro vínculos e totaliza
R$ 439,60. Portanto, enquanto a API pública não reproduzir o relatório interno,
a planilha é uma fonte operacional confiável e auditável para gerar a prévia.
O endpoint de recebíveis permanece útil para diagnóstico e reconciliação, não
como origem exclusiva do fechamento.

## Estado do MVP

O fluxo de prévia, aprovação, confirmação e lote está implementado. A emissão
de teste é permitida apenas no Asaas Sandbox.

O catálogo interno de empresas está implementado e é populado pela
sincronização da planilha do CRM 2.0. Na validação de julho/2026, a exportação
real produziu 63 empresas a partir de 572 linhas analisadas, com 512 pessoas e
60 avisos de linhas sem CNPJ no padrão esperado.

Falta confirmar, com a Evo, uma fonte automática para os valores correntes por
matrícula corporativa. Até lá, a planilha de fechamento validada continua sendo
a origem da prévia financeira.
