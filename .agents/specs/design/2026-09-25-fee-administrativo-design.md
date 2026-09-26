# Fee administrativo por colaborador

Data: 25/09/2026
Status: aprovado para implementação

## Objetivo

Cobrar a taxa administrativa que algumas empresas pagam por colaborador, hoje
ausente do software. O faturamento Sandbox de 25/09/2026 saiu sem ela.

## Onde o fee estava

Na aba `ADESÕES B2B` da planilha `CONTROLE OPERACIONAL 2026.xlsx`, coluna
`VALOR FEE`. Ele nunca esteve na exportação do EVO, e por isso nunca chegou ao
sistema: o EVO descreve quem é colaborador, não o que foi combinado
comercialmente com a empresa.

A coluna é texto livre e tem três formas distintas entre as 115 empresas:

| Forma | Empresas | Natureza |
|---|--:|---|
| vazio / `0` | 103 | sem fee |
| `8 reais por aderido` | 8 | por colaborador |
| `9,90 por aderido` | 3 | por colaborador |
| `500 reais fixo` | 1 (MFA) | fixo por empresa |

## Duas coisas que pareciam verdade e não eram

**A Plastpel não tem fee.** Ela foi listada por engano numa conferência
anterior. As três empresas de R$ 9,90 são Plastvidro, Vitória e Plastfer.

**A Contract tem fee de R$ 8,00** e havia ficado de fora. É ativa, fecha no dia
25 e foi faturada sem a taxa.

O erro nos dois casos foi trabalhar de memória em vez de ler a coluna.

**A observação "Exceção - dependentes não-legais" não explica o fee.** Ela
aparece em quase todas as empresas com fee, o que sugere causalidade, mas
aparece também em empresas sem fee nenhum — a Bio Pharmacos, por exemplo. É
sobre dependentes, não sobre taxa.

## Empresas afetadas

Das 12 linhas com fee no controle, 8 correspondem a empresas do catálogo:

| Empresa | CNPJ | Fee | Dia | Situação |
|---|---|--:|:-:|---|
| Ciamon | 34818653000102 | 8,00 | 25 | ativa |
| Ciasul | 53164208000102 | 8,00 | 25 | ativa |
| Contract | 58515495000171 | 8,00 | 25 | ativa |
| Gemon | 26626384000146 | 8,00 | — | ativa, sem dia |
| Geserv ABC | 34480924000154 | 8,00 | — | ativa, sem dia |
| Plastvidro | 01330329000183 | 9,90 | 25 | ativa |
| Vitória | 04026384000172 | 9,90 | 25 | ativa |
| Plastfer | 04967119000199 | 9,90 | — | inativa |

As 4 linhas restantes são filiais e uma empresa fora do catálogo: Ciasul
0009-60 e 0008-89, Contract 0003-33 e ABC Gemon 33171222000126.

**A Ciasul recebe fee por decisão explícita.** A matriz 0001-02 — que é a que
está no catálogo e a que foi faturada — não tem linha no controle; só as duas
filiais têm. A decisão foi tratar as filiais como evidência de que o grupo tem
fee.

Gemon e Geserv ABC não entram em lote enquanto estiverem sem dia de fechamento,
mas o valor fica cadastrado para quando entrarem.

## Desenho

`Company.FeePerMember`, um `decimal?` ao lado de `AmountPerMember`. Mesmo
formato, mesmo ciclo de vida, mesmas telas.

O fee é atributo do acordo comercial com a empresa, exatamente como o preço, e
é um valor único por empresa. Uma tabela de fees com vigência foi descartada
por abstração prematura: ninguém pediu histórico.

Embutir o fee no `AmountPerMember` — cobrar 67,90 em vez de 59,90 + 8,00 —
também foi descartado. É mais barato, mas apaga a distinção entre mensalidade e
taxa, e faz os números pararem de bater com o controle operacional na hora da
conferência.

### Regras do valor

`null` significa sem fee. Zero e valores negativos são recusados, com a
orientação de informar vazio para retirar a taxa.

