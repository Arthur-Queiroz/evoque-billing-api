# Regras de negócio

## Ambientes

- **Sandbox:** pode criar cobranças de teste após aprovação e confirmação. O
  boleto gerado é íntegro e traz marca d'água "Boleto para Teste"; é ele que
  valida valor, vencimento, empresa e descrição antes da emissão real.
- **O Sandbox não entrega e-mail de notificação.** Duas cobranças criadas em
  momentos diferentes não produziram nenhuma mensagem, com a caixa comprovada
  recebendo outros remetentes. A configuração está correta dos dois lados —
  cliente com e-mail, `notificationDisabled: false`, `PAYMENT_CREATED` com
  `enabled` e `emailEnabledForCustomer`, conta `APPROVED` — e a documentação do
  Asaas afirma que o envio funciona. Não reinvestigar: o comportamento da
  notificação só pode ser verificado em Produção, onde as cobranças vêm sendo
  pagas há meses. Não existe endpoint para reenviar notificação; isso é um botão
  do painel.
- **Produção:** a credencial independente pode habilitar consultas e vínculo
  de clientes por CNPJ, mas a criação de cobranças permanece bloqueada até
  autorização operacional explícita.
- O seletor visual nunca é autorização suficiente para criar cobranças reais.

## Fluxo de faturamento

```text
Dados correntes do Evo
→ prévia por empresa
→ aprovação da prévia/competência
→ prévia de lote sem chamada ao Asaas
→ aprovação do lote
→ confirmação textual CONFIRMAR
→ criação no Asaas
→ auditoria e resultado por item
```

Uma prévia Sandbox não consolida uma empresa como cobrada. Apenas uma execução
de Produção bem-sucedida consolida a cobrança definitiva.

A prévia financeira tem duas origens. O fluxo normal usa o catálogo local:
colaboradores corporativos ativos com contrato reconhecido são agrupados por
empresa e multiplicados pelo valor por colaborador configurado nela. A
importação de planilha continua disponível como caminho excepcional para
conferência ou contingência. As duas origens criam a mesma `BillingDraft` e
seguem as mesmas etapas de revisão, aprovação e idempotência.

## Catálogo de empresas

- A empresa é identificada pelo CNPJ normalizado, validado pelos dois dígitos
  verificadores. Uma sequência qualquer de 14 dígitos não é aceita.
- **A planilha do CRM 2.0 nunca cria empresa.** A coluna `Profissão` traz o
  empregador do aluno, não a empresa pagadora: das 63 empresas que ela cadastrou
  na primeira versão, a maioria era sindicato, igreja ou plano interno. O
  cadastro é manual e conferido contra os clientes do Asaas.
- A importação só vincula colaborador a empresa já cadastrada. Um CNPJ fora do
  catálogo é devolvido em `UnregisteredCompanies` como pendência.
- Um colaborador cuja empresa não está cadastrada não é vinculado **nem
  inativado**. Uma lacuna do catálogo não pode remover pessoas em silêncio.
- Um mesmo CNPJ com nomes diferentes não cria duas empresas: vence o nome mais
  frequente, de forma determinística, e um aviso de conflito é registrado.
- Uma empresa ausente da planilha não sofre nenhuma alteração.
- O cadastro manual exige o CNPJ. Nome operacional e dia são opcionais; quando
  o nome não é informado, o backend usa nome fantasia ou razão social da
  BrasilAPI. Indisponibilidade externa não impede o cadastro provisório.
- Não existe exclusão física. Inativar e reativar são operações explícitas que
  preservam prévias, lotes, auditoria e histórico.
- Identificadores de cliente Asaas não são preenchidos manualmente. O backend
  resolve o vínculo pelo CNPJ e o persiste com auditoria.
- No Sandbox, a resolução reutiliza o cliente de teste existente ou cria um
  espelho com e-mail controlado quando ele ainda não existe.
- Em Produção, a resolução é somente leitura: localiza exatamente um cliente e
  registra o vínculo interno. Ausência ou duplicidade viram pendência; o
  software não cria nem altera cliente real nessa etapa.
- A consulta ao cadastro público é enriquecimento: falha, timeout, `404` ou
  `429` não desfazem o cadastro e não apagam dados já obtidos.

## Colaboradores corporativos

- A identidade do colaborador é o `IdCliente` exportado pelo EVO. Nome não é
  chave e CPF não é armazenado para esse fim.
- Várias linhas ou contratos do mesmo `IdCliente` representam uma única pessoa
  com uma coleção de contratos, sem repetição visual.
- A aplicação de uma importação exige a confirmação explícita de que o arquivo
  é a exportação completa de clientes ativos do CRM 2.0.
- Um colaborador ativo ausente da exportação completa é inativado, nunca
  excluído. Se reaparecer na mesma empresa, é reativado.
- Mudança automática de empresa não faz parte do fluxo. Se o mesmo `IdCliente`
  aparecer sob outro CNPJ, a importação é bloqueada como conflito e nenhum
  vínculo é alterado.
