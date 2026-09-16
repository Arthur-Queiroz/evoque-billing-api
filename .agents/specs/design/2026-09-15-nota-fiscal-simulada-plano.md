# Nota fiscal simulada — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer a nota fiscal ser emitida por inteiro no Asaas Sandbox e deixar o PDF resultante acessível pelo portal.

**Architecture:** O cliente espelho do Sandbox passa a nascer com o endereço que o catálogo já tem, porque sem ele o Asaas recusa a nota. O gateway passa a capturar `pdfUrl` e `xmlUrl`, a entidade a guardá-los, e a tela de Notas fiscais a oferecer "Abrir nota". O gatilho não muda: executar um lote em Teste já emite.

**Tech Stack:** ASP.NET Core (net10.0), MySqlConnector, xUnit, Next.js/TypeScript.

**Spec:** `.agents/specs/design/2026-09-15-nota-fiscal-simulada-design.md`

---

## Estrutura de arquivos

**Modificar (API):**

| Arquivo | Mudança |
|---|---|
| `Evoque.Billing.Api/Integrations/Asaas/IAsaasCustomerGateway.cs` | `CreateSandboxAsync` recebe o endereço |
| `Evoque.Billing.Api/Integrations/Asaas/AsaasCustomerGateway.cs` | Envia o endereço ao Asaas |
| `Evoque.Billing.Api/Services/CompanyAsaasSynchronizationService.cs` | Repassa o endereço do catálogo |
| `Evoque.Billing.Api/Integrations/Asaas/IAsaasInvoiceGateway.cs` | `AsaasInvoiceState` ganha `PdfUrl` e `XmlUrl` |
| `Evoque.Billing.Api/Integrations/Asaas/AsaasInvoiceGateway.cs` | Lê `pdfUrl` e `xmlUrl` da resposta |
| `Evoque.Billing.Api/Domain/FiscalInvoice.cs` | `PdfUrl`, `XmlUrl` e `AttachDocuments` |
| `Evoque.Billing.Api/Services/FiscalInvoiceService.cs` | Captura documentos na sincronização |
| `Evoque.Billing.Api/Repositories/MySqlFiscalInvoiceRepository.cs` | Persiste as duas colunas |
| `Evoque.Billing.Api/Repositories/DatabaseSchemaInitializer.cs` | Migration `012` |
| `Evoque.Billing.Api/Contracts/FiscalInvoiceContracts.cs` | Expõe as URLs |
| `Evoque.Billing.Api.Tests/CompanyAsaasSynchronizationServiceTests.cs` | Testes do endereço |
| `Evoque.Billing.Api.Tests/FiscalInvoiceServiceTests.cs` | Testes dos documentos |

**Modificar (Web):**

| Arquivo | Mudança |
|---|---|
| `src/lib/api.ts` | `FiscalInvoice` ganha `pdfUrl` e `xmlUrl` |
| `src/app/page.tsx` | Link "Abrir nota" na tela de Notas fiscais |

**Verificação (todas as tasks da API):**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Baseline: **144 testes verdes, zero warnings.**

---

### Task 1: Endereço no cliente espelho do Sandbox

Sem endereço completo, o Asaas recusa a nota com `Endereço do cliente incompleto.; CEP do cliente é inválido.` — foi onde a simulação manual travou.

**Files:**
- Modify: `Evoque.Billing.Api/Integrations/Asaas/IAsaasCustomerGateway.cs`
- Modify: `Evoque.Billing.Api/Integrations/Asaas/AsaasCustomerGateway.cs`
- Modify: `Evoque.Billing.Api/Services/CompanyAsaasSynchronizationService.cs`
- Test: `Evoque.Billing.Api.Tests/CompanyAsaasSynchronizationServiceTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

O arquivo já tem `CreateScenario(AsaasCustomerLookupResult)`, o record
`TestScenario(Service, Gateway, DataStore)`, a classe `StubAsaasCustomerGateway`
e as constantes `CompanyTaxId = "56087276000103"` e
`OperatorId = "operador@evoque"`. Use tudo isso; não crie duplicatas.

Primeiro, no `StubAsaasCustomerGateway`, acrescente a captura do endereço junto
de `CreateSandboxCallCount`:

```csharp
        public CompanyRegistryAddress? LastSandboxAddress { get; private set; }
