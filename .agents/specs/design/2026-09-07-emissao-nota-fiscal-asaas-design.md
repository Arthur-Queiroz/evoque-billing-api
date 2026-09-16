# Emissão de nota fiscal de serviço pelo Asaas

Data: 07/09/2026
Status: aprovado para implementação

## Objetivo

O software passa a emitir a NFS-e de cada cobrança corporativa que ele cria no
Asaas, no mesmo lote autorizado que gera o boleto. Hoje a nota sai por uma
configuração automática do painel do Asaas, sem nenhuma visibilidade dentro do
produto: notas recusadas pela prefeitura ficam paradas por meses sem que
ninguém perceba.

## Evidência de produção

Leitura somente `GET` da conta de produção em 07/09/2026, por
`docs/asaas-producao-notas-fiscais.ps1`. Foram lidas 264 notas de 39 empresas —
exatamente o catálogo ativo.

Configuração fiscal da conta: prefeitura de São Caetano do Sul-SP, inscrição
municipal `148778`, `simplesNacional=false`, `useNationalPortal=false`, NBS
`1.2602.30.00` (Serviços de bem-estar físico), certificado e login municipal já
enviados.

Padrão observado nas notas:

| Campo | Valor real |
|---|---|
| `municipalServiceId` | `82367` em 264/264 |
| `municipalServiceCode` | sempre vazio |
| `municipalServiceName` | `Serviços prestados em MM/AAAA` |
| `taxes.iss` | `5,00000` sempre |
| `cofins`, `csll`, `inss`, `ir`, `pis` | zero ou vazio |
| `deductions` | vazio ou `0,00` |
| `observations` | sempre vazio |
| vínculo | `payment` em 264/264; nenhuma avulsa ou por parcelamento |
| `externalReference` | sempre vazio |

Distribuição por status: 171 `AUTHORIZED`, 13 `ERROR`, 12 `CANCELED`, 4
`CANCELLATION_DENIED`.

### A emissão atual não espera o pagamento

Defasagem entre a emissão da nota e o vencimento do boleto, em dias, com
positivo significando nota emitida antes do vencimento:

```
 2 dias: 15x     6 dias: 16x      9 dias:  7x
 4 dias: 17x     7 dias: 15x     10 dias:  7x
 5 dias:  3x     8 dias:  5x     12 dias:  4x
```

Existem notas `AUTHORIZED` emitidas em 01–04/09 para boletos com vencimento em
11/09 e 14/09 ainda `PENDING`. O padrão corresponde a emissão na criação da
cobrança. A primeira versão deste desenho previa emitir só após o pagamento
confirmado; a evidência mostrou que isso mudaria a prática contábil vigente, e
a decisão foi copiar o comportamento atual.

### A retenção de ISS varia por empresa

Seis dos treze erros são a prefeitura recusando: *"Conforme legislação
municipal, esta prestação de serviço deve ter retenção de ISS"*. Um sétimo diz
o contrário: *"Esta NFS-e não deverá ter o ISSQN Retido pelo tomador"*. Os
outros erros foram certificado digital inválido (1) e cancelamento negado por
competência já encerrada (4).

| Empresa | CNPJ | Retidas | Não retidas | Erros de retenção |
|---|---|---|---|---|
| GERENCIAMENTO AMBIENTAL TECH-LIX | 04902653000117 | 8 | 4 | 1 |
| INDUSTRIA AGRO-QUIMICA BRAIDO | 59274167000193 | 2 | 0 | 0 |
| CONTRACT REVESTIMENTOS | 58515495000171 | 0 | 6 | 4 |
| CIASUL COMERCIAL | 53164208000102 | 0 | 6 | 4 |
| ARZ - SERVICOS DE LOCACAO | 34426978000131 | 0 | 9 | 1 |
| ALGT - SERVICOS DE LOCACAO | 14357167000119 | 0 | 8 | 1 |
| PROJETO CRIANDO | 04974850000141 | 1 | 8 | 0 |

