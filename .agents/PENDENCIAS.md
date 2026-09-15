# Pendências do Evoque Cobranças

Atualizado em 15/09/2026.

Este documento reúne o que foi levantado na reunião de apresentação e na
auditoria do software feita antes dela. Cada item traz a evidência que o
sustenta, para que a decisão de priorizar não dependa de memória.

Ordem: primeiro o que está quebrado, depois o que falta, depois o que é
limitação externa e dívida técnica. Itens operacionais, que não se resolvem em
código, ficam no fim.

---

## 1. Defeitos

### 1.1 A prévia guarda o cliente Asaas do ambiente errado

**Gravidade: alta. Impede faturamento real a partir de prévia importada.**

`BillingDraft.AsaasCustomerId` é preenchido na importação com o identificador do
cliente **Sandbox**, e `ChargeCreationService` o usa direto, sem olhar o
ambiente do lote:

```csharp
new AsaasChargeRequest(
    billingDraft.AsaasCustomerId,   // id do Sandbox
```

Um lote executado em Produção enviaria um identificador de teste para a conta
real, onde ele não existe.

Evidência em produção (15/09/2026): as prévias de Web Prado e Open Sports
carregam `cus_000008494556` e `cus_000008494729`, que são exatamente os
`asaasSandboxCustomerId` do catálogo. O campo `asaasProductionCustomerId` das
duas empresas está vazio.

**Correção proposta:** a prévia não deve guardar identificador de ambiente
nenhum. Ela já tem o CNPJ, que é a identidade estável. Quem resolve o cliente
deve ser o `ChargeCreationService`, consultando o catálogo no ambiente do lote,
no momento da execução.

Como efeito colateral, isso remove a necessidade de escolher o cliente Asaas
durante a importação, e com ela a regra de uma empresa por planilha.

### 1.2 Não existe como cancelar uma prévia criada por engano

**Gravidade: média. Um erro de importação bloqueia a competência.**

`BillingDraftService` só oferece `CreateAsync`, `ApproveAsync`, `ListAsync` e
`ListAuditLogsAsync`. Não há remoção nem cancelamento.

Existe a regra de uma prévia por empresa por competência. Uma prévia importada
com o arquivo errado ocupa aquele lugar permanentemente, e a importação
correta passa a ser recusada com *"Já existe uma prévia de X em {mês}"*.

"Não existe exclusão física" faz sentido para empresa e colaborador, que têm
histórico. Uma prévia que nunca virou cobrança não tem histórico a preservar.

**Correção proposta:** cancelar uma prévia que ainda não gerou cobrança,
preservando o registro de que ela existiu e quem cancelou. Uma prévia com
`AsaasPaymentId` preenchido nunca pode ser cancelada.

---

## 2. Funcionalidades ausentes

### 2.1 Auditoria e rastreamento de faturamentos passados

**Levantado na reunião de 15/09/2026.**

O dado existe e está sendo gravado. O que não existe é como lê-lo.

`IAuditLogRepository` expõe apenas:

```csharp
Task AddAsync(AuditLog auditLog, ...);
Task<IReadOnlyCollection<AuditLog>> ListByBillingDraftIdAsync(Guid billingDraftId, ...);
```

Só é possível consultar a auditoria de uma prévia específica, e apenas se você
já souber o `Guid` dela. Não há como perguntar:

- o que aconteceu na competência de setembro;
- o que determinado operador fez;
- qual o histórico de uma empresa ao longo dos meses;
- quem aprovou e quem executou um lote.

O único endpoint é `GET /api/billing-drafts/{id}/audit-logs`, e **não existe
tela** — o frontend não tem nenhuma referência a auditoria.

As ações já registradas incluem criação e aprovação de competência e prévia,
prévia e aprovação de lote, início e fim de execução, alterações de agenda,
vínculo de colaboradores, alteração de retenção de ISS e os eventos de nota
fiscal.

**O que falta:**

- consultas no repositório por competência, empresa, operador e período de
  datas;
- endpoint de listagem com filtros e paginação;
- tela de auditoria, com busca e exportação;
- visão por empresa reunindo competências, prévias, lotes, cobranças e notas —
  hoje `GET /api/companies/{taxId}/billing-history` cobre parte disso, mas é
  uma lista de prévias, não uma linha do tempo do que foi cobrado e pago.