```

e dentro de `CreateSandboxAsync`, logo depois de `CreateSandboxCallCount++;`:

```csharp
            LastSandboxAddress = registryAddress;
```

Depois acrescente os dois testes, junto dos demais `[Fact]`:

```csharp
    /// <summary>
    /// O Asaas recusa a nota fiscal quando o tomador não tem endereço completo.
    /// O catálogo já recebe esse endereço da BrasilAPI, então o espelho de teste
    /// nasce com ele — é o que torna a simulação fiel ao que produção fará.
    /// </summary>
    [Fact]
    public async Task SynchronizeSandboxAsync_SendsTheCatalogAddressWhenCreatingTheMirror()
    {
        var scenario = CreateScenario(AsaasCustomerLookupResult.NotFound());
        var company = scenario.DataStore.Companies[CompanyTaxId];
        company.ApplyRegistryData(
            "OPEN SPORTS LTDA",
            "Open Sports",
            "ATIVA",
            new CompanyRegistryAddress(
                "Rua Silva Jardim",
                "270",
                "Sala 4",
                "Centro",
                "Santo André",
                "SP",
                "09190370"),
            DateTimeOffset.UtcNow);

        await scenario.Service.SynchronizeSandboxAsync(
            CompanyTaxId,
            new SynchronizeCompanyAsaasSandboxRequest("teste@evoque.com.br", OperatorId),
            CancellationToken.None);

        Assert.Equal(1, scenario.Gateway.CreateSandboxCallCount);
        var endereco = scenario.Gateway.LastSandboxAddress;
        Assert.NotNull(endereco);
        Assert.Equal("Rua Silva Jardim", endereco.Street);
        Assert.Equal("270", endereco.Number);
        Assert.Equal("Centro", endereco.Neighborhood);
        Assert.Equal("09190370", endereco.PostalCode);
    }

    /// <summary>
    /// Uma empresa sem endereço no catálogo continua tendo o espelho criado. A
    /// nota dela falhará com a mensagem do Asaas, que a tela exibe — é melhor
    /// expor a lacuna do que escondê-la com um endereço inventado.
    /// </summary>
    [Fact]
    public async Task SynchronizeSandboxAsync_StillCreatesTheMirrorWithoutACatalogAddress()
    {
        var scenario = CreateScenario(AsaasCustomerLookupResult.NotFound());

        var resultado = await scenario.Service.SynchronizeSandboxAsync(
            CompanyTaxId,
            new SynchronizeCompanyAsaasSandboxRequest("teste@evoque.com.br", OperatorId),
            CancellationToken.None);

        Assert.Equal(1, scenario.Gateway.CreateSandboxCallCount);
        Assert.Null(scenario.Gateway.LastSandboxAddress);
        Assert.Equal("Linked", resultado.Status);
    }
```

`CreateScenario` monta a empresa sem endereço, por isso o segundo teste não
precisa preparar nada.

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~CompanyAsaasSynchronizationServiceTests"
```

Esperado: erro de compilação — `CreateSandboxAsync` ainda não recebe endereço.

- [ ] **Step 3: Mudar a interface**

Em `IAsaasCustomerGateway.cs`, substitua a assinatura:

```csharp
    Task<AsaasCustomer> CreateSandboxAsync(
        string name,
        string taxId,
        string email,
        CompanyRegistryAddress? registryAddress,
        CancellationToken cancellationToken);
```

`CompanyRegistryAddress` está em `Evoque.Billing.Api.Domain`; o arquivo já
importa esse namespace.

- [ ] **Step 4: Enviar o endereço ao Asaas**

Em `AsaasCustomerGateway.cs`, altere `CreateSandboxAsync` para receber o
parâmetro novo e montar o corpo assim:

```csharp
        // O Asaas exige endereço completo do tomador para emitir NFS-e. Enviar
        // o do catálogo, que vem da BrasilAPI, faz o cliente de teste refletir
        // o que a produção encontrará.
        var requestBody = registryAddress is null
            ? (object)new
            {
                name,
                cpfCnpj = taxId,
                email,
                notificationDisabled = false,
            }
            : new
            {
                name,
                cpfCnpj = taxId,
                email,
                notificationDisabled = false,
                address = registryAddress.Street,
                addressNumber = registryAddress.Number,
                complement = registryAddress.Complement,
                province = registryAddress.Neighborhood,
                postalCode = registryAddress.PostalCode,
            };

        using var response = await httpClient.PostAsJsonAsync("customers", requestBody, cancellationToken);
```

O restante do método permanece igual.

- [ ] **Step 5: Repassar o endereço do catálogo**

Em `CompanyAsaasSynchronizationService.cs`, na chamada dentro de
`SynchronizeSandboxAsync`:

```csharp
            customer = await asaasCustomerGateway.CreateSandboxAsync(
                company.DisplayName,
                company.TaxId,
                email!,
                company.RegistryAddress,
                cancellationToken);
```

- [ ] **Step 6: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: PASS, com os dois testes novos somados aos 144.

- [ ] **Step 7: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Integrations/Asaas/IAsaasCustomerGateway.cs Evoque.Billing.Api/Integrations/Asaas/AsaasCustomerGateway.cs Evoque.Billing.Api/Services/CompanyAsaasSynchronizationService.cs Evoque.Billing.Api.Tests/CompanyAsaasSynchronizationServiceTests.cs
git commit -m "Give the sandbox mirror the address the invoice needs

Asaas refuses an invoice when the payer has no complete address, which is
where issuing one by hand in the sandbox stopped. The catalog already
holds that address from BrasilAPI, so the test mirror is created with it
and the simulation reflects what production will face.

A company without an address in the catalog still gets a mirror. Its
invoice fails with the Asaas message, now visible on screen, which is
better than hiding the gap behind an invented address.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 2: Capturar PDF e XML no gateway

**Files:**
- Modify: `Evoque.Billing.Api/Integrations/Asaas/IAsaasInvoiceGateway.cs`
- Modify: `Evoque.Billing.Api/Integrations/Asaas/AsaasInvoiceGateway.cs`

Gateways HTTP não têm teste unitário neste repositório — `AsaasChargeGateway` e
`AsaasCustomerGateway` seguem o mesmo padrão, e a cobertura fica na política e
nos serviços. A verificação aqui é build limpo; o comportamento é coberto pela
Task 4.

- [ ] **Step 1: Acrescentar os campos ao record de estado**

Em `IAsaasInvoiceGateway.cs`, substitua:

```csharp
public sealed record AsaasInvoiceState(
    string InvoiceId,
    string Status,
    string? StatusDescription,
    string? PdfUrl,
    string? XmlUrl);
```

`AsaasInvoiceCreation` **não muda**: na criação a nota nasce `SCHEDULED` e o
Asaas ainda não gerou documento.

- [ ] **Step 2: Ler as URLs da resposta**

Em `AsaasInvoiceGateway.cs`, substitua o record privado:

```csharp
    private sealed record AsaasInvoiceResponse(
        string? Id,
        string? Status,
        string? StatusDescription,
        string? PdfUrl,
        string? XmlUrl);
```

E, no fim de `GetInvoiceAsync`, o retorno:

```csharp
        return new AsaasInvoiceState(
            responseData.Id,
            responseData.Status,
            responseData.StatusDescription,
            responseData.PdfUrl,
            responseData.XmlUrl);
```

`ScheduleInvoiceAsync` continua devolvendo `AsaasInvoiceCreation` como está.

