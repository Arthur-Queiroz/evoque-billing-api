# Geração e visualização de nota fiscal simulada

Data: 15/09/2026
Status: aprovado para implementação

## Objetivo

Permitir que a emissão de nota fiscal seja exercitada por inteiro no Asaas
Sandbox, com o documento resultante acessível pelo portal, sem cobrar ninguém e
sem envolver a prefeitura real.

A pergunta veio da reunião de apresentação: a nota fiscal só pode funcionar em
produção? A resposta é não, e a maior parte do caminho já existe.

## O que já funciona

Desde o PR #12, `FiscalInvoiceService` decide emitir pela configuração do
ambiente, não pelo nome dele. Executar um lote em Sandbox já solicita a nota.

Comprovado em 15/09/2026 contra o Asaas Sandbox: a nota `inv_000000549258`
chegou a `AUTHORIZED`, com número 549258, RPS 2, PDF, XML, ISS de 5% e o
`externalReference` no padrão `billing-draft:`. A credencial de prefeitura
usada era fictícia, o que confirma que o ambiente é simulado.

Cada recusa ao longo do teste revelou um requisito:

| Tentativa | Resposta do Asaas |
|---|---|
| sem configuração | `invalid_fiscal_info` |
| com dados fiscais | exige credencial de prefeitura |
| com credencial fictícia | `Endereço do cliente incompleto.; CEP do cliente é inválido.` |
| com endereço completo | nota criada, `SCHEDULED` |

Os dois primeiros são configuração da conta Sandbox, já feita. O terceiro é
código, e é o item 1 desta spec.

## Decisões

1. **O gatilho não muda.** A nota sai junto do lote Sandbox, pelo mesmo caminho
   da produção. Simular um fluxo diferente do real não validaria o real.
2. **O endereço vem do catálogo**, que já o recebe da BrasilAPI. Endereço
   fictício fixo deixaria de validar justamente o dado que falha em produção.
3. **O PDF é link direto do Asaas.** Não baixamos nem hospedamos arquivo.
4. **A espera usa o botão "Atualizar situação"** que já existe. Sem polling.
5. **Não chamamos `authorize` automaticamente.** Em produção a data efetiva pode
   ser deliberada, e antecipar seria mudança de comportamento fiscal.

## Componentes

### 1. Endereço no cliente espelho

`AsaasCustomerGateway.CreateSandboxAsync` hoje envia apenas `name`, `cpfCnpj`,
`email` e `notificationDisabled`. Passa a enviar também `address`,
`addressNumber`, `province` e `postalCode`.

`CompanyAsaasSynchronizationService` lê esses valores de
`Company.RegistryAddress` e os repassa.

Uma empresa sem endereço no catálogo continua tendo o espelho criado, como hoje.
A nota dela falhará com a mensagem do Asaas, que a tela agora exibe. É
deliberado: expõe a mesma lacuna que a produção terá, em vez de escondê-la com
um endereço inventado.

### 2. Captura dos documentos

`AsaasInvoiceResponse`, no gateway, passa a ler `pdfUrl` e `xmlUrl`.

`AsaasInvoiceState` os carrega:

```csharp
public sealed record AsaasInvoiceState(
    string InvoiceId,
    string Status,
    string? StatusDescription,
    string? PdfUrl,
    string? XmlUrl);
```

`AsaasInvoiceCreation` **não** muda: na criação a nota nasce `SCHEDULED` e ainda
não tem documento.

### 3. Persistência

`FiscalInvoice` ganha `PdfUrl` e `XmlUrl`, com um método próprio:

```csharp
public void AttachDocuments(string? pdfUrl, string? xmlUrl, DateTimeOffset updatedAt)
```

Separado de `ApplyAsaasStatus` porque são coisas distintas — o status pode mudar
sem documento, e o documento pode chegar sem o status mudar. O padrão segue
`MarkScheduled` e `MarkFailed`, que também são métodos pequenos e explícitos.

`FiscalInvoiceService.SynchronizeAsync` chama `AttachDocuments` quando o estado
devolvido traz as URLs, e persiste.

Migration `012_add_fiscal_invoice_documents`, acrescentando `pdf_url` e
`xml_url` como `TEXT NULL`. A `009` não é editada.

### 4. Contrato e tela

`FiscalInvoiceResponse` expõe `pdfUrl` e `xmlUrl`.

A tela de Notas fiscais ganha **"Abrir nota"** na coluna de ação, no mesmo
formato do "Abrir boleto" já existente no card do lote. O link só aparece quando
há documento: nota agendada ainda não tem.

O fluxo do operador passa a ser: executa o lote em Teste, a nota aparece como
"Agendada", ele clica em "Atualizar situação", ela vira "Emitida" e o link
aparece.

## A verificar durante a implementação

No teste manual a nota só chegou a `AUTHORIZED` depois de uma chamada explícita
a `POST /invoices/{id}/authorize`. Não foi confirmado se ela chegaria sozinha —
a data efetiva era o próprio dia, então é provável, mas não verificado.

Se não chegar, o operador ficaria olhando "Agendada" indefinidamente no Sandbox.
Nesse caso, a decisão a tomar é se o botão "Atualizar situação" deve antecipar a
autorização **apenas em Sandbox**, mantendo a produção intocada.

Isso precisa ser verificado antes de a implementação ser dada como pronta, não
depois.

## Erros

Falha ao emitir continua não derrubando a cobrança nem o lote, como já
implementado. A mensagem do Asaas é gravada íntegra e exibida.

Uma empresa sem endereço produzirá uma falha previsível e legível, que é o
resultado desejado.

## Testes

`AsaasCustomerGateway` / `CompanyAsaasSynchronizationServiceTests`:

- a criação do espelho envia o endereço do catálogo;
- empresa sem endereço ainda cria o espelho, sem exceção.

`FiscalInvoiceServiceTests`:

- a sincronização captura `pdfUrl` e `xmlUrl` e os persiste;
- uma nota sem documento permanece sem link;
- a falha por endereço incompleto grava a mensagem do Asaas e preserva a
  cobrança.

Verificação: `dotnet test Evoque.Billing.slnx --no-restore` e `npm run build`.

## Fora de escopo

Baixar ou hospedar o PDF, visualizador embutido no portal, atualização
automática da tela, botão de simular avulso por prévia, prévia local dos dados
antes de emitir, e preencher endereço de cliente em produção — este último é o
item 2.4 de `PENDENCIAS.md` e tem risco próprio.

## Pendências relacionadas

- **2.3** de `PENDENCIAS.md`: completar a simulação. Esta spec a resolve.
- **2.4**: endereço do tomador em produção. Fica em aberto, e esta spec torna o
  problema visível antes de ligar a emissão real.
