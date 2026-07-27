# Regras de negócio

## Ambientes

- **Sandbox:** pode criar cobranças de teste após aprovação e confirmação.
- **Produção:** permanece bloqueada até autorização operacional explícita e
  configuração independente do Asaas.
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

## Catálogo de empresas

- A empresa é identificada pelo CNPJ normalizado, validado pelos dois dígitos
  verificadores. Uma sequência qualquer de 14 dígitos não é aceita.
- A sincronização da planilha do CRM 2.0 descobre a empresa mesmo quando o
  valor do contrato está vazio, zero ou inválido. Exigir valor positivo é regra
  da prévia de faturamento, não do catálogo.
- Um mesmo CNPJ com nomes diferentes não cria duas empresas: vence o nome mais
  frequente, de forma determinística, e um aviso de conflito é registrado.
- A sincronização atualiza o nome observado no EVO, a contagem de pessoas e as
  datas de aparição. Ela nunca sobrescreve o nome operacional editado, a
  agenda, a situação manual ou os identificadores de cliente Asaas.
- Uma empresa ausente da planilha não é inativada nem excluída. Ela apenas
  deixa de constar como vista na última sincronização.
- Uma empresa inativa que reaparece continua inativa e é marcada para revisão.
- Uma empresa cadastrada manualmente e ausente da planilha continua existindo.
- Não existe exclusão física. Inativar e reativar são operações explícitas que
  preservam prévias, lotes, auditoria e histórico.
- Nenhuma operação do catálogo cria cliente ou cobrança no Asaas. Configurar um
  identificador de cliente apenas registra um vínculo existente.
- A consulta ao cadastro público é enriquecimento: falha, timeout, `404` ou
  `429` não desfazem a sincronização e não apagam dados já obtidos.

## Empresas e ciclos

- Dias de cobrança permitidos: `02`, `18`, `20` e `25`.
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

## Segurança e idempotência

- Cada operação registra operador, data, ambiente e resultado.
- Não criar duas cobranças para a mesma empresa, competência e versão aprovada.
- Erros do Asaas ficam no item do lote e podem ser repetidos somente pelo fluxo
  de retry controlado.