- [ ] **Step 3: Compilar**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
```

Esperado: erro de compilação em `FiscalInvoiceServiceTests.cs`, onde o gateway
falso constrói `AsaasInvoiceState` com três argumentos. Corrija lá acrescentando
`null, null` aos construtores existentes — a Task 4 os substitui por valores de
verdade.

Depois disso, build limpo e suíte verde.

- [ ] **Step 4: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Integrations/Asaas/IAsaasInvoiceGateway.cs Evoque.Billing.Api/Integrations/Asaas/AsaasInvoiceGateway.cs Evoque.Billing.Api.Tests/FiscalInvoiceServiceTests.cs
git commit -m "Read the invoice documents Asaas already returns

The gateway parsed only id, status and description, discarding the pdf and
xml links that come with an authorized invoice.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 3: Guardar os documentos na entidade

**Files:**
- Modify: `Evoque.Billing.Api/Domain/FiscalInvoice.cs`
- Test: `Evoque.Billing.Api.Tests/FiscalInvoiceTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

Acrescente em `FiscalInvoiceTests.cs`:

```csharp
    [Fact]
    public void AttachDocuments_StoresTheLinksAsaasReturned()
    {
        var fiscalInvoice = CreateFiscalInvoice();
        fiscalInvoice.MarkScheduled("inv_000000549258", CreatedAt.AddMinutes(1));

        fiscalInvoice.AttachDocuments(
            "https://www.asaas.com/file/public/download/pdf",
            "https://www.asaas.com/file/public/download/xml",
            CreatedAt.AddMinutes(2));

        Assert.Equal("https://www.asaas.com/file/public/download/pdf", fiscalInvoice.PdfUrl);
        Assert.Equal("https://www.asaas.com/file/public/download/xml", fiscalInvoice.XmlUrl);
        Assert.True(fiscalInvoice.HasDocuments);
    }

    /// <summary>
    /// Uma nota agendada ainda não tem documento. Uma sincronização que volta
    /// sem as URLs não pode apagar as que já foram guardadas.
    /// </summary>
    [Fact]
    public void AttachDocuments_KeepsWhatWasAlreadyStoredWhenAsaasReturnsNothing()
    {
        var fiscalInvoice = CreateFiscalInvoice();
        fiscalInvoice.MarkScheduled("inv_000000549258", CreatedAt.AddMinutes(1));
        fiscalInvoice.AttachDocuments("https://pdf", "https://xml", CreatedAt.AddMinutes(2));

        fiscalInvoice.AttachDocuments(null, null, CreatedAt.AddMinutes(3));

        Assert.Equal("https://pdf", fiscalInvoice.PdfUrl);
        Assert.Equal("https://xml", fiscalInvoice.XmlUrl);
    }

    [Fact]
    public void HasDocuments_IsFalseWhileTheInvoiceHasNoLinks()
    {
        var fiscalInvoice = CreateFiscalInvoice();

        Assert.False(fiscalInvoice.HasDocuments);
        Assert.Null(fiscalInvoice.PdfUrl);
        Assert.Null(fiscalInvoice.XmlUrl);
    }
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~FiscalInvoiceTests"
```

Esperado: erro de compilação — `AttachDocuments`, `PdfUrl`, `XmlUrl` e
`HasDocuments` não existem.

- [ ] **Step 3: Acrescentar as propriedades**

Em `FiscalInvoice.cs`, logo abaixo de `ErrorMessage`:

```csharp
    /// <summary>
    /// Links do PDF e do XML que o Asaas gera quando a nota é autorizada. São
    /// públicos e estáveis, então guardamos a URL em vez de baixar o arquivo.
    /// </summary>
    public string? PdfUrl { get; private set; }

    public string? XmlUrl { get; private set; }

    public bool HasDocuments => !string.IsNullOrWhiteSpace(PdfUrl);
```

- [ ] **Step 4: Acrescentar o método**

Em `FiscalInvoice.cs`, logo depois de `ApplyAsaasStatus`:

```csharp
    /// <summary>
    /// Guarda os documentos devolvidos pelo Asaas. Separado de
    /// <see cref="ApplyAsaasStatus"/> porque as duas coisas são independentes:
    /// o status muda antes de existir documento, e uma consulta posterior pode
    /// trazer o documento sem que o status mude.
    ///
    /// Uma resposta sem URL não apaga o que já foi guardado — o Asaas só
    /// devolve os links depois de autorizar.
    /// </summary>
    public void AttachDocuments(string? pdfUrl, string? xmlUrl, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(pdfUrl) && string.IsNullOrWhiteSpace(xmlUrl))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(pdfUrl))
        {
            PdfUrl = pdfUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(xmlUrl))
        {
            XmlUrl = xmlUrl.Trim();
        }

        UpdatedAt = updatedAt;
    }
```

