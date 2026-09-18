# Valor por empresa, e a prévia sem planilha montada à mão

Data: 18/09/2026
Status: aprovado para implementação

## Objetivo

Guardar quanto cada empresa paga por colaborador, e gerar a prévia de
faturamento a partir da base de colaboradores que o sistema já mantém.

O resultado é que a exportação crua do EVO passa a bastar. Ninguém filtra nem
preenche planilha antes de faturar.

## O problema

O EVO exporta **uma única planilha completa**. Não existe exportação por empresa
nem relatório de fechamento: as planilhas "por empresa" que circulam são a
completa com um autofiltro do Excel aplicado, e as planilhas "organizadas" eram
montadas à mão.

Medido na exportação de 18/09/2026, 525 linhas:

| Contrato | Linhas | Com valor |
|---|---|---|
| `EVOQUE CORPORATIVO - COBRANÇA INTERMEDIADA` | 98 | 0 |
| `EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO` | 70 | 0 |
| `EVOPASS RECORRENTE` (5 faixas) | 354 | 354 |

**Os colaboradores corporativos saem com valor zerado**, porque o desconto
acontece em folha. Quem tem valor é o assinante EVOPASS, que paga a própria
academia e não deve ser cobrado de empresa nenhuma.

Por isso o importador de fechamento nunca serviu para a exportação crua: ele
exige valor positivo por linha, e recusaria exatamente as 168 pessoas que
interessam enquanto aceitaria as 354 que não.

### O que a Geovanna estava fazendo

Não era reformatar. Era **suprir o valor**, que o EVO não fornece. Essa é a razão
de nenhum conversor de formato ter resolvido o problema — o dado ausente não
estava no arquivo em outro formato, estava fora dele.

### Dois detalhes que enganam

**Filtro do Excel não filtra para um programa.** Uma planilha "da BIO" tem 3
linhas visíveis e 522 ocultas, e as 525 continuam no arquivo. O leitor do sistema
não trata ocultação, então subir a filtrada é idêntico a subir a completa.

**Nem todo contrato com "CORPORATIVO" no nome é cobrável.** A exportação traz
`EVOQUE CORPORATIVO RECORRENTE - 39,95`, que vem com valor R$ 59,90 preenchido —
quem paga é a própria pessoa. E trazia `VIP (até 6 meses) - EVOQUE CORPORATIVO`,
uma cortesia já encerrada.

## A prova de que o cálculo está certo

`colaboradores × valor` foi conferido contra dois fechamentos que a Geovanna
montou à mão, por caminhos independentes:

| Empresa | Cálculo | Fechamento manual |
|---|---|---|
| Open Sports | 24 × 89,90 = **2.157,60** | 2.157,60 |
| Web Prado | 4 × 109,90 = **439,60** | 439,60 |

O segundo é o mesmo R$ 439,60 registrado em `PROJECT_CONTEXT.md` como validação
de julho/2026.

Aplicado às 28 empresas com colaboradores cobráveis: **R$ 12.334,60 por mês**,
154 colaboradores.

## Decisões

1. **Um valor por empresa**, não por tipo de contrato. Metade das empresas tem
   pessoas em intermediada e em folha ao mesmo tempo, e pagam o mesmo pelas duas.
   No controle operacional, apenas 3 de 115 empresas têm titular e dependente
   diferentes, e nenhuma delas está entre as 28 cobráveis.
2. **Opcional para cadastrar, obrigatório para faturar.** Exigir no cadastro
   quebraria a regra de que basta o CNPJ, e travaria cadastrar uma empresa nova
   antes de alguém saber o valor combinado.
3. **Lista explícita de contratos cobráveis**, não padrão de texto. Um contrato
   novo criado no EVO não entra em cobrança sozinho, e também não some sem
   ninguém ver.
4. **Dois passos**, com conferência no meio. Atualizar quem trabalha onde e criar
   o que será cobrado são decisões diferentes.
5. **A prévia não lê planilha.** Ela lê a base de colaboradores e o catálogo. O
   que ela produz é exatamente o que o passo anterior já mostrou.

## O campo

`Company.AmountPerMember`, decimal, opcional, com migration própria. O catálogo
tem 39 empresas com vínculo Asaas, agenda e histórico conferidos à mão: a coluna
é acrescentada, não recriada.

## Quem entra na conta

Colaborador **ativo**, vinculado à empresa, cujo contrato esteja na lista:

```text
EVOQUE CORPORATIVO - COBRANÇA INTERMEDIADA
EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO
```

A base já guarda o nome do contrato em `CorporateMemberContract.ContractName`.

Contrato corporativo fora da lista **não entra e aparece na tela** para alguém
decidir.

A lista vive **no código**, não em configuração de ambiente. Mudar quem é cobrado
é mudança de regra de faturamento e merece revisão e histórico, não uma variável
que alguém edita na VPS às pressas. O custo é precisar de deploy para acomodar um
contrato novo; o benefício é que ninguém acrescenta uma fonte de cobrança sem
que isso apareça num diff.