Essa é exatamente a regra de `SetAmountPerMember`, e a igualdade é deliberada:
dois campos quase idênticos com regras diferentes de valor vazio é uma armadilha
de leitura. Recusar zero também garante uma única representação de "sem fee",
que era o objetivo.

### Geração da prévia

`CorporateBillingDraftService` acrescenta **um item**, depois dos
colaboradores, quando a empresa tem fee:

```text
BillingDraftItem(
    Description    = "Taxa administrativa",
    Quantity       = nº de colaboradores,
    UnitAmount     = fee da empresa,
    ExternalMemberId = null)
```

`ExternalMemberId` já é anulável, então a prévia não muda de schema. O item
entra no `TotalAmount`, e é daí que vêm o valor do boleto e a base da NFS-e.

### Fluxo do valor

```text
colaboradores ativos
→ itens de mensalidade + 1 item de taxa
→ TotalAmount da prévia
→ value do boleto
→ value da NFS-e, com ISS de 5%
```

O fee é tributado como serviço, junto com o resto. Foi decisão explícita: a
alternativa — cobrar no boleto mas excluir da base da nota — exigiria separar
valor cobrado de valor tributado, que hoje são o mesmo campo.

### O que deliberadamente não muda

**A importação de planilha não recebe fee.** A planilha de fechamento é fonte
já fechada e revisada; somar fee sobre ela cobraria duas vezes quando ela já o
inclui. Só o fluxo do catálogo calcula taxa.

**As descrições do boleto e da nota continuam genéricas.** O boleto envia um
`value` único e a descrição `Faturamento Evoque <competência> - <empresa>`; a
nota envia `value` e `serviceDescription`. Nenhum dos dois já foi detalhado por
item, então o fee não quebra texto nenhum. O detalhamento vive na prévia, no
portal.

### Fee fixo por empresa fica fora

A MFA tem `500 reais fixo` e vai continuar sem fee nenhum no software. Hoje ela
não é faturada — está sem valor por colaborador e sem prévia — então a forma
fixa não teria como ser exercitada.

Isso precisa virar pendência explícita. Quando a MFA começar a faturar, a
ausência do fee fixo é perda de receita silenciosa: nada no sistema vai
sinalizar que faltou.

## Superfície

| Camada | Mudança |
|---|---|
| `Domain/Company.cs` | `FeePerMember`, `SetFeePerMember` |
| `Repositories/DatabaseSchemaInitializer.cs` | migration `018_add_company_fee_per_member` |
| `Repositories/MySqlCompanyRepository.cs` | ler e gravar a coluna |
| `Services/CorporateBillingDraftService.cs` | item de taxa |
| `Contracts/CompanyContracts.cs` | `FeePerMember` no request e no response |
| `web/client` | exibir e editar a taxa nas telas de empresa |

## Erros e bordas

- Fee negativo é recusado com `ValidationException`.
- Empresa sem fee gera prévia idêntica à de hoje — nenhum item extra.
- Empresa com fee e sem colaborador ativo não gera prévia, então não gera taxa.
  A quantidade do item de taxa nunca é zero, o que satisfaz a validação
  existente de `Quantity > 0`.

## Testes

- Empresa sem fee: itens = colaboradores e total inalterado. É o teste de
  regressão que protege as 30 empresas ativas que não têm taxa.
- Empresa com fee: exatamente um item a mais, total `n×valor + n×fee`.
- Contract, com número real: 6 × 59,90 + 6 × 8,00 = R$ 407,40.
- Fee negativo recusado.
- A migration semeia as 8 empresas e não altera o fee das demais.

## Efeito no ciclo do dia 25

| Empresa | Pessoas | Fee | Acréscimo |
|---|--:|--:|--:|
| Ciamon | 5 | 8,00 | 40,00 |
| Ciasul | 1 | 8,00 | 8,00 |
| Contract | 6 | 8,00 | 48,00 |
| Plastvidro | 6 | 9,90 | 59,40 |
| Vitória | 4 | 9,90 | 39,60 |
| | | | **195,00** |

O lote do dia 25 passa de R$ 6.066,57 para R$ 6.261,57.