- [ ] **Step 5: Acrescentar ao `Restore`**

Em `FiscalInvoice.cs`, o construtor privado e o `Restore` precisam carregar os
dois campos, para o repositório MySQL reidratar a entidade.

No **construtor privado**, acrescente os parâmetros depois de `errorMessage`:

```csharp
        string? errorMessage,
        string? pdfUrl,
        string? xmlUrl,
        DateTimeOffset createdAt,
```

e, no corpo, junto das demais atribuições:

```csharp
        PdfUrl = pdfUrl;
        XmlUrl = xmlUrl;
```

No **construtor público**, que delega ao privado, passe `null, null` na posição
correspondente — uma nota nasce sem documento.

No **`Restore`**, acrescente os mesmos parâmetros depois de `errorMessage` e
repasse-os ao construtor privado.

- [ ] **Step 6: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~FiscalInvoiceTests"
```

Esperado: PASS. A suíte completa ainda falha em `MySqlFiscalInvoiceRepository`,
que a Task 4 corrige.

- [ ] **Step 7: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Domain/FiscalInvoice.cs Evoque.Billing.Api.Tests/FiscalInvoiceTests.cs
git commit -m "Let the invoice carry the documents it produced

AttachDocuments is separate from ApplyAsaasStatus because the two are
independent: the status changes before any document exists, and a later
query can bring the document without the status moving. A response
without links never erases what was already stored.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 4: Capturar os documentos na sincronização

Aqui mora a sutileza: `SynchronizeAsync` hoje faz `continue` quando o status não
muda, **pulando o `UpdateAsync`**. Se os documentos chegarem sem mudança de
status, seriam descartados.

**Files:**
- Modify: `Evoque.Billing.Api/Services/FiscalInvoiceService.cs`
- Test: `Evoque.Billing.Api.Tests/FiscalInvoiceServiceTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

Em `FiscalInvoiceServiceTests.cs`, o gateway falso precisa devolver documentos.
Acrescente ao `RecordingAsaasInvoiceGateway` um construtor opcional e use-o em
`GetInvoiceAsync`:

```csharp
        private string? pdfUrlOnGet;
        private string? xmlUrlOnGet;

        public void ReturnDocumentsOnGet(string? pdfUrl, string? xmlUrl)
        {
            pdfUrlOnGet = pdfUrl;
            xmlUrlOnGet = xmlUrl;
        }
```

e o retorno de `GetInvoiceAsync` passa a ser:

```csharp
            return Task.FromResult(new AsaasInvoiceState(
                asaasInvoiceId,
                statusOnGet,
                null,
                pdfUrlOnGet,
                xmlUrlOnGet));
```

Acrescente então os testes:

```csharp
    [Fact]
    public async Task SynchronizeAsync_StoresTheDocumentsAsaasReturned()
    {
        var context = await CreateApprovedDraftAsync();
        await ExecuteProductionBatchAsync(context);
        context.InvoiceGateway.ReturnDocumentsOnGet("https://asaas/pdf", "https://asaas/xml");

        await context.FiscalInvoiceService.SynchronizeAsync(
            new BillingPeriodReference(2026, 8),
            OperatorId,
            CancellationToken.None);

        var fiscalInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));
        Assert.Equal("https://asaas/pdf", fiscalInvoice.PdfUrl);
        Assert.Equal("https://asaas/xml", fiscalInvoice.XmlUrl);
    }

    /// <summary>
    /// Regressão: o laço pulava a persistência quando o status não mudava. Se os
    /// documentos chegassem nessa passagem, seriam descartados.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_StoresDocumentsEvenWhenTheStatusDoesNotChange()
    {
        var context = await CreateApprovedDraftAsync(
            invoiceGateway: new RecordingAsaasInvoiceGateway(statusOnGet: "SCHEDULED"));
        await ExecuteProductionBatchAsync(context);
        context.InvoiceGateway.ReturnDocumentsOnGet("https://asaas/pdf", null);

        await context.FiscalInvoiceService.SynchronizeAsync(
            new BillingPeriodReference(2026, 8),
            OperatorId,
            CancellationToken.None);

        var fiscalInvoice = Assert.Single(
            await context.FiscalInvoiceRepository.ListByBillingPeriodIdAsync(
                context.BillingPeriodId,
                CancellationToken.None));
        Assert.Equal(FiscalInvoiceStatus.Scheduled, fiscalInvoice.Status);
        Assert.Equal("https://asaas/pdf", fiscalInvoice.PdfUrl);
    }
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~FiscalInvoiceServiceTests"
```