A tela é o que torna esse custo aceitável: um contrato desconhecido não trava o
faturamento das demais empresas, só fica de fora e visível até alguém decidir.

## O fluxo

```text
1) sobe a PLANILHA COMPLETA, crua
   → base de colaboradores atualizada: novos, mantidos, inativados, conflitos
   → o operador confere

2) "gerar prévias da competência"
   → 28 prévias, cada uma com N itens de valor igual
   → o operador aprova uma a uma, como hoje
```

O passo 1 já existe e não muda. O passo 2 é um serviço novo, somente leitura
sobre a base e o catálogo, que cria as prévias.

### Camadas

```text
BillingDraftsController ──→ CorporateBillingDraftService
                              ↘ ICorporateMemberRepository  (quem, e de onde)
                              ↘ ICompanyRepository          (valor e situação)
                              ↘ IBillingDraftRepository     (grava a prévia)
```

O serviço novo não conhece planilha, e o leitor de planilha não conhece valor por
empresa. São dois caminhos para criar prévia e eles não se cruzam.

## O que impede uma prévia, e como aparece

| Situação | O que acontece | Na exportação de 18/09 |
|---|---|---|
| Empresa sem valor cadastrado | não gera, listada com nome e CNPJ | depende do cadastro |
| Empresa sem dia de fechamento | gera, mas não entra em lote agendado | 2 |
| Empresa inativa | não gera | — |
| Colaborador sem CNPJ na Profissão | não entra em prévia nenhuma | **14** |
| Contrato corporativo desconhecido | não entra, listado para decisão | 2 tipos |
| Prévia já existente na competência | não gera de novo, informa | — |

A primeira linha depende do cadastro, não da exportação: **hoje nenhuma das 28
tem valor no catálogo, porque o campo ainda não existe.** Os valores das 28 são
conhecidos — 26 vieram do controle operacional e 2 foram informados pela Evoque —
e precisam ser carregados antes da primeira geração. Sem isso, o passo 2 recusa
todas as 28, corretamente.

A tela do passo 2 mostra o que **não** será cobrado **antes** de o operador
aprovar o que será. Erro silencioso aqui é receita perdida que ninguém procura:
os 14 colaboradores sem CNPJ valem cerca de R$ 1.100 por mês.

Os sem CNPJ e as empresas sem valor precisam de **lista permanente na tela**, não
só um aviso no momento da importação. Um aviso que passa é um aviso que não
existe.

## Erros

Uma empresa que não gera prévia não interrompe as outras. A geração percorre as
28 e devolve, ao fim, o que criou e o que não criou com o motivo de cada uma —
mesma regra que já vale para item de lote e para nota fiscal, e pela mesma razão.

Gerar duas vezes na mesma competência não duplica: a prévia existente é
preservada e informada. A regra de uma prévia por empresa por competência
continua valendo, e é ela que protege contra o clique repetido.

## Testes

- `colaboradores × valor` reproduz Open Sports em 2.157,60 e Web Prado em 439,60,
  com os números reais;
- empresa sem valor não gera prévia e aparece no resultado;
- colaborador inativo não entra na contagem;
- colaborador de contrato EVOPASS não entra, mesmo com empresa cadastrada;
- contrato corporativo fora da lista não entra e é reportado;
- gerar duas vezes na mesma competência não cria prévia duplicada;
- uma empresa que falha não impede as demais.

## Fora de escopo

Proporcional para quem entra no meio do ciclo, histórico de preço por
competência, valor por colaborador, e valor diferente para titular e dependente —
existem 3 casos assim em 115 empresas e nenhum entre as cobráveis.

O importador de planilha de fechamento **permanece como está**. Ele deixa de ser
o caminho normal, mas removê-lo é decisão separada, depois que o caminho novo
provar-se em uso.

## Pendências que isto torna mais urgentes

**Cancelar prévia** (item 1.2). Gerar 28 de uma vez significa que um valor errado
produz uma prévia errada que hoje não tem como desfazer, e ela bloqueia a
competência daquela empresa. Passa de incômodo a necessário.

**Empresas sem dia de fechamento.** Plastpel e Ciasul não constam do controle
operacional, então falta o dia além do valor. A Plastpel tem 14 colaboradores e
R$ 838,60 por mês, e não entraria em lote agendado nenhum.

## Uma observação sobre os dias de fechamento

O controle operacional traz os ciclos **2, 20 e 25**, com 13, 31 e 23 empresas.
Não existe dia 18 ali, embora `BUSINESS_RULES.md` liste `02, 18, 20, 25` como
permitidos. Vale confirmar com a operação se o 18 ainda é usado, se virou outro
dia, ou se o controle está desatualizado — antes que alguém procure empresas de
um grupo que não existe.