Sem isso, a resposta a "o que foi cobrado desta empresa em julho e quem
autorizou" depende de abrir o painel do Asaas e cruzar à mão.

### 2.2 Disparo de e-mail próprio, pela Azure

**Desenhado em 14/09/2026, spec ainda não escrita.**

O Asaas Sandbox não entrega e-mail de notificação. Duas cobranças criadas em
momentos diferentes não produziram mensagem alguma, com configuração correta
dos dois lados. O comportamento só pode ser verificado em Produção.

Consequência prática: não há como conferir, ponta a ponta, o que o cliente
recebe, sem cobrar alguém de verdade.

**Desenho aprovado:**

- alcance **somente Sandbox**; em Produção quem notifica continua sendo o Asaas,
  que funciona há meses;
- destinatário **sempre um endereço controlado**, vindo de configuração, nunca o
  e-mail cadastrado da empresa;
- serviço: **Azure Communication Services Email**;
- conteúdo: empresa, competência, valor, vencimento e o link do
  `bankSlipUrl`; sem anexo;
- gatilho: junto da execução do lote, como a nota fiscal;
- assunto marcando o ambiente: `[TESTE] Boleto Evoque · {empresa} · {competência}`.

Duas travas independentes, porque o risco aqui é mandar boleto de teste para
cliente real: a de ambiente é regra de código, e a de destinatário impede que o
e-mail da empresa sequer seja lido nesse caminho.

**Bloqueado por:** credenciais. É preciso um recurso Azure Communication
Services com Email Communication Service, um domínio verificado com SPF e DKIM
publicados, a connection string e o endereço remetente autorizado.

Enquanto não chegarem, a flag fica desligada e o sistema se comporta como hoje.

### 2.3 Identificação de quem opera

**Levantado na auditoria de interface.**

`operatorId` está fixo no código do portal:

```ts
const operatorId = "operador-web";
```

Toda a auditoria — aprovar prévia, aprovar lote, digitar `CONFIRMAR` — registra
o mesmo nome. Num sistema que cria cobrança real, a pergunta "quem autorizou?"
não tem resposta.

Isso reduz o valor do item 2.1: uma tela de auditoria mostraria "operador-web"
em todas as linhas.

**O que falta:** autenticação, ainda que simples, e o operador real viajando
nas chamadas em vez da constante.

---

## 3. Limitações externas

### 3.1 O EVO não expõe o valor do contrato corporativo

**Confirmado em julho/2026 pela API e em setembro/2026 pelas exportações.**

Na validação de julho, os contratos corporativos reais retornaram sem
recebíveis, com valores zerados e sem parceria nas vendas.

Em setembro, três exportações do CRM 2.0 confirmaram o mesmo pela planilha: das
169 linhas com contrato `EVOQUE CORPORATIVO`, **uma** tinha valor. As 349 linhas
com valor eram assinantes `EVOPASS`, que pagam a própria academia.

Só o relatório de fechamento calcula o valor por empresa. Por isso a planilha
continua sendo a origem da prévia financeira.

**Pendente:** confirmar com a Evo se existe uma fonte automática para esses
valores. Enquanto não houver, a importação manual permanece.

### 3.2 O leitor de planilha não aceita o formato que o EVO gera

**Consequência direta do item anterior. Custa trabalho manual todo mês.**

O importador exige `Nome`, `Empresa` ou `Profissão` contendo `EMPRESA - CNPJ` no
mesmo campo, e `Valor do contrato`.

O relatório de fechamento do EVO entrega outra coisa:

| Coluna no relatório | Esperado pelo leitor |
|---|---|
| `Nome do aluno` | `Nome` |
| `Nome do responsavel` + `CNPJ do responsavel` (separadas) | um campo só, com CNPJ no fim |
| `Valor` | `Valor do contrato` |

Além disso o relatório traz **uma aba por empresa**, e
`ReadFirstWorksheetRows` lê apenas a primeira. E há linhas de total, que
virariam item de cobrança.

Em 15/09/2026 foram necessárias quatro tentativas até chegar um arquivo
aproveitável, e ainda assim ele precisou ser convertido por script
(`docs/converter-fechamento.py`).