- A prévia deve informar novos, mantidos, inativados, reativados e conflitos
  antes da confirmação.

## Empresas e ciclos

- Dias de **fechamento** permitidos: `02`, `18`, `20` e `25`. O dia guardado na
  agenda é quando o período de serviço fecha, **não** o vencimento do boleto.
  No histórico real do Asaas, um período "do dia 26/05 ao dia 25/06" vence em
  06/07: os vencimentos caem em 06, 10, 12, 27 e 30, quase sempre no mês
  seguinte. Enquanto o lote agendado filtrava empresas pelo dia do vencimento,
  ele nunca encontrava nenhuma.
- O vencimento é escolhido por lote e só não pode ser anterior ao fechamento.
  Exigir que ele pertença ao mês da competência rejeitava o caso normal.
- O boleto continua pagável por um prazo após o vencimento, definido pelo padrão
  da conta Asaas. Não enviamos `daysAfterDueDateToRegistrationCancellation`, e
  esse atributo não pode ser alterado depois da criação da cobrança: mudar a
  regra exige emitir outra cobrança.
- Uma empresa inativa no catálogo não entra em lote agendado, mesmo que reste
  uma agenda ativa antiga.
- Uma empresa sem agenda não entra em lote dos dias `02`, `18`, `20` ou `25`.
- Uma execução recorrente seleciona somente empresas com agenda ativa naquele
  dia e prévias aprovadas na competência.
- Aprovar todas as prévias existentes não encerra definitivamente a
  competência. Uma nova empresa pode ser importada posteriormente e a
  competência volta para revisão, pois os ciclos `02`, `18`, `20` e `25`
  acontecem em momentos diferentes do mesmo mês.
- O status `ChargesCreated` representa encerramento e não deve ser atribuído
  automaticamente após o primeiro lote do mês.
- O vínculo necessário é explícito e auditável:

```text
planilha do CRM 2.0 → empresa pagadora → CNPJ → cliente Asaas → dia
```

- Não inferir empresa pagadora a partir do nome de um colaborador.
- O Asaas envia o boleto/e-mail ao cliente cadastrado; o MVP não depende de
  Gmail ou OAuth do GCP.

## Nota fiscal de serviço

- A nota é emitida no mesmo lote autorizado que cria a cobrança, vinculada ao
  `payment`, sem esperar o pagamento. É a prática já existente na conta: das 264
  notas lidas da produção em 07/09/2026, praticamente todas saem de 2 a 12 dias
  **antes** do vencimento, e há notas autorizadas para boletos ainda pendentes.
- O valor da nota é o total da prévia aprovada, nunca o valor pago. Juros e
  multa de boleto atrasado não são serviço prestado.
- A configuração fiscal é única — serviço municipal `82367`, ISS de 5%, demais
  tributos e deduções zerados — exceto a retenção de ISS, que é atributo do
  tomador e vive no cadastro da empresa.
- A prefeitura recusa a nota quando a retenção está errada **nos dois sentidos**.
  Quatro empresas nascem com retenção ligada por evidência da produção; ARZ,
  ALGT e Projeto Criando seguem pendentes de confirmação contábil.
- **Falha na nota nunca invalida a cobrança.** O boleto já foi criado e cobrado
  do cliente; marcar o item do lote como falho apagaria o identificador da
  cobrança e deixaria um boleto real órfão. A recusa fica registrada na nota e
  na auditoria, e o lote segue.
- Uma prévia tem no máximo uma nota viva. Só uma nota recusada pode ser
  reemitida, e a reemissão é recusada quando outra nota da mesma prévia já está
  viva — uma nota recusada continua recusada para sempre, inclusive depois de
  uma reemissão bem-sucedida.
- A reemissão cria a sequência seguinte, usa a retenção de ISS atual da empresa
  e exige a frase `CONFIRMAR`. É esse caminho que torna útil corrigir o cadastro
  de uma empresa cuja nota foi recusada.
- **O Sandbox emite NFS-e.** Quem decide é a configuração do ambiente, não o
  nome dele: `FiscalInvoiceService` consulta `CanIssueInvoices(asaasEnvironment)`
  e não existe caminho que ignore a emissão por ser Sandbox. Comprovado em
  15/09/2026 com a nota `inv_000000549777`, que chegou a `AUTHORIZED` sozinha em
  cerca de sete minutos, com PDF e XML. A credencial de prefeitura usada era
  fictícia, o que confirma que o ambiente é simulado.
- A emissão exige `AllowInvoiceIssuance` habilitado, desligado por padrão, além
  da política de ambiente já existente.
- A sincronização de status é acionada pela tela, não por processo em segundo
  plano. Emitir é automático; descobrir o desfecho não precisa ser — mas precisa
  existir, porque notas recusadas já ficaram meses sem ninguém notar.

## Segurança e idempotência

- Cada operação registra operador, data, ambiente e resultado.
- Não criar duas cobranças para a mesma empresa, competência e versão aprovada.
- Erros do Asaas ficam no item do lote e podem ser repetidos somente pelo fluxo
  de retry controlado.