Esperado: o primeiro teste falha porque nada guarda os documentos; o segundo
falha porque o `continue` pula a persistência.

- [ ] **Step 3: Capturar os documentos antes de decidir persistir**

Em `FiscalInvoiceService.SynchronizeAsync`, substitua o miolo do laço:

```csharp
            var invoiceState = await asaasInvoiceGateway.GetInvoiceAsync(
                fiscalInvoice.AsaasEnvironment,
                fiscalInvoice.AsaasInvoiceId,
                cancellationToken);

            var previousStatus = fiscalInvoice.Status;
            var hadDocuments = fiscalInvoice.HasDocuments;

            fiscalInvoice.ApplyAsaasStatus(
                invoiceState.Status,
                invoiceState.StatusDescription,
                DateTimeOffset.UtcNow);
            fiscalInvoice.AttachDocuments(
                invoiceState.PdfUrl,
                invoiceState.XmlUrl,
                DateTimeOffset.UtcNow);

            // Persiste quando qualquer um dos dois mudou. Olhar só o status
            // descartaria o documento que chega sem o status se mover.
            var statusChanged = fiscalInvoice.Status != previousStatus;
            var documentsArrived = fiscalInvoice.HasDocuments && !hadDocuments;
            if (!statusChanged && !documentsArrived)
            {
                continue;
            }

            await fiscalInvoiceRepository.UpdateAsync(fiscalInvoice, cancellationToken);
            if (!statusChanged)
            {
                continue;
            }
```

O `RegisterAuditAsync` que vem em seguida permanece como está — auditoria
registra mudança de status, e a chegada de um documento não é mudança de estado
do negócio.

- [ ] **Step 4: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: PASS.

- [ ] **Step 5: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Services/FiscalInvoiceService.cs Evoque.Billing.Api.Tests/FiscalInvoiceServiceTests.cs
git commit -m "Keep the documents that arrive without a status change

The loop skipped persistence whenever the status stayed the same, so a
document arriving in that pass would have been thrown away. It now
persists when either the status or the documents moved, and still audits
only the status change, which is what the business cares about.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 5: Persistência e migration 012

**Files:**
- Modify: `Evoque.Billing.Api/Repositories/MySqlFiscalInvoiceRepository.cs`
- Modify: `Evoque.Billing.Api/Repositories/DatabaseSchemaInitializer.cs`

A migration `009` **não é editada** — já foi aplicada em produção. Editá-la foi
o erro que derrubou a listagem de notas com erro 500 em 15/09.

- [ ] **Step 1: Acrescentar as colunas ao repositório**

Em `MySqlFiscalInvoiceRepository.cs`:

Em `SelectColumns`, acrescente `pdf_url, xml_url,` logo depois de
`error_message,`.

Em `AddAsync`, acrescente `pdf_url, xml_url,` à lista de colunas e
`@pdfUrl, @xmlUrl,` à de valores, nas mesmas posições.

Em `UpdateAsync`, acrescente ao `SET`:

```sql
                pdf_url = @pdfUrl,
                xml_url = @xmlUrl,
```

e os parâmetros, junto dos demais:

```csharp
        command.Parameters.AddWithValue("@pdfUrl", (object?)fiscalInvoice.PdfUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@xmlUrl", (object?)fiscalInvoice.XmlUrl ?? DBNull.Value);
```