**Correção proposta:** aceitar os nomes de coluna do relatório de fechamento,
ler o CNPJ de coluna própria, percorrer todas as abas e descartar linhas de
total. O formato do fechamento é melhor que o atual — CNPJ em campo próprio
dispensa extrair dígitos do fim de um texto, que já falhou com nome truncado.

Isso também elimina a regra de uma empresa por planilha, junto com o item 1.1.

**Questão aberta:** o relatório `Fechamento - 06.xlsx` trazia abas com sufixo de
competência (`FARMAVA 06`, `FARMAVA 08`) no mesmo arquivo. Se isso for padrão, o
importador precisa saber qual aba pertence à competência sendo faturada.

---

## 4. Dívida técnica

### 4.1 O portal inteiro em um arquivo

`web/client/src/app/page.tsx` tem 2.352 linhas e concentra todas as telas.

Há também quatro componentes que nunca são renderizados: `MembersPage`,
`CompactEvoMembersPage`, `EvoMembersPage` e `CorporateMembersPage`.

Não afeta o funcionamento. Afeta quem for alterar.

### 4.2 `CompanyResponse` com 23 campos posicionais

O record cresceu a ponto de dois `bool` adjacentes serem difíceis de auditar
visualmente no ponto de construção. Vale quebrar em sub-objetos quando houver
motivo para mexer nele.

---

## 5. Pendências operacionais

Não se resolvem em código.

### 5.1 Emissão de nota fiscal em produção continua desligada

`ASAAS__PRODUCTION__ALLOWINVOICEISSUANCE` está `false`. Antes de ligar, é
preciso responder: **ao criar uma cobrança no painel do Asaas, a nota sai
sozinha ou alguém clica depois?**

A investigação de 15/09/2026 mostrou que a conta **não tem assinaturas** (0 de
0), e é só por assinatura que o Asaas configura emissão automática. A
documentação indica uma opção marcada cobrança a cobrança, na tela do painel —
que não se aplicaria às cobranças criadas por API.

Se ninguém clica, existe uma configuração que a API não expõe e ela precisa ser
localizada. Se alguém clica, basta parar de clicar nas cobranças que o sistema
criar.

### 5.2 Retenção de ISS pendente de confirmação contábil

Três empresas estão marcadas com retenção em produção: Ciasul, Contract e
Tech-Lix.

**Braido (59.274.167/0001-93) não está no catálogo**, então a semente da
migration não a alcançou. Se for cliente, precisa ser cadastrada e marcada.

**ARZ, ALGT e Projeto Criando** seguem sem retenção. Cada uma teve um caso
isolado contra oito ou nove no sentido oposto, e a decisão é da contabilidade.

### 5.3 Setenta e seis cobranças sem nota fiscal

Leitura de 14/09/2026: 76 de 304 cobranças da conta não têm nota válida, quase
todas já recebidas. Dezesseis notas estão travadas, dez delas por retenção de
ISS.

Isso é anterior ao software — nenhuma das 265 notas da conta passou por ele,
confirmado pelo `externalReference` vazio em todas. Mas é passivo fiscal a
resolver, e a tela de Notas fiscais foi feita para que não volte a acontecer.

### 5.4 Empresas com contrato corporativo fora do catálogo

A exportação de 15/09/2026 mostrou empresas com contrato corporativo que não
estão cadastradas, entre elas **Plastfer Gerenciamento Ambiental** e **Asilo São
Vicente de Paulo**, além de oito linhas sem CNPJ na coluna de empresa.

Cada uma precisa de uma decisão: é cliente e falta cadastrar, ou não é.

---

## Prioridade sugerida

1. **1.1** — impede faturar em produção a partir de prévia importada.
2. **5.1** — destrava a emissão de nota fiscal, que já está pronta.
3. **2.1** — auditoria, levantada na reunião.
4. **3.2** — elimina a conversão manual de planilha todo mês.
5. **1.2** — cancelar prévia, que hoje só se resolve no banco.
6. **2.3** — identificação do operador, que dá sentido à auditoria.
7. **2.2** — e-mail pela Azure, bloqueado por credencial.
