# Contexto do projeto

O Evoque Cobranças é um sistema interno para preparar, aprovar e emitir
cobranças corporativas no Asaas. A aplicação não substitui o Evo: pessoas,
contratos e matrículas continuam sendo lidos da API do Evo.

## Fontes de dados

- **Evo API:** leitura somente, para membros e contratos disponíveis. Nenhuma
  chamada mutável é permitida neste projeto. Ela não é fonte confiável da
  empresa pagadora.
- **Cadastro interno de empresas:** fonte operacional do catálogo. A equipe
  cadastra e mantém empresas pelo CNPJ; a BrasilAPI preenche os dados públicos
  disponíveis.
- **Planilha completa do CRM 2.0 do Evo:** fonte do snapshot de colaboradores
  corporativos e ferramenta de inclusão de empresas. Descobre
  `IdCliente + nome + contratos + empresa/CNPJ`; cadastra somente CNPJs
  inexistentes e nunca altera os dados operacionais de empresas já cadastradas.
- **Planilha de fechamento validada:** fonte auditável da prévia financeira
  enquanto a API pública não reproduzir o relatório interno.
- **BrasilAPI:** enriquecimento e validação cadastral de um CNPJ já conhecido.
  Não é fonte obrigatória nem fonte do vínculo.
- **MySQL próprio na KVM2:** destino persistente do produto, no database
  `evoque_billing`. Armazena competência, prévia, agenda, catálogo,
  colaboradores corporativos, sincronizações, lotes, auditoria e resultados.
  Ele roda somente na rede interna do Compose e não possui porta pública.
- **Azure MySQL `evoque_corporativo`:** banco atual da Evoque e possível fonte
  futura de integrações aprovadas. Não é o destino primário deste produto e
  não será alterado pelo deploy.
- **Banco legado anterior ao Azure:** não é fonte nem destino deste produto.

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

O catálogo interno de empresas está implementado e persiste independentemente
das planilhas. As 63 empresas iniciais foram descobertas na exportação real do
CRM 2.0, a partir de 572 linhas analisadas. Novas empresas passam a ser
cadastradas individualmente; a planilha permanece apenas como inclusão em lote.

Os colaboradores corporativos também formam uma base persistente. A
exportação completa compara o estado atual com a base: adiciona novos, mantém
os presentes, inativa ausentes e reativa quem reaparece na mesma empresa. Uma
divergência de CNPJ para o mesmo `IdCliente` é bloqueada para conferência
manual.

Falta confirmar, com a Evo, uma fonte automática para os valores correntes por
matrícula corporativa. Até lá, a planilha de fechamento validada continua sendo
a origem da prévia financeira.