Em `AddParameters`, acrescente as mesmas duas linhas.

Em `ReadFiscalInvoice`, acrescente depois de `GetNullableString(reader, "error_message"),`:

```csharp
            GetNullableString(reader, "pdf_url"),
            GetNullableString(reader, "xml_url"),
```

A ordem precisa casar com a de `FiscalInvoice.Restore`, onde os dois campos
ficam depois de `errorMessage` e antes de `createdAt`.

- [ ] **Step 2: Acrescentar a migration**

Em `DatabaseSchemaInitializer.cs`, junto dos demais identificadores:

```csharp
    private const string FiscalInvoiceDocumentsMigrationId = "012_add_fiscal_invoice_documents";
```

Em `ApplyLatestMigrationsAsync`, ao final:

```csharp
        await AddFiscalInvoiceDocumentsAsync(connection, cancellationToken);
```

E o método, junto dos demais:

```csharp
    /// <summary>
    /// PDF e XML que o Asaas gera ao autorizar a nota. São links públicos e
    /// estáveis; guardamos a URL em vez de baixar o arquivo.
    /// </summary>
    private static async Task AddFiscalInvoiceDocumentsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (await IsAppliedAsync(connection, FiscalInvoiceDocumentsMigrationId, cancellationToken))
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await ColumnExistsAsync(connection, transaction, "fiscal_invoices", "pdf_url", cancellationToken))
        {
            await ExecuteAsync(connection, """
                ALTER TABLE fiscal_invoices
                ADD COLUMN pdf_url TEXT NULL AFTER error_message,
                ADD COLUMN xml_url TEXT NULL AFTER pdf_url;
                """, transaction, cancellationToken);
        }

        await InsertMigrationAsync(
            connection,
            transaction,
            FiscalInvoiceDocumentsMigrationId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
```

`ColumnExistsAsync` já existe, criado pela migration `011`.

- [ ] **Step 3: Compilar e rodar a suíte**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: build limpo e PASS. As migrations não rodam nos testes; a verificação
aqui é leitura cuidadosa do SQL, conferindo nome por nome contra
`ReadFiscalInvoice`.

- [ ] **Step 4: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Repositories/MySqlFiscalInvoiceRepository.cs Evoque.Billing.Api/Repositories/DatabaseSchemaInitializer.cs
git commit -m "Persist the invoice documents

Migration 012 adds pdf_url and xml_url. Migration 009 is left as
production applied it; editing an applied migration is what broke the
invoice listing earlier today.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 6: Expor as URLs no contrato

**Files:**
- Modify: `Evoque.Billing.Api/Contracts/FiscalInvoiceContracts.cs`

- [ ] **Step 1: Acrescentar ao record**

Em `FiscalInvoiceResponse`, acrescente depois de `string? ErrorMessage,`:

```csharp
    string? PdfUrl,
    string? XmlUrl,
```

E em `FromDomain`, na mesma posição:

```csharp
            fiscalInvoice.PdfUrl,
            fiscalInvoice.XmlUrl,
```

- [ ] **Step 2: Compilar e rodar a suíte**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: build limpo, PASS.

- [ ] **Step 3: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Contracts/FiscalInvoiceContracts.cs
git commit -m "Expose the invoice documents to the portal

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 7: Link "Abrir nota" na tela

**Files:**
- Modify: `C:\prog\evoque\web\client\src\lib\api.ts`
- Modify: `C:\prog\evoque\web\client\src\app\page.tsx`

Repositório diferente: `C:\prog\evoque\web\client`.

- [ ] **Step 1: Acrescentar ao tipo**

Em `src/lib/api.ts`, na interface `FiscalInvoice`, depois de
`errorMessage: string | null;`:

```ts
  pdfUrl: string | null;
  xmlUrl: string | null;
```

- [ ] **Step 2: Mostrar o link**

Em `src/app/page.tsx`, dentro de `FiscalInvoicesPage`, na célula de ação da
tabela, substitua o conteúdo atual por:

```tsx
                    <td className="px-5 py-4 text-right">
                      {invoice.status === "Failed" ? (
                        <button className="button-secondary h-9" onClick={() => onReissue(invoice)}>
                          <RefreshCw size={15} />Reemitir
                        </button>
                      ) : invoice.pdfUrl ? (
                        <a
                          className="inline-flex items-center gap-1.5 text-sm font-extrabold text-orange hover:underline"
                          href={invoice.pdfUrl}
                          target="_blank"
                          rel="noopener noreferrer"
                        >
                          <FileText size={15} />Abrir nota
                        </a>
                      ) : (
                        <span className="text-xs font-semibold text-slate-400">Sem ação</span>
                      )}
                    </td>
```

`FileText` e `RefreshCw` já estão importados no arquivo.

- [ ] **Step 3: Compilar**

```bash
cd C:\prog\evoque\web\client
npm run build
```

Esperado: `Compiled successfully`.

- [ ] **Step 4: Commit**

```bash
cd C:\prog\evoque\web\client
git add src/lib/api.ts src/app/page.tsx
git commit -m "Open the issued invoice from the screen

An issued invoice now links to the document Asaas generated, the same way
the batch card already links to the bank slip. A scheduled invoice has no
document yet, so no link is shown.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 8: Verificar a simulação ponta a ponta

A spec deixa uma questão em aberto que precisa ser respondida **antes** de dar a
implementação como pronta: a nota chega a `AUTHORIZED` sozinha no Sandbox, ou só
depois de `POST /invoices/{id}/authorize`?

No teste manual de 15/09 ela só avançou após a chamada explícita, mas isso não
foi isolado.

- [ ] **Step 1: Subir a API com o Sandbox habilitado**

Os user-secrets locais já têm `Asaas:Sandbox:AllowInvoiceIssuance = true`.

```bash
cd C:\prog\evoque\api\Evoque.Billing.Api
$env:ASPNETCORE_URLS = "http://localhost:5207"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile
```

- [ ] **Step 2: Executar um lote Sandbox para uma empresa com endereço**

Use a Open Sports (`56087276000103`) ou a Web Prado (`43322169000170`), que já
têm cliente espelho. Percorra competência, empresa, sincronização Sandbox,
prévia, aprovação, lote, aprovação e `CONFIRMAR`.

A sincronização precisa rodar **depois** da Task 1, para o espelho receber o
endereço. Se o cliente já existir sem endereço, o `FindByTaxIdAsync` o reutiliza
e o endereço não é preenchido — nesse caso, complete o endereço uma vez pelo
painel do Sandbox ou apague o cliente de teste para ele ser recriado.

- [ ] **Step 3: Sincronizar e observar**

```bash
curl.exe -s -X POST "http://localhost:5207/api/billing-periods/2026/9/fiscal-invoices/synchronize" -H "Content-Type: application/json" -d "{\"operatorId\":\"verificacao\"}"
```

Repita a cada 30 segundos, por até cinco minutos.

**Resultado A — a nota chega a `Authorized` sozinha, com `pdfUrl` preenchido.**
A implementação está completa. Registre o tempo observado no fim da spec.

**Resultado B — ela para em `Scheduled` ou `Synchronized` indefinidamente.**
Não improvise. Pare, registre o que foi observado e leve a decisão ao dono do
produto: se o botão "Atualizar situação" deve antecipar a autorização **apenas
em Sandbox**, deixando a produção intocada, ou se a simulação termina em
`Synchronized` mesmo.

- [ ] **Step 4: Registrar o que foi observado**

Acrescente ao fim de `.agents/specs/design/2026-09-15-nota-fiscal-simulada-design.md`,
substituindo a seção "A verificar durante a implementação" pelo que aconteceu de
fato, com a data.

- [ ] **Step 5: Commit**

```bash
cd C:\prog\evoque\api
git add .agents/specs/design/2026-09-15-nota-fiscal-simulada-design.md
git commit -m "Record how the sandbox invoice reaches its final status

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

## Depois do plano

Atualizar `.agents/PENDENCIAS.md`: o item **2.3** passa a resolvido, e o **2.4**
ganha a observação de que a simulação agora expõe o problema de endereço antes
de ligar a emissão em produção.
