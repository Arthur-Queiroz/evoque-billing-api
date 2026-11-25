# Operação do faturamento mensal

Passo a passo validado de ponta a ponta em 25/11/2026, na competência 11/2026,
em Sandbox: 25 prévias, R$ 8.898,94, proporcional de entrada calculado com o
EVO real e lotes executados com boleto de teste criado no Asaas.

As telas citadas são as do client web (`evoque.devarthur.com.br`).

## Antes de começar

- Ambiente no topo da tela: **Sandbox** para teste, **Produção** só com
  autorização operacional explícita (ver `BUSINESS_RULES.md`).
- Competência no seletor do topo: o mês que está sendo faturado.
- Empresas do ciclo precisam de **valor por colaborador**, **dia de fechamento
  com agenda ativa** e **cliente Asaas** vinculado no ambiente escolhido. Sem
  valor, a empresa aparece como "Empresa sem valor por colaborador cadastrado";
  com agenda inativa, a prévia é criada mas não entra no lote do dia.

## 1. Iniciar a competência

**Visão geral** ou **Cobranças** → **Iniciar faturamento de {mês}**.

Só cria a competência. Nada vai ao Asaas.

## 2. Atualizar os colaboradores pela exportação do CRM

1. No Portal EVO, exportar a **lista completa de clientes ativos do CRM 2.0**.
   Um filtro aplicado na planilha não atrapalha: o leitor ignora linhas
   ocultas e processa todas.
2. **Colaboradores** → **Atualizar pelo EVO** (a tela se chama "Atualizar
   empresas e colaboradores"). Também se chega a ela por **Empresas** →
   **Adicionar em lote**.
3. **Conferir dados** e revisar antes de confirmar:
   - **Saíram (inativados):** devem ser pessoas com contrato corporativo
     cancelado. Dezenas de inativados numa empresa ativa indicam arquivo
     filtrado ou parcial — não confirmar.
   - **Novos e reativados:** quem pode sair com proporcional.
   - **Conflitos:** mesma pessoa sob outro CNPJ; bloqueiam a importação.
   - **Avisos "A linha não possui empresa e CNPJ no formato esperado":** a
     Profissão não termina com CNPJ. Para EVOPASS é esperado. Para contrato
     `EVOQUE CORPORATIVO - ...` é receita perdida: a pessoa não entra em prévia
     nenhuma e, se já estava no catálogo, é inativada. A correção é a Evoque
     preencher a Profissão no EVO como `EMPRESA - 00000000000000` e exportar de
     novo.
4. Marcar a confirmação de exportação completa e **Atualizar base**.

> **Não use "Importar fechamento do EVO"** (card escuro em Cobranças) com a
> exportação do CRM. Aquela tela é para a planilha financeira de fechamento:
> ela soma a coluna "Valor do contrato", que na exportação do CRM é o que os
> alunos EVOPASS pagam (R$ 23.830,20 em 11/2026), e descarta os corporativos,
> que vêm com valor zero.

## 3. Gerar as prévias

**Cobranças** → **Faturamento do dia** → **Gerar prévias corporativas**.

Gera uma prévia por empresa, para todos os dias de fechamento de uma vez. No
resultado, conferir:

- total por empresa e a data de fechamento de cada uma;
- **proporcionais**: colaborador, data de entrada, dias/30 e valor;
- **Proporcional não verificado (cobrado cheio):** deve vir vazia ou quase.
  Se vier com "Evo indisponível" para todos, a credencial do EVO está
  quebrada (ver Problemas conhecidos);
- **Entraram depois do fechamento:** ficam para a próxima competência;
- **Empresas não faturadas:** sem valor, inativas ou "Já existe uma prévia
  desta empresa nesta competência" — clicar de novo em gerar não duplica.

## 4. Aprovar as prévias

Na tabela **Prévias por empresa**, **Aprovar todas as pendentes** (ou
**Aprovar** linha a linha).

Quando todas as prévias ficam aprovadas, a **competência é aprovada
automaticamente**. Sem competência aprovada, a execução do lote falha com "A
competência precisa estar aprovada antes de criar cobranças".

## 5. Criar e executar o lote do dia

Ainda em **Faturamento do dia**:

1. Escolher o **dia de fechamento** (02, 18, 20 ou 25) e conferir em
   **Empresas do ciclo** se são as empresas esperadas.
2. **Vencimento do boleto**: não pode ser anterior ao fechamento; normalmente
   cai no mês seguinte.
3. **Gerar prévia do dia N**. Ainda não envia nada ao Asaas.
4. No cartão do lote em **Cobranças e lotes criados**: **Aprovar**, depois
   **Executar** e digitar `CONFIRMAR`.

Repetir para cada dia de fechamento do mês.

## 6. Conferir o resultado

- **Histórico** lista as cobranças emitidas com link do boleto. A tela só
  recarrega ao entrar no sistema ou mudar um filtro: depois de executar um
  lote, atualizar a página (F5).
- O boleto de teste também aparece no painel do Asaas Sandbox, com marca
  d'água "Boleto para Teste".

## Repetir um teste em Sandbox

Uma cobrança Sandbox não marca a prévia como cobrada, então **executar o mesmo
dia duas vezes cria boletos em dobro** — foi o que aconteceu com o dia 20 em
25/11/2026. Para refazer o fluxo do zero na mesma competência:

- prévias cobradas em Sandbox: **Substituir teste**;
- prévias não cobradas: **Cancelar**.

Na próxima geração, elas voltam como versão 2.

## O que não acontece hoje

- **Nota fiscal:** a emissão está desligada em produção
  (`Asaas__AllowInvoiceIssuance` não configurado).
- **E-mail do boleto no Sandbox:** o Asaas não entrega; só é verificável em
  Produção.
- **Card "Valor nas matrículas" (Visão geral):** soma os contratos de uma
  amostra de 50 alunos ativos da rede inteira no EVO. Não se refere às
  empresas do catálogo nem ao faturamento.

## Problemas conhecidos

**EVO responde HTTP 401 / "Evo indisponível" em todos os colaboradores.** Os
segredos da API vêm de `/var/lib/vps-apps/evoque/secrets.env` (lidos pelo
`deployctl`), não de `/opt/evoque/production.env`, que é resto do deploy
antigo. O `deployctl` passa os valores literalmente: um valor entre aspas
chega à API com as aspas. Em 24/11/2026 `Evo__Username`, `Evo__ApiKey` e
`Asaas__Production__ApiKey` estavam entre aspas simples; a correção foi
removê-las (com backup) e reexecutar
`deployctl release evoque api <digest atual>`, que recria o container com a
mesma imagem. Não alterar `MYSQL_PASSWORD`.

**Tela quebra depois de "Atualizar base"** (`reading 'toString'`): corrigido
no web em 11/2026 (PR #20). A importação era gravada mesmo com a tela quebrada.
