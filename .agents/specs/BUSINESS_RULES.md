# Regras de negócio

## Ambientes

- **Sandbox:** pode criar cobranças de teste após aprovação e confirmação.
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

## Catálogo de empresas

- A empresa é identificada pelo CNPJ normalizado, validado pelos dois dígitos
  verificadores. Uma sequência qualquer de 14 dígitos não é aceita.
- A inclusão em lote pela planilha do CRM 2.0 descobre a empresa mesmo quando o
  valor do contrato está vazio, zero ou inválido. Exigir valor positivo é regra
  da prévia de faturamento, não do catálogo.
- Um mesmo CNPJ com nomes diferentes não cria duas empresas: vence o nome mais
  frequente, de forma determinística, e um aviso de conflito é registrado.
- A planilha cadastra somente CNPJs inexistentes. Uma empresa já cadastrada é
  ignorada no catálogo: nome, agenda, situação e vínculos Asaas não são
  atualizados. Os colaboradores presentes na mesma planilha são comparados com
  a base persistente.
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