CONTRACT e CIASUL falham todo mês pelo mesmo motivo. Uma configuração fiscal
única para todas as empresas não sobrevive a esses dados: a retenção é atributo
do tomador.

## Decisões

1. A nota é emitida na execução do lote de cobrança, logo após a criação do
   boleto, vinculada ao `payment`. Não há polling, worker ou webhook.
2. Falha na emissão da nota não invalida a cobrança. O resultado da nota vive em
   `fiscal_invoices` e aparece na listagem da competência; o item do lote
   continua descrevendo apenas a cobrança, sem alteração de contrato.
3. A configuração fiscal é global, exceto a retenção de ISS, que é atributo da
   empresa no catálogo.
4. O valor da nota é o `TotalAmount` da prévia aprovada, nunca o valor pago:
   juros e multa de boleto atrasado não são serviço prestado.
5. A sincronização de status é sob demanda, por ação do operador na tela. A
   emissão é automática; descobrir o desfecho não precisa ser.
6. A emissão só ocorre em `Production`. O Sandbox não emite NFS-e real e a
   tentativa é registrada como ignorada.

## Fluxo

```text
Lote aprovado → CONFIRMAR
   ↓ por item
ChargeCreationService  → POST /v3/payments      (cobrança)
FiscalInvoiceService   → POST /v3/invoices      (nota vinculada ao payment)
   ↓
fiscal_invoices (Scheduled) + auditoria
   ↓ sob demanda, pelo botão "Atualizar status" da tela
GET /v3/invoices/{id} → Authorized | Failed | Canceled
```

## Componentes

Mantém `Controller → Service → Repository → Banco`, sem camadas novas.

| Arquivo | Responsabilidade |
|---|---|
| `Domain/FiscalInvoice.cs` | Entidade da nota, com as transições de status |
| `Domain/FiscalInvoiceStatus.cs` | `Issuing`, `Scheduled`, `Synchronized`, `Authorized`, `CancellationRequested`, `Canceled`, `CancellationDenied`, `Failed` |
| `Repositories/IFiscalInvoiceRepository.cs` | `AddAsync`, `UpdateAsync`, `ListByBillingPeriodIdAsync`, `ListUnresolvedAsync`, `FindLatestByBillingDraftIdAsync` |
| `Repositories/MySqlFiscalInvoiceRepository.cs` | Persistência MySQL |
| `Repositories/InMemoryFiscalInvoiceRepository.cs` | Persistência em memória, registrada em `Program.cs` como as demais |
| `Integrations/Asaas/IAsaasInvoiceGateway.cs` + `AsaasInvoiceGateway.cs` | `ScheduleInvoiceAsync`, `GetInvoiceAsync` |
| `Integrations/Asaas/FiscalInvoiceOptions.cs` | Serviço municipal, alíquota de ISS, deduções e templates de texto |
| `Services/FiscalInvoiceService.cs` | Regra de emissão, sincronização de status e auditoria |
| `Controllers/FiscalInvoicesController.cs` | `GET` da competência, `POST` de sincronização e `POST` de reemissão |
| `Contracts/FiscalInvoiceContracts.cs` | DTOs explícitos |

`ChargeBatchService.ExecuteItemAsync` passa a chamar `FiscalInvoiceService`
após a cobrança do item ser criada. A chamada fica em seu próprio `try/catch`:
uma exceção da nota é gravada em `fiscal_invoices` e na auditoria, e o lote
segue para o próximo item sem reverter a cobrança. `ChargeBatchResponse` não
muda.

## Requisição enviada ao Asaas

`POST /v3/invoices`:

```csharp
payment              = billingDraft.AsaasPaymentId
value                = billingDraft.TotalAmount
deductions           = 0
effectiveDate        = data da execução do lote
municipalServiceId   = "82367"                                    // configuração
municipalServiceName = $"Serviços prestados em {competência:MM/yyyy}"
serviceDescription   = $"Serviços prestados em {competência:MM/yyyy}."
observations         = ""
externalReference    = $"billing-draft:{id}:version:{version}"
taxes = {
    retainIss = company.RetainsIss,
    iss       = 5.00,
    cofins = 0, csll = 0, inss = 0, ir = 0, pis = 0
}
```

O `externalReference` é a única divergência deliberada do que existe hoje, onde
ele vem vazio. É o que liga nota, prévia, competência e versão aprovada — o
mesmo padrão já usado na criação da cobrança.

Os campos de reforma tributária que aparecem na resposta (`nbsCode`,
`taxSituationCode`, `taxClassificationCode`, `operationIndicatorCode`, IBS e
CBS) são preenchidos pelo Asaas a partir da configuração fiscal da conta e não
são enviados por nós. A nota piloto precisa confirmar que eles saem iguais aos
das notas atuais.

## Retenção de ISS no catálogo

Coluna `retains_iss` em `companies`, `false` por padrão, editável na tela de
Empresas e exposta nos DTOs de empresa. A migration semeia `true` apenas para
as quatro empresas com evidência inequívoca:

- `04902653000117` GERENCIAMENTO AMBIENTAL TECH-LIX — já emite com retenção
- `59274167000193` INDUSTRIA AGRO-QUIMICA BRAIDO — todas as notas retidas
- `58515495000171` CONTRACT REVESTIMENTOS — prefeitura recusa exigindo retenção
- `53164208000102` CIASUL COMERCIAL — prefeitura recusa exigindo retenção

ARZ, ALGT e PROJETO CRIANDO ficam `false`: cada uma tem um caso isolado contra
oito ou nove no sentido oposto. Pendência para a contabilidade confirmar.

## Persistência

`009_add_fiscal_invoices` — a `008` já existe (`008_rename_billing_day_to_closing_day`):

```sql
CREATE TABLE fiscal_invoices (
    id                  CHAR(36)      NOT NULL PRIMARY KEY,
    billing_draft_id    CHAR(36)      NOT NULL,
    billing_period_id   CHAR(36)      NOT NULL,
    sequence            INT           NOT NULL,
    asaas_payment_id    VARCHAR(64)   NOT NULL,
    asaas_invoice_id    VARCHAR(64)   NULL,
    status              VARCHAR(32)   NOT NULL,
    value               DECIMAL(12,2) NOT NULL,
    effective_date      DATE          NOT NULL,
    retains_iss         TINYINT(1)    NOT NULL,
    service_description VARCHAR(500)  NOT NULL,
    error_message       VARCHAR(1000) NULL,
    created_at          DATETIME(6)   NOT NULL,
    updated_at          DATETIME(6)   NOT NULL,
    UNIQUE KEY uk_fiscal_invoices_draft_sequence (billing_draft_id, sequence),
    KEY ix_fiscal_invoices_period (billing_period_id)
);
```

A chave é `(billing_draft_id, sequence)` e não apenas a prévia porque
cancelamento e reemissão acontecem de verdade — a produção tem 12 notas
canceladas e uma com cancelamento negado pela prefeitura.

`010_add_company_iss_retention` adiciona `retains_iss` a `companies` e semeia as
quatro empresas acima. Migrations já aplicadas não são editadas.

## Idempotência, ambiente e auditoria

- O registro é gravado como `Issuing` **antes** da chamada ao Asaas. A chave
  única impede que duas execuções concorrentes emitam duas notas para a mesma
  prévia.
- Uma prévia que já possui nota em qualquer status diferente de `Failed` não
  gera outra.
- Uma nota `Failed` pode ser reemitida pelo operador, o que cria a `sequence`
  seguinte com a configuração vigente naquele momento. É esse caminho que torna
  útil corrigir a retenção de ISS de uma empresa: sem ele, a nota recusada
  ficaria recusada para sempre. A reemissão exige a frase `CONFIRMAR`, como as
  demais operações mutáveis de produção.
