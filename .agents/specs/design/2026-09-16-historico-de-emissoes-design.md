# Histórico de emissões

Data: 16/09/2026
Status: aprovado para implementação

## Objetivo

Uma tela que lista, em ordem cronológica, cada cobrança que este sistema
emitiu — com empresa, competência, ambiente, situação do boleto, situação da
nota fiscal e os links para os dois documentos.

## O problema que ela resolve

Em 15/09/2026 um operador executou um lote Sandbox, o boleto foi criado, e ele
não conseguiu encontrá-lo na tela.

O boleto existia e abria: `https://sandbox.asaas.com/b/pdf/sq9ksjh5zsq4rvtu`,
39,3 KB. Ele estava no lote concluído, colapsado atrás de "Mostrar 1 lote(s)
concluído(s)", enquanto o card visível era um segundo lote ainda não executado,
com "0 criadas".

Hoje, para achar um boleto, é preciso saber a competência certa, ir até
Faturamento do dia e expandir um bloco fechado. Não existe forma de perguntar
"o que foi emitido para a Farmava nos últimos meses".

## Decisões

1. **Escopo: o que este sistema emitiu.** Não as 304 cobranças da conta Asaas,
   quase todas criadas pelo painel. Elas não têm competência nem vínculo com
   prévia do nosso lado; exibi-las seria um espelho pobre do painel.
2. **Uma linha por cobrança**, não por lote. O lote é a unidade de autorização,
   não a de consulta.
3. **Item próprio na barra lateral**, chamado Histórico.
4. **Situação de pagamento por botão explícito**, no mesmo padrão das notas
   fiscais. A tela lê do nosso banco e abre rápido.
5. **O ambiente é destacado visualmente**, com os rótulos Teste e Real já
   adotados. Boleto de teste indistinguível de um real é como se confunde os
   dois.

## A linha

```text
30/09/2026  ·  Teste          Farmava                         R$ 299,50
               Setembro/2026 · 5 pessoas
               Boleto: Pago em 02/10     Nota: Emitida    [Abrir boleto] [Abrir nota]
```

Campos: data de emissão, ambiente, empresa, valor, competência, número de
pessoas, situação do boleto, situação da nota, link do boleto, link da nota.

Um link só aparece quando o documento existe.

## Busca e filtros

- **Busca por empresa**, por nome ou CNPJ. Digitar "Farmava" lista as cobranças
  dela em ordem cronológica.
- **Filtro de ambiente**: todos, Teste, Real.
- **Filtro de situação do boleto**: todos, em aberto, pago, vencido, falhou.
- **Filtro de competência**.

## Componentes

### Consulta que atravessa competências

`IChargeBatchRepository` hoje só oferece `ListByBillingPeriodIdAsync`. O
histórico precisa de uma consulta nova, que não dependa de competência e aceite
os filtros acima com paginação.

### Junção dos quatro lugares

O dado de uma linha está espalhado:

| Origem | Campos |
|---|---|
| `ChargeBatch` | data, ambiente |
| `ChargeBatchItem` | identificador da cobrança, boleto, situação |
| `BillingDraft` | empresa, CNPJ, valor, número de pessoas |
| `FiscalInvoice` | situação da nota, PDF |

Um serviço de consulta reúne isso numa linha. Ele lê; não altera nada.

### Situação de pagamento

`IAsaasChargeGateway` só sabe criar:

```csharp
Task<AsaasChargeCreation> CreateChargeAsync(...);
```

Ganha a leitura:

```csharp
Task<AsaasChargeState> GetChargeAsync(
    AsaasEnvironment asaasEnvironment,
    string asaasPaymentId,
    CancellationToken cancellationToken);
```

`ChargeBatchItem` ganha a situação de pagamento e a data em que foi pago, com
migration própria. Os estados observados na conta real são `PENDING`,
`RECEIVED`, `CONFIRMED` e `OVERDUE`; um estado desconhecido é ignorado, como
`FiscalInvoice.ApplyAsaasStatus` já faz, para que um valor novo do Asaas não
derrube a sincronização das demais.

A sincronização consulta apenas cobranças ainda em aberto — uma cobrança paga
não muda mais — e é acionada por botão.

## Erros

Falha ao consultar uma cobrança não interrompe as demais nem derruba a tela: a
linha mantém a última situação conhecida. É a mesma regra que já vale para nota
fiscal, e pela mesma razão — uma indisponibilidade externa não pode apagar o que
já sabemos.

## Testes

- a consulta atravessa competências e devolve em ordem cronológica decrescente;
- a busca por empresa encontra por nome e por CNPJ normalizado;
- o filtro de ambiente separa Teste de Real;
- uma cobrança sem nota fiscal aparece sem link de nota;
- a sincronização não consulta cobrança já paga;
- uma falha de consulta preserva a situação anterior e não interrompe as outras.

## Fora de escopo

As cobranças criadas pelo painel do Asaas, exportação, gráficos, processo em
segundo plano, e cancelamento ou estorno pela tela.

## Uma consequência a antecipar

Na primeira versão a tela mostrará **17 cobranças**, porque é o que o sistema
emitiu até aqui — a operação ainda trabalha pelo painel. A tela dirá isso
explicitamente, em vez de parecer quebrada.

O histórico fica completo à medida que a operação migra. Prometer histórico
completo e entregar 17 de 304 seria pior do que ser claro.

## Relação com as pendências

Isto atende, em forma concreta, o item **2.1** de `PENDENCIAS.md` — auditoria e
rastreamento de faturamentos passados, levantado na reunião de 15/09. Não o
substitui: a consulta ao log de auditoria por operador e por período continua
aberta, e o item **2.5** (operador fixo em `"operador-web"`) segue reduzindo o
valor de qualquer trilha de auditoria.