- `AsaasConnectionOptions.AllowInvoiceIssuance`, `false` por padrão, é validada
  por `AsaasOperationPolicy.ValidateInvoiceIssuance` e exposta em
  `IntegrationStatusService`, no mesmo molde de `AllowChargeCreation`.
- `StartupConfigurationValidator` recusa subir com `AllowInvoiceIssuance=true` e
  `FiscalInvoiceOptions` incompleta.
- Eventos de auditoria: `fiscal-invoice.scheduled`, `fiscal-invoice.failed`,
  `fiscal-invoice.status-synchronized`, `fiscal-invoice.skipped-sandbox`.

## Erros e visibilidade

A mensagem da prefeitura vem em `statusDescription` e é gravada íntegra em
`error_message` — é ela que diz o que corrigir, como no caso da retenção de
ISS. A tela da competência lista as notas com status e motivo, e uma nota
`Failed` não bloqueia o restante do lote.

Sem essa visibilidade nada muda: as oito notas de CONTRACT e CIASUL estão
recusadas desde maio e ninguém agiu.

## Testes

`FiscalInvoiceServiceTests`, com gateways falsos:

- emite a nota após a cobrança do item ser criada, com os campos da configuração
- envia `retainIss=true` para empresa marcada e `false` para as demais
- usa o `TotalAmount` da prévia, não o valor pago
- não emite segunda nota para uma prévia que já tem nota
- falha do Asaas grava `Failed` com a mensagem da prefeitura e não derruba a
  cobrança nem o lote
- ambiente Sandbox registra `skipped-sandbox` e não chama o gateway
- sincronização mapeia cada status do Asaas para o status interno
- reemissão de nota `Failed` cria a `sequence` seguinte e usa o `retains_iss`
  atual da empresa; reemissão sem `CONFIRMAR` é recusada; nota que não está
  `Failed` não pode ser reemitida

`AsaasOperationPolicyTests`: emissão bloqueada com `AllowInvoiceIssuance=false`
e com ambiente incompatível com a URL.

`ChargeBatchServiceTests`: item do lote conclui a cobrança mesmo quando a nota
falha.

Verificação: `dotnet test Evoque.Billing.slnx --no-restore`.

## Rollout piloto

Primeira execução real com uma empresa só, conferindo a nota no painel antes de
liberar o lote inteiro. Candidata: **LUDMAX COMERCIO ELETRONICO LTDA**
(`45423360000134`) — R$ 79,90, sem retenção de ISS, histórico curto e ativo, com
nota emitida em 02/09/2026. Alternativa: DERMO PHARMACOS (`05872500000137`),
R$ 59,90.

Antes do piloto, a operação precisa desligar a emissão automática de nota no
painel do Asaas para as cobranças criadas pelo software. Se as duas rotas
ficarem ativas, sai nota duplicada para a mesma cobrança — e o cancelamento
depende da prefeitura, que já negou um por competência encerrada.

Depois do piloto: conferir no painel que os campos de reforma tributária vieram
iguais aos das notas atuais e que o PDF saiu com a mesma descrição.

## Riscos e pendências

1. **Nota duplicada** se a configuração automática do painel continuar ligada.
   Bloqueia o piloto até ser confirmado.
2. **Certificado digital** já causou recusa em maio; sua validade é
   pré-requisito operacional e não se resolve em código.
3. **Retenção de ARZ, ALGT e PROJETO CRIANDO** pendente de confirmação contábil.
4. **Sandbox não valida a emissão.** Produção é o primeiro teste real, contido
   pelo piloto de uma empresa e pela flag desligada por padrão.

## Fora de escopo

Cancelamento de nota pela API, reemissão automática após cancelamento, webhooks
de nota fiscal, notas de cobranças criadas direto no painel, configuração fiscal
por empresa além da retenção de ISS e edição de alíquotas pela tela.
