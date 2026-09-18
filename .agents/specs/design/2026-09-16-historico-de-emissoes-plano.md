# Histórico de emissões — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Uma tela que lista cronologicamente cada cobrança que este sistema emitiu, com empresa, ambiente, situação do boleto e da nota, e os links dos dois documentos.

**Architecture:** Um modelo de leitura próprio junta o que hoje está espalhado por quatro agregados. Ele não altera nada — só consulta. A situação de pagamento entra depois, numa segunda metade do plano, para que a tela já seja útil antes dela.

**Tech Stack:** ASP.NET Core (net10.0), MySqlConnector, xUnit, Next.js/TypeScript.

**Spec:** `.agents/specs/design/2026-09-16-historico-de-emissoes-design.md`

---

## Ordem deliberada

As Tasks 1 a 4 entregam a tela funcionando, sem situação de pagamento. As Tasks
5 a 9 acrescentam a situação de pagamento sobre ela.

Quem parar na Task 4 tem software útil: o operador acha o boleto, que é o
problema que originou esta feature.

## Estrutura de arquivos

**Criar (API):**

| Arquivo | Responsabilidade |
|---|---|
| `Evoque.Billing.Api/Domain/ChargeHistoryEntry.cs` | Uma linha do histórico, já reunida |
| `Evoque.Billing.Api/Domain/ChargeHistoryFilter.cs` | Filtros aceitos pela consulta |
| `Evoque.Billing.Api/Repositories/IChargeHistoryRepository.cs` | Contrato do modelo de leitura |
| `Evoque.Billing.Api/Repositories/InMemoryChargeHistoryRepository.cs` | Implementação em memória |
| `Evoque.Billing.Api/Repositories/MySqlChargeHistoryRepository.cs` | Implementação MySQL |
| `Evoque.Billing.Api/Services/ChargeHistoryService.cs` | Regra de consulta e resposta |
| `Evoque.Billing.Api/Contracts/ChargeHistoryContracts.cs` | DTOs HTTP |
| `Evoque.Billing.Api/Controllers/ChargeHistoryController.cs` | `GET` e `POST` de sincronização |
| `Evoque.Billing.Api/Domain/ChargePaymentStatus.cs` | Situação de pagamento |
| `Evoque.Billing.Api/Services/ChargePaymentSynchronizationService.cs` | Atualiza a situação junto ao Asaas |
| `Evoque.Billing.Api.Tests/ChargeHistoryServiceTests.cs` | Testes da consulta |
| `Evoque.Billing.Api.Tests/ChargePaymentSynchronizationServiceTests.cs` | Testes da sincronização |

**Modificar (API):**

| Arquivo | Mudança |
|---|---|
| `Evoque.Billing.Api/Domain/ChargeBatchItem.cs` | Situação de pagamento e data |
| `Evoque.Billing.Api/Integrations/Asaas/IAsaasChargeGateway.cs` | `GetChargeAsync` |
| `Evoque.Billing.Api/Integrations/Asaas/AsaasChargeGateway.cs` | Implementação |
| `Evoque.Billing.Api/Repositories/MySqlChargeBatchRepository.cs` | Persiste os campos novos |
| `Evoque.Billing.Api/Repositories/DatabaseSchemaInitializer.cs` | Migration `013` |
| `Evoque.Billing.Api/Program.cs` | Registro do repositório e dos serviços |
| `Evoque.Billing.Api.Tests/BillingWorkflowTests.cs` | Ajuste do gateway falso |
| `Evoque.Billing.Api.Tests/FiscalInvoiceServiceTests.cs` | Ajuste do gateway falso |

**Modificar (Web), em `C:\prog\evoque\web\client`:**

| Arquivo | Mudança |
|---|---|
| `src/lib/api.ts` | Tipos e chamadas do histórico |
| `src/app/page.tsx` | Tela, item na barra lateral, busca e filtros |

**Verificação (todas as tasks da API):**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Baseline: **153 testes verdes, zero warnings.**

---

### Task 1: O modelo de leitura

Uma linha do histórico reúne dados de quatro agregados. Ela é um modelo de
leitura: não tem comportamento, não é persistida, e existe só para responder à
consulta. Por isso não entra em `IChargeBatchRepository`, que é do lado de
escrita.

**Files:**
- Create: `Evoque.Billing.Api/Domain/ChargeHistoryEntry.cs`
- Create: `Evoque.Billing.Api/Domain/ChargeHistoryFilter.cs`
- Create: `Evoque.Billing.Api/Repositories/IChargeHistoryRepository.cs`

- [ ] **Step 1: Criar a linha do histórico**

`Evoque.Billing.Api/Domain/ChargeHistoryEntry.cs`:

```csharp
namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Uma cobrança emitida por este sistema, com o que é preciso para encontrá-la:
/// quando saiu, para quem, em qual ambiente, e os documentos que ela produziu.
///
/// É um modelo de leitura. Reúne dados de <see cref="ChargeBatch"/>,
/// <see cref="ChargeBatchItem"/>, <see cref="BillingDraft"/> e
/// <see cref="FiscalInvoice"/>, e não é persistido em tabela própria.
/// </summary>
public sealed record ChargeHistoryEntry(
    Guid ChargeBatchId,
    Guid BillingDraftId,
    BillingPeriodReference BillingPeriodReference,
    AsaasEnvironment AsaasEnvironment,
    string CompanyName,
    string CompanyTaxId,
    decimal TotalAmount,
    int MemberCount,
    DateOnly DueDate,
    DateTimeOffset IssuedAt,
    ChargeBatchItemStatus ItemStatus,
    string? AsaasPaymentId,
    string? BankSlipUrl,
    string? ItemErrorMessage,
    FiscalInvoiceStatus? FiscalInvoiceStatus,
    string? FiscalInvoicePdfUrl,
    string? FiscalInvoiceErrorMessage);
```

- [ ] **Step 2: Criar os filtros**

`Evoque.Billing.Api/Domain/ChargeHistoryFilter.cs`:

```csharp
namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Filtros da consulta ao histórico. Todos opcionais: sem nenhum, devolve tudo
/// que o sistema emitiu, do mais recente para o mais antigo.
/// </summary>
public sealed record ChargeHistoryFilter
{
    /// <summary>Nome da empresa ou CNPJ, parcial. Compara sem acento e sem caixa.</summary>
    public string? CompanySearch { get; init; }

    public AsaasEnvironment? AsaasEnvironment { get; init; }

    public BillingPeriodReference? BillingPeriodReference { get; init; }
}
```

- [ ] **Step 3: Criar o contrato do repositório**

`Evoque.Billing.Api/Repositories/IChargeHistoryRepository.cs`:

```csharp
using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

/// <summary>
/// Consulta somente leitura sobre as cobranças emitidas, atravessando
/// competências. `IChargeBatchRepository` responde por uma competência de cada
/// vez, que é o que o fluxo de emissão precisa; o histórico precisa do oposto.
/// </summary>
public interface IChargeHistoryRepository
{
    Task<IReadOnlyCollection<ChargeHistoryEntry>> ListAsync(
        ChargeHistoryFilter filter,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Compilar**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
```

Esperado: build limpo. Nada consome esses tipos ainda.

- [ ] **Step 5: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Domain/ChargeHistoryEntry.cs Evoque.Billing.Api/Domain/ChargeHistoryFilter.cs Evoque.Billing.Api/Repositories/IChargeHistoryRepository.cs
git commit -m "Add a read model for what this system issued

A history row spans four aggregates and has no behaviour of its own, so it
does not belong on IChargeBatchRepository, which answers one competency at
a time because that is what issuing a batch needs.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 2: A consulta em memória e o serviço

**Files:**
- Create: `Evoque.Billing.Api/Repositories/InMemoryChargeHistoryRepository.cs`
- Create: `Evoque.Billing.Api/Services/ChargeHistoryService.cs`
- Create: `Evoque.Billing.Api/Contracts/ChargeHistoryContracts.cs`
- Test: `Evoque.Billing.Api.Tests/ChargeHistoryServiceTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

Crie `Evoque.Billing.Api.Tests/ChargeHistoryServiceTests.cs`:

```csharp
using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class ChargeHistoryServiceTests
{
    private const string OperatorId = "maria";
    private const string FarmavaTaxId = "02346076000107";
    private const string OpenSportsTaxId = "56087276000103";

    [Fact]
    public async Task ListAsync_ReturnsTheMostRecentFirstAcrossCompetencies()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(new ChargeHistoryQuery(), CancellationToken.None);

        Assert.Equal(2, historico.Count);
        Assert.Equal("Open Sports", historico.First().CompanyName);
        Assert.Equal("Farmava", historico.Last().CompanyName);
    }

    [Fact]
    public async Task ListAsync_FindsACompanyByName()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "farmava"),
            CancellationToken.None);

        Assert.Equal("Farmava", Assert.Single(historico).CompanyName);
    }

    [Fact]
    public async Task ListAsync_FindsACompanyByTaxId()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: FarmavaTaxId),
            CancellationToken.None);

        Assert.Equal("Farmava", Assert.Single(historico).CompanyName);
    }

    /// <summary>
    /// Boleto de teste ao lado de um real, sem distinção, é como se confunde os
    /// dois. O ambiente separa e é filtrável.
    /// </summary>
    [Fact]
    public async Task ListAsync_SeparatesTestFromRealCharges()
    {
        var scenario = await CreateScenarioAsync();

        var teste = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Environment: "Sandbox"),
            CancellationToken.None);
        var real = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Environment: "Production"),
            CancellationToken.None);

        Assert.Equal("Farmava", Assert.Single(teste).CompanyName);
        Assert.Equal("Open Sports", Assert.Single(real).CompanyName);
    }

    [Fact]
    public async Task ListAsync_ShowsTheInvoiceWhenThereIsOne()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "farmava"),
            CancellationToken.None);

        var linha = Assert.Single(historico);
        Assert.Equal("Authorized", linha.FiscalInvoiceStatus);
        Assert.Equal("https://asaas/nota.pdf", linha.FiscalInvoicePdfUrl);
    }

    [Fact]
    public async Task ListAsync_LeavesTheInvoiceEmptyWhenThereIsNone()
    {
        var scenario = await CreateScenarioAsync();

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "open"),
            CancellationToken.None);

        var linha = Assert.Single(historico);
        Assert.Null(linha.FiscalInvoiceStatus);
        Assert.Null(linha.FiscalInvoicePdfUrl);
    }

    /// <summary>
    /// Monta duas cobranças emitidas: a Farmava em Sandbox, com nota, e a Open
    /// Sports em Produção, sem nota, uma competência depois.
    /// </summary>
    private static async Task<TestScenario> CreateScenarioAsync()
    {
        var dataStore = new InMemoryBillingDataStore();
        var billingPeriodRepository = new InMemoryBillingPeriodRepository(dataStore);
        var billingDraftRepository = new InMemoryBillingDraftRepository(dataStore);
        var chargeBatchRepository = new InMemoryChargeBatchRepository(dataStore);
        var fiscalInvoiceRepository = new InMemoryFiscalInvoiceRepository(dataStore);

        var setembro = new BillingPeriod(new BillingPeriodReference(2026, 9), DateTimeOffset.UtcNow);
        var outubro = new BillingPeriod(new BillingPeriodReference(2026, 10), DateTimeOffset.UtcNow);
        await billingPeriodRepository.AddAsync(setembro, CancellationToken.None);
        await billingPeriodRepository.AddAsync(outubro, CancellationToken.None);

        var farmava = await CriarCobrancaAsync(
            dataStore, billingDraftRepository, chargeBatchRepository,
            setembro, FarmavaTaxId, "Farmava", 299.50m, AsaasEnvironment.Sandbox,
            new DateOnly(2026, 9, 28), "pay_farmava", new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero));

        await CriarCobrancaAsync(
            dataStore, billingDraftRepository, chargeBatchRepository,
            outubro, OpenSportsTaxId, "Open Sports", 2157.60m, AsaasEnvironment.Production,
            new DateOnly(2026, 10, 25), "pay_open", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var nota = new FiscalInvoice(
            farmava, setembro.Id, 1, AsaasEnvironment.Sandbox, "pay_farmava",
            299.50m, new DateOnly(2026, 9, 28), false,
            "Serviços prestados em 09/2026.", DateTimeOffset.UtcNow);
        nota.MarkScheduled("inv_farmava", DateTimeOffset.UtcNow);
        nota.ApplyAsaasStatus("AUTHORIZED", null, DateTimeOffset.UtcNow);
        nota.AttachDocuments("https://asaas/nota.pdf", "https://asaas/nota.xml", DateTimeOffset.UtcNow);
        await fiscalInvoiceRepository.AddAsync(nota, CancellationToken.None);

        return new TestScenario(
            new ChargeHistoryService(new InMemoryChargeHistoryRepository(dataStore)),
            dataStore);
    }

    private static async Task<Guid> CriarCobrancaAsync(
        InMemoryBillingDataStore dataStore,
        InMemoryBillingDraftRepository billingDraftRepository,
        InMemoryChargeBatchRepository chargeBatchRepository,
        BillingPeriod billingPeriod,
        string companyTaxId,
        string companyName,
        decimal amount,
        AsaasEnvironment asaasEnvironment,
        DateOnly dueDate,
        string asaasPaymentId,
        DateTimeOffset issuedAt)
    {
        var billingDraft = new BillingDraft(
            billingPeriod.Id, companyTaxId, companyName, companyTaxId, "cus_teste",
            [new BillingDraftItem("Plano corporativo", 1, amount, "m1")],
            issuedAt);
        billingDraft.Approve(OperatorId, issuedAt);
        await billingDraftRepository.AddAsync(billingDraft, CancellationToken.None);

        var chargeBatch = new ChargeBatch(
            billingPeriod.Id, dueDate, OperatorId, asaasEnvironment, null,
            [billingDraft.Id], issuedAt);
        chargeBatch.Approve(OperatorId, issuedAt);
        chargeBatch.StartProcessing(issuedAt);
        chargeBatch.GetItem(billingDraft.Id).MarkChargeCreated(
            asaasPaymentId, $"https://asaas/boleto/{asaasPaymentId}", true, issuedAt);
        chargeBatch.MarkCompleted(issuedAt);
        await chargeBatchRepository.AddAsync(chargeBatch, CancellationToken.None);

        return billingDraft.Id;
    }

    private sealed record TestScenario(
        ChargeHistoryService Service,
        InMemoryBillingDataStore DataStore);
}
```

Leia `InMemoryBillingPeriodRepository` e `BillingPeriod` antes de rodar: se o
construtor de `BillingPeriod` ou o método de adicionar tiverem assinatura
diferente da usada acima, ajuste para a real. O restante do teste não muda.

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~ChargeHistoryServiceTests"
```

Esperado: erro de compilação — `InMemoryChargeHistoryRepository`,
`ChargeHistoryService` e `ChargeHistoryQuery` não existem.

- [ ] **Step 3: Criar os DTOs**

`Evoque.Billing.Api/Contracts/ChargeHistoryContracts.cs`:

```csharp
using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Contracts;

/// <summary>Filtros aceitos por `GET /api/charge-history`.</summary>
public sealed record ChargeHistoryQuery(
    string? Search = null,
    string? Environment = null,
    int? Year = null,
    int? Month = null);

public sealed record ChargeHistoryEntryResponse(
    Guid ChargeBatchId,
    Guid BillingDraftId,
    int Year,
    int Month,
    string AsaasEnvironment,
    string CompanyName,
    string CompanyTaxId,
    string FormattedCompanyTaxId,
    decimal TotalAmount,
    int MemberCount,
    string DueDate,
    DateTimeOffset IssuedAt,
    string ItemStatus,
    string? AsaasPaymentId,
    string? BankSlipUrl,
    string? ItemErrorMessage,
    string? FiscalInvoiceStatus,
    string? FiscalInvoicePdfUrl,
    string? FiscalInvoiceErrorMessage)
{
    public static ChargeHistoryEntryResponse FromDomain(ChargeHistoryEntry entry)
    {
        return new ChargeHistoryEntryResponse(
            entry.ChargeBatchId,
            entry.BillingDraftId,
            entry.BillingPeriodReference.Year,
            entry.BillingPeriodReference.Month,
            entry.AsaasEnvironment.ToString(),
            entry.CompanyName,
            entry.CompanyTaxId,
            // Qualificado porque a propriedade `CompanyTaxId` deste record
            // esconde a classe estática de mesmo nome, e a chamada sem
            // qualificação não compila (CS0120).
            Domain.CompanyTaxId.Format(entry.CompanyTaxId),
            entry.TotalAmount,
            entry.MemberCount,
            entry.DueDate.ToString("yyyy-MM-dd"),
            entry.IssuedAt,
            entry.ItemStatus.ToString(),
            entry.AsaasPaymentId,
            entry.BankSlipUrl,
            entry.ItemErrorMessage,
            entry.FiscalInvoiceStatus?.ToString(),
            entry.FiscalInvoicePdfUrl,
            entry.FiscalInvoiceErrorMessage);
    }
}
```

- [ ] **Step 4: Criar o repositório em memória**

`Evoque.Billing.Api/Repositories/InMemoryChargeHistoryRepository.cs`:

```csharp
using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryChargeHistoryRepository(InMemoryBillingDataStore dataStore)
    : IChargeHistoryRepository
{
    public Task<IReadOnlyCollection<ChargeHistoryEntry>> ListAsync(
        ChargeHistoryFilter filter,
        CancellationToken cancellationToken)
    {
        var billingPeriodsById = dataStore.BillingPeriods.Values
            .ToDictionary(billingPeriod => billingPeriod.Id);

        var entries = new List<ChargeHistoryEntry>();
        foreach (var chargeBatch in dataStore.ChargeBatches.Values)
        {
            if (!billingPeriodsById.TryGetValue(chargeBatch.BillingPeriodId, out var billingPeriod))
            {
                continue;
            }

            foreach (var chargeBatchItem in chargeBatch.Items)
            {
                if (!dataStore.BillingDrafts.TryGetValue(chargeBatchItem.BillingDraftId, out var billingDraft))
                {
                    continue;
                }

                var fiscalInvoice = dataStore.FiscalInvoices.Values
                    .Where(invoice => invoice.BillingDraftId == billingDraft.Id)
                    .OrderByDescending(invoice => invoice.Sequence)
                    .FirstOrDefault();

                entries.Add(new ChargeHistoryEntry(
                    chargeBatch.Id,
                    billingDraft.Id,
                    billingPeriod.Reference,
                    chargeBatch.AsaasEnvironment,
                    billingDraft.CompanyName,
                    billingDraft.CompanyTaxId,
                    billingDraft.TotalAmount,
                    billingDraft.Items.Count,
                    chargeBatch.DueDate,
                    chargeBatch.CreatedAt,
                    chargeBatchItem.Status,
                    chargeBatchItem.AsaasPaymentId,
                    chargeBatchItem.BankSlipUrl,
                    chargeBatchItem.ErrorMessage,
                    fiscalInvoice?.Status,
                    fiscalInvoice?.PdfUrl,
                    fiscalInvoice?.ErrorMessage));
            }
        }

        return Task.FromResult<IReadOnlyCollection<ChargeHistoryEntry>>(
            Filtrar(entries, filter)
                .OrderByDescending(entry => entry.IssuedAt)
                .ToArray());
    }

    private static IEnumerable<ChargeHistoryEntry> Filtrar(
        IEnumerable<ChargeHistoryEntry> entries,
        ChargeHistoryFilter filter)
    {
        if (filter.AsaasEnvironment is not null)
        {
            entries = entries.Where(entry => entry.AsaasEnvironment == filter.AsaasEnvironment);
        }

        if (filter.BillingPeriodReference is not null)
        {
            entries = entries.Where(entry => entry.BillingPeriodReference == filter.BillingPeriodReference);
        }

        if (!string.IsNullOrWhiteSpace(filter.CompanySearch))
        {
            // Buscar por nome e por CNPJ ao mesmo tempo, com OR, transforma
            // "Farmava 2" também numa busca por "2" dentro do CNPJ — e "2"
            // aparece em quase todo CNPJ de 14 dígitos. A forma do que foi
            // digitado decide qual das duas buscas vale.
            if (LooksLikeTaxId(filter.CompanySearch))
            {
                var digitsOnly = new string(filter.CompanySearch.Where(char.IsAsciiDigit).ToArray());
                entries = entries.Where(entry =>
                    entry.CompanyTaxId.Contains(digitsOnly, StringComparison.Ordinal));
            }
            else
            {
                var searchTerm = TextNormalization.Normalize(filter.CompanySearch);
                entries = entries.Where(entry =>
                    TextNormalization.Normalize(entry.CompanyName)
                        .Contains(searchTerm, StringComparison.Ordinal));
            }
        }

        return entries;
    }

    /// <summary>
    /// Um termo só é busca por CNPJ quando não tem letra nenhuma: dígitos e a
    /// pontuação usual bastam. Assim "02.346.076/0001-07" e "02346076" procuram
    /// CNPJ, e "Farmava 2" procura nome.
    /// </summary>
    private static bool LooksLikeTaxId(string companySearch)
    {
        return companySearch.Any(char.IsAsciiDigit)
            && companySearch.All(character =>
                char.IsAsciiDigit(character) || character is '.' or '/' or '-' or ' ');
    }
}
```

`TextNormalization.Normalize` vive em `Domain/TextNormalization.cs` e remove
acento e caixa — é o que o catálogo usa para comparar nomes, e o que faz esta
busca casar com a collation `utf8mb4_0900_ai_ci` do MySQL. Não escreva um
segundo normalizador.

- [ ] **Step 5: Criar o serviço**

`Evoque.Billing.Api/Services/ChargeHistoryService.cs`:

```csharp
using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Responde o que este sistema emitiu. Somente leitura: nenhuma operação aqui
/// altera cobrança, prévia ou nota.
/// </summary>
public sealed class ChargeHistoryService(IChargeHistoryRepository chargeHistoryRepository)
{
    public async Task<IReadOnlyCollection<ChargeHistoryEntryResponse>> ListAsync(
        ChargeHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var filter = new ChargeHistoryFilter
        {
            CompanySearch = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            AsaasEnvironment = ParseAsaasEnvironment(query.Environment),
            BillingPeriodReference = ParseBillingPeriodReference(query.Year, query.Month),
        };

        var entries = await chargeHistoryRepository.ListAsync(filter, cancellationToken);
        return entries.Select(ChargeHistoryEntryResponse.FromDomain).ToArray();
    }

    private static AsaasEnvironment? ParseAsaasEnvironment(string? requestedEnvironment)
    {
        if (string.IsNullOrWhiteSpace(requestedEnvironment))
        {
            return null;
        }

        if (Enum.TryParse<AsaasEnvironment>(requestedEnvironment, true, out var asaasEnvironment))
        {
            return asaasEnvironment;
        }

        throw new ValidationException("O ambiente do filtro deve ser Sandbox ou Production.");
    }

    private static BillingPeriodReference? ParseBillingPeriodReference(int? year, int? month)
    {
        if (year is null && month is null)
        {
            return null;
        }

        if (year is null || month is null)
        {
            throw new ValidationException("Informe ano e mês juntos para filtrar por competência.");
        }

        return new BillingPeriodReference(year.Value, month.Value);
    }
}
```

- [ ] **Step 6: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: PASS, com os seis testes novos somados aos 153.

- [ ] **Step 7: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Repositories/InMemoryChargeHistoryRepository.cs Evoque.Billing.Api/Services/ChargeHistoryService.cs Evoque.Billing.Api/Contracts/ChargeHistoryContracts.cs Evoque.Billing.Api.Tests/ChargeHistoryServiceTests.cs
git commit -m "Answer what was issued, across competencies

Searching by company name or tax id, filtering by environment, newest
first. Test and real charges are separated because a test slip sitting
next to a real one with no distinction is how the two get confused.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 3: A consulta MySQL e o endpoint

**Files:**
- Create: `Evoque.Billing.Api/Repositories/MySqlChargeHistoryRepository.cs`
- Create: `Evoque.Billing.Api/Controllers/ChargeHistoryController.cs`
- Modify: `Evoque.Billing.Api/Program.cs`

- [ ] **Step 1: Criar o repositório MySQL**

`Evoque.Billing.Api/Repositories/MySqlChargeHistoryRepository.cs`:

```csharp
using System.Text;
using Evoque.Billing.Api.Domain;
using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

/// <summary>
/// Junta lote, item, prévia e nota numa consulta só. A nota entra pela mais
/// recente de cada prévia: uma reemissão cria a sequência seguinte, e é ela que
/// vale.
/// </summary>
public sealed class MySqlChargeHistoryRepository(MySqlConnectionFactory connectionFactory)
    : IChargeHistoryRepository
{
    private const string BaseQuery = """
        SELECT
            cb.id                AS charge_batch_id,
            bd.id                AS billing_draft_id,
            bp.reference_year    AS reference_year,
            bp.reference_month   AS reference_month,
            cb.asaas_environment AS asaas_environment,
            bd.company_name      AS company_name,
            bd.company_tax_id    AS company_tax_id,
            cb.due_date          AS due_date,
            cb.created_at        AS issued_at,
            cbi.status           AS item_status,
            cbi.asaas_payment_id AS asaas_payment_id,
            cbi.bank_slip_url    AS bank_slip_url,
            cbi.error_message    AS item_error_message,
            fi.status            AS invoice_status,
            fi.pdf_url           AS invoice_pdf_url,
            fi.error_message     AS invoice_error_message,
            (SELECT COALESCE(SUM(bdi.quantity * bdi.unit_amount), 0)
               FROM billing_draft_items bdi
              WHERE bdi.billing_draft_id = bd.id) AS total_amount,
            (SELECT COUNT(*)
               FROM billing_draft_items bdi
              WHERE bdi.billing_draft_id = bd.id) AS member_count
        FROM charge_batch_items cbi
        JOIN charge_batches cb ON cb.id = cbi.charge_batch_id
        JOIN billing_drafts bd ON bd.id = cbi.billing_draft_id
        JOIN billing_periods bp ON bp.id = cb.billing_period_id
        LEFT JOIN fiscal_invoices fi
               ON fi.billing_draft_id = bd.id
              AND fi.sequence_number = (
                    SELECT MAX(fi2.sequence_number)
                      FROM fiscal_invoices fi2
                     WHERE fi2.billing_draft_id = bd.id)
        """;

    public async Task<IReadOnlyCollection<ChargeHistoryEntry>> ListAsync(
        ChargeHistoryFilter filter,
        CancellationToken cancellationToken)
    {
        var commandText = new StringBuilder(BaseQuery);
        var conditions = new List<string>();

        if (filter.AsaasEnvironment is not null)
        {
            conditions.Add("cb.asaas_environment = @asaasEnvironment");
        }

        if (filter.BillingPeriodReference is not null)
        {
            conditions.Add("bp.reference_year = @referenceYear AND bp.reference_month = @referenceMonth");
        }

        // Nome e CNPJ são buscas mutuamente exclusivas, decididas pela forma do
        // que foi digitado. Um OR entre as duas faz "Farmava 2" virar também uma
        // busca por "2" dentro do CNPJ, que casa com quase toda empresa.
        var searchesByTaxId = LooksLikeTaxId(filter.CompanySearch);
        if (!string.IsNullOrWhiteSpace(filter.CompanySearch))
        {
            conditions.Add(searchesByTaxId
                ? "bd.company_tax_id LIKE @companyTaxIdSearch"
                : "bd.company_name LIKE @companySearch");
        }

        if (conditions.Count > 0)
        {
            commandText.Append("\nWHERE ").Append(string.Join("\n  AND ", conditions));
        }

        commandText.Append("\nORDER BY cb.created_at DESC;");

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText.ToString(), connection);

        if (filter.AsaasEnvironment is not null)
        {
            command.Parameters.AddWithValue("@asaasEnvironment", filter.AsaasEnvironment.ToString());
        }

        if (filter.BillingPeriodReference is not null)
        {
            command.Parameters.AddWithValue("@referenceYear", filter.BillingPeriodReference.Year);
            command.Parameters.AddWithValue("@referenceMonth", filter.BillingPeriodReference.Month);
        }

        if (!string.IsNullOrWhiteSpace(filter.CompanySearch))
        {
            if (searchesByTaxId)
            {
                var digitsOnly = new string(filter.CompanySearch.Where(char.IsAsciiDigit).ToArray());
                command.Parameters.AddWithValue("@companyTaxIdSearch", $"%{digitsOnly}%");
            }
            else
            {
                command.Parameters.AddWithValue("@companySearch", $"%{filter.CompanySearch.Trim()}%");
            }
        }

        var entries = new List<ChargeHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    private static ChargeHistoryEntry ReadEntry(MySqlDataReader reader)
    {
        var invoiceStatus = ReadNullableString(reader, "invoice_status");
        return new ChargeHistoryEntry(
            reader.GetGuid("charge_batch_id"),
            reader.GetGuid("billing_draft_id"),
            new BillingPeriodReference(reader.GetInt32("reference_year"), reader.GetInt32("reference_month")),
            Enum.Parse<AsaasEnvironment>(reader.GetString("asaas_environment")),
            reader.GetString("company_name"),
            reader.GetString("company_tax_id"),
            reader.GetDecimal("total_amount"),
            reader.GetInt32("member_count"),
            DateOnly.FromDateTime(reader.GetDateTime("due_date")),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime("issued_at"), DateTimeKind.Utc)),
            Enum.Parse<ChargeBatchItemStatus>(reader.GetString("item_status")),
            ReadNullableString(reader, "asaas_payment_id"),
            ReadNullableString(reader, "bank_slip_url"),
            ReadNullableString(reader, "item_error_message"),
            invoiceStatus is null ? null : Enum.Parse<FiscalInvoiceStatus>(invoiceStatus),
            ReadNullableString(reader, "invoice_pdf_url"),
            ReadNullableString(reader, "invoice_error_message"));
    }

    private static string? ReadNullableString(MySqlDataReader reader, string columnName)
    {
        var columnOrdinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(columnOrdinal) ? null : reader.GetString(columnOrdinal);
    }
}
```

**Não duplique `LooksLikeTaxId`.** A Task 2 já decidiu a regra de "isto é busca
por CNPJ ou por nome" na implementação em memória. As duas precisam concordar:
um usuário que digita a mesma coisa não pode ver resultados diferentes conforme
o repositório que responde. Leia o que a Task 2 deixou e reaproveite. Se ela
deixou o helper privado dentro de um repositório, promova-o a um lugar que as
duas enxerguem, de preferência o próprio `ChargeHistoryFilter`, que é de quem a
pergunta é.

- [ ] **Step 2: Criar o controller**

`Evoque.Billing.Api/Controllers/ChargeHistoryController.cs`:

```csharp
using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

/// <summary>
/// O que este sistema emitiu, em ordem cronológica. Não inclui cobranças
/// criadas diretamente no painel do Asaas: elas não têm competência nem prévia
/// deste lado.
/// </summary>
[ApiController]
[Route("api/charge-history")]
public sealed class ChargeHistoryController(ChargeHistoryService chargeHistoryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<ChargeHistoryEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<ChargeHistoryEntryResponse>>> ListAsync(
        [FromQuery] ChargeHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var historico = await chargeHistoryService.ListAsync(query, cancellationToken);
        return Ok(historico);
    }
}
```

- [ ] **Step 3: Registrar no container**

Em `Evoque.Billing.Api/Program.cs`:

No bloco **sem** connection string, depois de `IFiscalInvoiceRepository`:

```csharp
    builder.Services.AddScoped<IChargeHistoryRepository, InMemoryChargeHistoryRepository>();
```

No bloco **com** connection string, depois de `IFiscalInvoiceRepository`:

```csharp
    builder.Services.AddScoped<IChargeHistoryRepository, MySqlChargeHistoryRepository>();
```

E junto dos demais serviços, depois de `FiscalInvoiceService`:

```csharp
builder.Services.AddScoped<ChargeHistoryService>();
```

- [ ] **Step 4: Compilar e rodar a suíte**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: build limpo, suíte verde. As consultas MySQL não rodam nos testes; a
verificação aqui é leitura cuidadosa do SQL, conferindo cada alias contra
`ReadEntry`.

- [ ] **Step 5: Subir a aplicação e conferir o endpoint**

```bash
cd C:\prog\evoque\api\Evoque.Billing.Api
$env:ASPNETCORE_URLS = "http://localhost:5207"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile
```

Noutro terminal:

```bash
curl.exe -s "http://localhost:5207/api/charge-history"
```

Esperado: `200` com `[]`, porque o ambiente local usa repositórios em memória e
não tem dados. Um `500` aqui significa erro de registro no container.

Encerre a aplicação com `Ctrl+C`.

- [ ] **Step 6: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Repositories/MySqlChargeHistoryRepository.cs Evoque.Billing.Api/Controllers/ChargeHistoryController.cs Evoque.Billing.Api/Program.cs
git commit -m "Serve the issuance history over HTTP

One query joins batch, item, draft and invoice. The invoice comes from the
highest sequence for each draft, because a reissue creates the next one and
that is the one that counts.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 4: A tela

Ao fim desta task o operador já encontra o boleto, que é o problema que originou
a feature. As tasks seguintes acrescentam a situação de pagamento.

**Files:**
- Modify: `C:\prog\evoque\web\client\src\lib\api.ts`
- Modify: `C:\prog\evoque\web\client\src\app\page.tsx`

- [ ] **Step 1: Acrescentar o tipo e a chamada**

Em `src/lib/api.ts`, junto das demais interfaces:

```ts
export interface ChargeHistoryEntry {
  chargeBatchId: string;
  billingDraftId: string;
  year: number;
  month: number;
  asaasEnvironment: AsaasEnvironment;
  companyName: string;
  companyTaxId: string;
  formattedCompanyTaxId: string;
  totalAmount: number;
  memberCount: number;
  dueDate: string;
  issuedAt: string;
  itemStatus: string;
  asaasPaymentId: string | null;
  bankSlipUrl: string | null;
  itemErrorMessage: string | null;
  fiscalInvoiceStatus: string | null;
  fiscalInvoicePdfUrl: string | null;
  fiscalInvoiceErrorMessage: string | null;
}

export interface ChargeHistoryFilters {
  search?: string;
  environment?: AsaasEnvironment;
  year?: number;
  month?: number;
}
```

E, junto dos demais métodos de `api`:

```ts
  getChargeHistory: (filters: ChargeHistoryFilters = {}) =>
    request<ChargeHistoryEntry[]>(`/api/charge-history${toQueryString(filters)}`),
```

Se `toQueryString` não existir no arquivo, veja como `getCatalogCompanies` monta
a query de `CompanyFilters` e use o mesmo mecanismo.

- [ ] **Step 2: Acrescentar a página e o item da barra lateral**

Em `src/app/page.tsx`:

No tipo `Page`, acrescente `| "chargeHistory"`.

No array `items` de `Sidebar`, entre "Notas fiscais" e "Integrações":

```tsx
    { page: "chargeHistory", label: "Histórico", icon: ReceiptText },
```

Troque o ícone de "Histórico" para `CalendarDays` e deixe `ReceiptText` em
"Notas fiscais", para os dois não ficarem iguais.

No estado do componente principal, junto dos demais:

```tsx
  const [chargeHistory, setChargeHistory] = useState<ChargeHistoryEntry[]>([]);
  const [chargeHistorySearch, setChargeHistorySearch] = useState("");
  const [chargeHistoryEnvironment, setChargeHistoryEnvironment] = useState<AsaasEnvironment | "all">("all");
  const [isLoadingChargeHistory, setIsLoadingChargeHistory] = useState(false);
```

E a função que carrega, junto das demais:

```tsx
  async function refreshChargeHistory(
    search: string = chargeHistorySearch,
    environmentFilter: AsaasEnvironment | "all" = chargeHistoryEnvironment,
  ) {
    setIsLoadingChargeHistory(true);
    try {
      setChargeHistory(await api.getChargeHistory({
        search: search.trim() || undefined,
        environment: environmentFilter === "all" ? undefined : environmentFilter,
      }));
    } catch (error) {
      showError(error instanceof Error ? error.message : "Não foi possível consultar o histórico.");
    } finally {
      setIsLoadingChargeHistory(false);
    }
  }
```

No `useEffect` que roda na primeira carga, acrescente `void refreshChargeHistory();`.

E o render, junto das demais páginas:

```tsx
            {page === "chargeHistory" && (
              <ChargeHistoryPage
                entries={chargeHistory}
                environmentFilter={chargeHistoryEnvironment}
                isLoading={isLoadingChargeHistory}
                search={chargeHistorySearch}
                onFiltersChange={(search, environmentFilter) => {
                  setChargeHistorySearch(search);
                  setChargeHistoryEnvironment(environmentFilter);
                  void refreshChargeHistory(search, environmentFilter);
                }}
              />
            )}
```

- [ ] **Step 3: Criar o componente da tela**

Em `src/app/page.tsx`, junto dos demais componentes de página:

```tsx
function ChargeHistoryPage({ entries, environmentFilter, isLoading, search, onFiltersChange }: {
  entries: ChargeHistoryEntry[];
  environmentFilter: AsaasEnvironment | "all";
  isLoading: boolean;
  search: string;
  onFiltersChange: (search: string, environmentFilter: AsaasEnvironment | "all") => void;
}) {
  const environmentOptions: Array<{ key: AsaasEnvironment | "all"; label: string }> = [
    { key: "all", label: "Todos" },
    { key: "Sandbox", label: "Teste" },
    { key: "Production", label: "Real" },
  ];

  return <section>
    <PageHeading
      title="Histórico"
      description="Cobranças emitidas por este sistema, da mais recente para a mais antiga."
    />

    <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center">
      <div className="relative flex max-w-md flex-1 items-center">
        <Search className="absolute ml-3 text-slate-400" size={17} />
        <input
          className="field w-full pl-10"
          placeholder="Buscar por empresa ou CNPJ"
          value={search}
          onChange={(event) => onFiltersChange(event.target.value, environmentFilter)}
        />
      </div>
      <div className="flex gap-2">
        {environmentOptions.map((option) => (
          <button
            key={option.key}
            className={`badge ${environmentFilter === option.key ? "bg-charcoal text-white" : "bg-slate-100 text-slate-700"}`}
            onClick={() => onFiltersChange(search, option.key)}
          >
            {option.label}
          </button>
        ))}
      </div>
    </div>

    {/* O histórico começa quando o sistema passa a emitir. Dizer isso evita que
        uma lista curta pareça defeito para quem sabe que existem mais cobranças
        no painel do Asaas. */}
    <Callout tone="warning">
      Aqui aparecem apenas as cobranças emitidas por este sistema. As criadas
      diretamente no painel do Asaas continuam só lá.
    </Callout>

    <div className="panel overflow-hidden">
      <div className="overflow-x-auto">
        <table className="min-w-[980px] w-full text-left text-sm">
          <thead className="border-b border-slate-200 bg-slate-50 text-xs font-extrabold uppercase tracking-wide text-slate-500">
            <tr>
              <th className="px-5 py-3">Emissão</th>
              <th className="px-5 py-3">Empresa</th>
              <th className="px-5 py-3">Ambiente</th>
              <th className="px-5 py-3 text-right">Valor</th>
              <th className="px-5 py-3">Nota fiscal</th>
              <th className="px-5 py-3 text-right">Documentos</th>
            </tr>
          </thead>
          <tbody>
            {entries.map((entry) => (
              <tr key={`${entry.chargeBatchId}-${entry.billingDraftId}`} className="border-b border-slate-100 last:border-0">
                <td className="px-5 py-4">
                  <p className="font-bold">{date(entry.issuedAt.slice(0, 10))}</p>
                  <p className="mt-0.5 text-xs text-slate-500">
                    {monthLabel(entry.year, entry.month)} · vence {date(entry.dueDate)}
                  </p>
                </td>
                <td className="px-5 py-4">
                  <p className="font-bold">{entry.companyName}</p>
                  <p className="mt-0.5 text-xs text-slate-500">
                    {entry.formattedCompanyTaxId} · {entry.memberCount} pessoa(s)
                  </p>
                </td>
                <td className="px-5 py-4">
                  <span className={`badge ${entry.asaasEnvironment === "Production" ? "bg-red-50 text-red-700" : "bg-amber-50 text-amber-700"}`}>
                    {entry.asaasEnvironment === "Production" ? "Real" : "Teste"}
                  </span>
                </td>
                <td className="px-5 py-4 text-right font-extrabold">{money(entry.totalAmount)}</td>
                <td className="px-5 py-4">
                  {entry.fiscalInvoiceStatus ? (
                    <span className={`badge ${fiscalInvoiceBadge(entry.fiscalInvoiceStatus)}`}>
                      {fiscalInvoiceLabel(entry.fiscalInvoiceStatus)}
                    </span>
                  ) : (
                    <span className="text-xs text-slate-400">Não emitida</span>
                  )}
                </td>
                <td className="px-5 py-4">
                  <div className="flex justify-end gap-3">
                    {entry.bankSlipUrl && (
                      <a className="inline-flex items-center gap-1.5 text-sm font-extrabold text-orange hover:underline"
                        href={entry.bankSlipUrl} target="_blank" rel="noopener noreferrer">
                        <FileText size={15} />Boleto
                      </a>
                    )}
                    {entry.fiscalInvoicePdfUrl && (
                      <a className="inline-flex items-center gap-1.5 text-sm font-extrabold text-orange hover:underline"
                        href={entry.fiscalInvoicePdfUrl} target="_blank" rel="noopener noreferrer">
                        <ReceiptText size={15} />Nota
                      </a>
                    )}
                    {!entry.bankSlipUrl && !entry.fiscalInvoicePdfUrl && (
                      <span className="text-xs text-slate-400">{entry.itemErrorMessage ? "Falhou" : "Sem documento"}</span>
                    )}
                  </div>
                </td>
              </tr>
            ))}
            {entries.length === 0 && (
              <EmptyTable
                colSpan={6}
                message={isLoading
                  ? "Consultando..."
                  : "Nenhuma cobrança emitida por este sistema ainda. Execute um lote para que ela apareça aqui."}
              />
            )}
          </tbody>
        </table>
      </div>
    </div>
  </section>;
}
```

`fiscalInvoiceBadge`, `fiscalInvoiceLabel`, `date`, `money`, `monthLabel`,
`PageHeading`, `EmptyTable`, `Callout`, `Search`, `FileText` e `ReceiptText` já
existem no arquivo.

- [ ] **Step 4: Compilar**

```bash
cd C:\prog\evoque\web\client
npm run build
```

Esperado: `Compiled successfully`.

- [ ] **Step 5: Commit**

```bash
cd C:\prog\evoque\web\client
git add src/lib/api.ts src/app/page.tsx
git commit -m "Show what was issued, in one place

Finding a slip meant knowing its competency and expanding a collapsed
block. The history lists every charge this system issued, newest first,
searchable by company, with test and real told apart at a glance.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 5: A situação de pagamento no domínio

**Files:**
- Create: `Evoque.Billing.Api/Domain/ChargePaymentStatus.cs`
- Modify: `Evoque.Billing.Api/Domain/ChargeBatchItem.cs`
- Test: `Evoque.Billing.Api.Tests/ChargeBatchItemTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

Crie `Evoque.Billing.Api.Tests/ChargeBatchItemTests.cs`:

```csharp
using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Tests;

public sealed class ChargeBatchItemTests
{
    private static readonly Guid BillingDraftId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewItem_HasNoPaymentStatusYet()
    {
        var chargeBatchItem = new ChargeBatchItem(BillingDraftId, CreatedAt);

        Assert.Equal(ChargePaymentStatus.Unknown, chargeBatchItem.PaymentStatus);
        Assert.Null(chargeBatchItem.PaidAt);
        Assert.False(chargeBatchItem.IsPaymentSettled);
    }

    [Theory]
    [InlineData("PENDING", ChargePaymentStatus.Pending)]
    [InlineData("RECEIVED", ChargePaymentStatus.Received)]
    [InlineData("CONFIRMED", ChargePaymentStatus.Confirmed)]
    [InlineData("OVERDUE", ChargePaymentStatus.Overdue)]
    [InlineData("REFUNDED", ChargePaymentStatus.Refunded)]
    public void ApplyPaymentStatus_MapsWhatTheAccountReturns(
        string asaasStatus,
        ChargePaymentStatus expected)
    {
        var chargeBatchItem = new ChargeBatchItem(BillingDraftId, CreatedAt);

        chargeBatchItem.ApplyPaymentStatus(asaasStatus, null, CreatedAt.AddDays(1));

        Assert.Equal(expected, chargeBatchItem.PaymentStatus);
    }

    /// <summary>
    /// Um status novo do Asaas não pode derrubar a sincronização das demais
    /// cobranças, como já vale para a nota fiscal.
    /// </summary>
    [Fact]
    public void ApplyPaymentStatus_IgnoresAnUnknownStatus()
    {
        var chargeBatchItem = new ChargeBatchItem(BillingDraftId, CreatedAt);
        chargeBatchItem.ApplyPaymentStatus("RECEIVED", new DateOnly(2026, 10, 2), CreatedAt.AddDays(1));

        chargeBatchItem.ApplyPaymentStatus("ALGO_QUE_AINDA_NAO_EXISTE", null, CreatedAt.AddDays(2));

        Assert.Equal(ChargePaymentStatus.Received, chargeBatchItem.PaymentStatus);
    }

    [Fact]
    public void ApplyPaymentStatus_KeepsTheDateItWasPaid()
    {
        var chargeBatchItem = new ChargeBatchItem(BillingDraftId, CreatedAt);

        chargeBatchItem.ApplyPaymentStatus("RECEIVED", new DateOnly(2026, 10, 2), CreatedAt.AddDays(1));

        Assert.Equal(new DateOnly(2026, 10, 2), chargeBatchItem.PaidAt);
        Assert.True(chargeBatchItem.IsPaymentSettled);
    }

    /// <summary>
    /// Uma cobrança paga não muda mais, e a sincronização usa isso para não
    /// consultar de novo o que já está resolvido.
    /// </summary>
    [Theory]
    [InlineData("PENDING", false)]
    [InlineData("OVERDUE", false)]
    [InlineData("RECEIVED", true)]
    [InlineData("CONFIRMED", true)]
    [InlineData("REFUNDED", true)]
    public void IsPaymentSettled_IsTrueOnlyForFinalStates(string asaasStatus, bool expected)
    {
        var chargeBatchItem = new ChargeBatchItem(BillingDraftId, CreatedAt);

        chargeBatchItem.ApplyPaymentStatus(asaasStatus, null, CreatedAt.AddDays(1));

        Assert.Equal(expected, chargeBatchItem.IsPaymentSettled);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~ChargeBatchItemTests"
```

Esperado: erro de compilação — `ChargePaymentStatus`, `PaymentStatus`, `PaidAt`,
`IsPaymentSettled` e `ApplyPaymentStatus` não existem.

- [ ] **Step 3: Criar o enum**

`Evoque.Billing.Api/Domain/ChargePaymentStatus.cs`:

```csharp
namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Situação de pagamento da cobrança no Asaas. `Unknown` é o estado de uma
/// cobrança que ainda não foi consultada — diferente de `Pending`, que é uma
/// resposta do Asaas.
/// </summary>
public enum ChargePaymentStatus
{
    Unknown,
    Pending,
    Received,
    Confirmed,
    Overdue,
    Refunded,
}
```

- [ ] **Step 4: Acrescentar ao item do lote**

Em `Evoque.Billing.Api/Domain/ChargeBatchItem.cs`:

Acrescente ao construtor privado, depois de `errorMessage`:

```csharp
        ChargePaymentStatus paymentStatus,
        DateOnly? paidAt,
```

No corpo, junto das atribuições:

```csharp
        PaymentStatus = paymentStatus;
        PaidAt = paidAt;
```

No construtor público, que delega, passe `ChargePaymentStatus.Unknown, null` na
posição correspondente.

Em `Restore`, acrescente os mesmos dois parâmetros na mesma posição e repasse.

As propriedades, depois de `ErrorMessage`:

```csharp
    /// <summary>
    /// Situação do boleto no Asaas, atualizada por consulta explícita. O produto
    /// cria a cobrança e não acompanha o pagamento sozinho.
    /// </summary>
    public ChargePaymentStatus PaymentStatus { get; private set; }

    public DateOnly? PaidAt { get; private set; }

    /// <summary>Uma cobrança nestes estados não muda mais e não precisa ser consultada de novo.</summary>
    public bool IsPaymentSettled => PaymentStatus is ChargePaymentStatus.Received
        or ChargePaymentStatus.Confirmed
        or ChargePaymentStatus.Refunded;
```

E o método, depois de `MarkFailed`:

```csharp
    /// <summary>
    /// Aplica a situação devolvida pelo Asaas. Um status ainda desconhecido é
    /// ignorado de propósito: sincronizar o histórico inteiro não pode parar
    /// porque o Asaas passou a devolver um estado novo.
    /// </summary>
    public void ApplyPaymentStatus(string asaasStatus, DateOnly? paidAt, DateTimeOffset updatedAt)
    {
        var mappedStatus = MapPaymentStatus(asaasStatus);
        if (mappedStatus is null)
        {
            return;
        }

        PaymentStatus = mappedStatus.Value;
        PaidAt = paidAt ?? PaidAt;
        UpdatedAt = updatedAt;
    }

    private static ChargePaymentStatus? MapPaymentStatus(string asaasStatus)
    {
        return asaasStatus switch
        {
            "PENDING" or "AWAITING_RISK_ANALYSIS" => ChargePaymentStatus.Pending,
            "RECEIVED" or "RECEIVED_IN_CASH" => ChargePaymentStatus.Received,
            "CONFIRMED" => ChargePaymentStatus.Confirmed,
            "OVERDUE" => ChargePaymentStatus.Overdue,
            "REFUNDED" or "REFUND_REQUESTED" => ChargePaymentStatus.Refunded,
            _ => null,
        };
    }
```

- [ ] **Step 5: Corrigir o chamador de `Restore`**

`MySqlChargeBatchRepository.ListItemsAsync` chama `ChargeBatchItem.Restore` e
para de compilar com os parâmetros novos. Passe `ChargePaymentStatus.Unknown,
null` ali, **e deixe assim no commit**.

Não é gambiarra: até a Task 6 criar as colunas, `Unknown` é exatamente o que o
banco sabe sobre essas cobranças — ninguém consultou nenhuma. Um comentário de
uma linha aponta que a Task 6 passa a ler as colunas de verdade.

A alternativa que este plano trazia antes — mexer e reverter antes de commitar —
produziria um commit que não compila. Isso quebra `git bisect` e qualquer CI que
rode naquele ponto, para economizar uma linha que de todo jeito precisa existir.

- [ ] **Step 6: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~ChargeBatchItemTests"
```

Esperado: PASS, com os 13 casos desta classe.

- [ ] **Step 7: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Domain/ChargePaymentStatus.cs Evoque.Billing.Api/Domain/ChargeBatchItem.cs Evoque.Billing.Api.Tests/ChargeBatchItemTests.cs
git commit -m "Let a batch item carry whether the slip was paid

The product creates a charge and never looks again, so the history could
say what went out but not what came back. Unknown is kept distinct from
Pending: one means nobody asked, the other is an answer from Asaas.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 6: Persistência e migration 013

A migration `003` criou `charge_batch_items` e **não é editada**: ela já rodou em
produção.

**Files:**
- Modify: `Evoque.Billing.Api/Repositories/MySqlChargeBatchRepository.cs`
- Modify: `Evoque.Billing.Api/Repositories/DatabaseSchemaInitializer.cs`
- Modify: `Evoque.Billing.Api/Repositories/MySqlChargeHistoryRepository.cs`

- [ ] **Step 1: Persistir os campos novos**

Em `MySqlChargeBatchRepository.InsertOrUpdateItemAsync`, acrescente
`payment_status, paid_at` à lista de colunas e `@paymentStatus, @paidAt` à de
valores, nas mesmas posições, e ao bloco `ON DUPLICATE KEY UPDATE`:

```sql
                payment_status = VALUES(payment_status),
                paid_at = VALUES(paid_at),
```

Os parâmetros, junto dos demais:

```csharp
        command.Parameters.AddWithValue("@paymentStatus", chargeBatchItem.PaymentStatus.ToString());
        command.Parameters.AddWithValue(
            "@paidAt",
            chargeBatchItem.PaidAt.HasValue
                ? chargeBatchItem.PaidAt.Value.ToDateTime(TimeOnly.MinValue)
                : (object)DBNull.Value);
```

Em `ListItemsAsync`, acrescente `payment_status, paid_at` ao `SELECT` e, na
chamada a `ChargeBatchItem.Restore`, depois de `error_message`:

```csharp
                Enum.Parse<ChargePaymentStatus>(reader.GetString("payment_status")),
                reader.IsDBNull(reader.GetOrdinal("paid_at"))
                    ? null
                    : DateOnly.FromDateTime(reader.GetDateTime("paid_at")),
```

A ordem precisa casar com a assinatura de `Restore`.

- [ ] **Step 2: Acrescentar a migration**

Em `DatabaseSchemaInitializer.cs`, junto dos demais identificadores:

```csharp
    private const string ChargePaymentStatusMigrationId = "013_add_charge_payment_status";
```

Em `ApplyLatestMigrationsAsync`, ao final:

```csharp
        await AddChargePaymentStatusAsync(connection, cancellationToken);
```

E o método:

```csharp
    /// <summary>
    /// Situação de pagamento do boleto, para o histórico responder o que foi
    /// pago. `Unknown` é o padrão das cobranças já existentes: elas nunca foram
    /// consultadas.
    /// </summary>
    private static async Task AddChargePaymentStatusAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (await IsAppliedAsync(connection, ChargePaymentStatusMigrationId, cancellationToken))
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await ColumnExistsAsync(connection, transaction, "charge_batch_items", "payment_status", cancellationToken))
        {
            await ExecuteAsync(connection, """
                ALTER TABLE charge_batch_items
                ADD COLUMN payment_status VARCHAR(32) NOT NULL DEFAULT 'Unknown' AFTER error_message,
                ADD COLUMN paid_at DATE NULL AFTER payment_status;
                """, transaction, cancellationToken);
        }

        await InsertMigrationAsync(
            connection,
            transaction,
            ChargePaymentStatusMigrationId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
```

- [ ] **Step 3: Levar a situação ao histórico**

Em `MySqlChargeHistoryRepository`, acrescente ao `SELECT`:

```sql
            cbi.payment_status   AS payment_status,
            cbi.paid_at          AS paid_at,
```

E em `ReadEntry`, os dois campos na posição correspondente do
`ChargeHistoryEntry` — que a Task 7 acrescenta ao record. Faça os dois juntos.

- [ ] **Step 4: Compilar e rodar a suíte**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: build limpo, suíte verde.

- [ ] **Step 5: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Repositories/MySqlChargeBatchRepository.cs Evoque.Billing.Api/Repositories/DatabaseSchemaInitializer.cs Evoque.Billing.Api/Repositories/MySqlChargeHistoryRepository.cs
git commit -m "Persist whether the slip was paid

Migration 013 adds payment_status and paid_at. Migration 003 is left as
production applied it. Existing charges default to Unknown, which is
accurate: nobody has asked about them yet.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 7: Levar a situação até a tela

**Files:**
- Modify: `Evoque.Billing.Api/Domain/ChargeHistoryEntry.cs`
- Modify: `Evoque.Billing.Api/Repositories/InMemoryChargeHistoryRepository.cs`
- Modify: `Evoque.Billing.Api/Contracts/ChargeHistoryContracts.cs`
- Test: `Evoque.Billing.Api.Tests/ChargeHistoryServiceTests.cs`

- [ ] **Step 1: Escrever o teste que falha**

Acrescente em `ChargeHistoryServiceTests.cs`:

```csharp
    [Fact]
    public async Task ListAsync_ShowsWhetherTheSlipWasPaid()
    {
        var scenario = await CreateScenarioAsync();
        var chargeBatch = scenario.DataStore.ChargeBatches.Values
            .Single(batch => batch.AsaasEnvironment == AsaasEnvironment.Sandbox);
        chargeBatch.Items.Single().ApplyPaymentStatus(
            "RECEIVED",
            new DateOnly(2026, 10, 2),
            DateTimeOffset.UtcNow);

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(Search: "farmava"),
            CancellationToken.None);

        var linha = Assert.Single(historico);
        Assert.Equal("Received", linha.PaymentStatus);
        Assert.Equal("2026-10-02", linha.PaidAt);
    }
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~ChargeHistoryServiceTests"
```

Esperado: erro de compilação — `PaymentStatus` e `PaidAt` não existem na
resposta.

- [ ] **Step 3: Acrescentar ao modelo e ao contrato**

Em `ChargeHistoryEntry`, acrescente depois de `ItemErrorMessage`:

```csharp
    ChargePaymentStatus PaymentStatus,
    DateOnly? PaidAt,
```

Em `InMemoryChargeHistoryRepository`, na construção da entrada, na mesma
posição:

```csharp
                    chargeBatchItem.PaymentStatus,
                    chargeBatchItem.PaidAt,
```

Em `ChargeHistoryEntryResponse`, depois de `ItemErrorMessage`:

```csharp
    string PaymentStatus,
    string? PaidAt,
```

E em `FromDomain`, na mesma posição:

```csharp
            entry.PaymentStatus.ToString(),
            entry.PaidAt?.ToString("yyyy-MM-dd"),
```

- [ ] **Step 4: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: PASS.

- [ ] **Step 5: Mostrar na tela**

Em `C:\prog\evoque\web\client\src\lib\api.ts`, acrescente a `ChargeHistoryEntry`:

```ts
  paymentStatus: string;
  paidAt: string | null;
```

Em `src/app/page.tsx`, junto dos demais rótulos:

```tsx
const chargePaymentLabels: Record<string, string> = {
  Unknown: "Não consultado",
  Pending: "Em aberto",
  Received: "Pago",
  Confirmed: "Pago",
  Overdue: "Vencido",
  Refunded: "Estornado",
};

function chargePaymentBadge(paymentStatus: string): string {
  if (paymentStatus === "Received" || paymentStatus === "Confirmed") return "bg-emerald-50 text-emerald-700";
  if (paymentStatus === "Overdue") return "bg-red-50 text-red-700";
  if (paymentStatus === "Refunded") return "bg-slate-100 text-slate-600";
  return "bg-amber-50 text-amber-700";
}
```

Em `ChargeHistoryPage`, acrescente a coluna no cabeçalho, entre "Valor" e "Nota
fiscal":

```tsx
              <th className="px-5 py-3">Boleto</th>
```

E a célula, na mesma posição da linha:

```tsx
                <td className="px-5 py-4">
                  <span className={`badge ${chargePaymentBadge(entry.paymentStatus)}`}>
                    {chargePaymentLabels[entry.paymentStatus] ?? entry.paymentStatus}
                  </span>
                  {entry.paidAt && <p className="mt-1 text-xs text-slate-500">em {date(entry.paidAt)}</p>}
                </td>
```

Ajuste o `colSpan` do `EmptyTable` de 6 para 7.

- [ ] **Step 6: Compilar os dois**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
cd C:\prog\evoque\web\client
npm run build
```

Esperado: build limpo e `Compiled successfully`.

- [ ] **Step 7: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Domain/ChargeHistoryEntry.cs Evoque.Billing.Api/Repositories/InMemoryChargeHistoryRepository.cs Evoque.Billing.Api/Contracts/ChargeHistoryContracts.cs Evoque.Billing.Api.Tests/ChargeHistoryServiceTests.cs
git commit -m "Carry the payment status through to the history

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"

cd C:\prog\evoque\web\client
git add src/lib/api.ts src/app/page.tsx
git commit -m "Show whether each slip was paid

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 8: Consultar o pagamento no Asaas

**Files:**
- Modify: `Evoque.Billing.Api/Integrations/Asaas/IAsaasChargeGateway.cs`
- Modify: `Evoque.Billing.Api/Integrations/Asaas/AsaasChargeGateway.cs`
- Modify: `Evoque.Billing.Api.Tests/BillingWorkflowTests.cs`
- Modify: `Evoque.Billing.Api.Tests/FiscalInvoiceServiceTests.cs`

Gateways HTTP não têm teste unitário neste repositório; a cobertura fica na
política e nos serviços. A verificação aqui é build limpo.

- [ ] **Step 1: Acrescentar a leitura ao contrato**

Em `IAsaasChargeGateway.cs`:

```csharp
    Task<AsaasChargeState> GetChargeAsync(
        AsaasEnvironment asaasEnvironment,
        string asaasPaymentId,
        CancellationToken cancellationToken);
```

E o record, junto dos demais:

```csharp
public sealed record AsaasChargeState(string PaymentId, string Status, DateOnly? PaymentDate);
```

- [ ] **Step 2: Implementar**

Em `AsaasChargeGateway.cs`, acrescente o método depois de `CreateChargeAsync`:

```csharp
    public async Task<AsaasChargeState> GetChargeAsync(
        AsaasEnvironment asaasEnvironment,
        string asaasPaymentId,
        CancellationToken cancellationToken)
    {
        var connectionOptions = asaasOptions.Value.GetConnection(asaasEnvironment);
        AsaasOperationPolicy.ValidateReadOperation(hostEnvironment, asaasEnvironment, connectionOptions);
        AsaasOperationPolicy.ConfigureHttpClient(httpClient, connectionOptions);

        using var response = await httpClient.GetAsync(
            $"payments/{Uri.EscapeDataString(asaasPaymentId)}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var failureReason = await AsaasErrorMessage.ReadAsync(response, cancellationToken);
            throw new ExternalOperationNotAllowedException(
                $"Não foi possível consultar a cobrança no Asaas: {failureReason}");
        }

        var responseData = await response.Content.ReadFromJsonAsync<AsaasPaymentStateResponse>(
            cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(responseData?.Id) || string.IsNullOrWhiteSpace(responseData.Status))
        {
            throw new ExternalOperationNotAllowedException(
                "O Asaas retornou uma resposta inválida ao consultar a cobrança.");
        }

        return new AsaasChargeState(
            responseData.Id,
            responseData.Status,
            DateOnly.TryParse(responseData.PaymentDate, out var paymentDate) ? paymentDate : null);
    }
```

E o record privado, junto de `AsaasPaymentResponse`:

```csharp
    private sealed record AsaasPaymentStateResponse(string? Id, string? Status, string? PaymentDate);
```

- [ ] **Step 3: Ajustar os gateways falsos**

`RecordingAsaasChargeGateway` em `BillingWorkflowTests.cs` e
`StubAsaasChargeGateway` em `FiscalInvoiceServiceTests.cs` implementam
`IAsaasChargeGateway` e vão quebrar. Acrescente em cada um:

```csharp
        public Task<AsaasChargeState> GetChargeAsync(
            AsaasEnvironment asaasEnvironment,
            string asaasPaymentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsaasChargeState(asaasPaymentId, "PENDING", null));
        }
```

- [ ] **Step 4: Compilar e rodar a suíte**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: build limpo, suíte verde.

- [ ] **Step 5: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Integrations/Asaas/IAsaasChargeGateway.cs Evoque.Billing.Api/Integrations/Asaas/AsaasChargeGateway.cs Evoque.Billing.Api.Tests/BillingWorkflowTests.cs Evoque.Billing.Api.Tests/FiscalInvoiceServiceTests.cs
git commit -m "Let the charge gateway read, not only create

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

### Task 9: Sincronizar o pagamento

**Files:**
- Create: `Evoque.Billing.Api/Services/ChargePaymentSynchronizationService.cs`
- Modify: `Evoque.Billing.Api/Controllers/ChargeHistoryController.cs`
- Modify: `Evoque.Billing.Api/Program.cs`
- Modify: `C:\prog\evoque\web\client\src\lib\api.ts`
- Modify: `C:\prog\evoque\web\client\src\app\page.tsx`
- Test: `Evoque.Billing.Api.Tests/ChargePaymentSynchronizationServiceTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

Crie `Evoque.Billing.Api.Tests/ChargePaymentSynchronizationServiceTests.cs`:

```csharp
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class ChargePaymentSynchronizationServiceTests
{
    private const string OperatorId = "maria";

    [Fact]
    public async Task SynchronizeAsync_StoresWhatAsaasReturned()
    {
        var scenario = CreateScenario("RECEIVED", new DateOnly(2026, 10, 2));

        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);

        var item = scenario.DataStore.ChargeBatches.Values.Single().Items.Single();
        Assert.Equal(ChargePaymentStatus.Received, item.PaymentStatus);
        Assert.Equal(new DateOnly(2026, 10, 2), item.PaidAt);
    }

    /// <summary>
    /// Uma cobrança paga não muda mais. Consultá-la de novo só gasta chamada.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_DoesNotQueryAnAlreadyPaidCharge()
    {
        var scenario = CreateScenario("RECEIVED", new DateOnly(2026, 10, 2));
        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);
        var chamadasAposPrimeira = scenario.Gateway.GetCallCount;

        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);

        Assert.Equal(chamadasAposPrimeira, scenario.Gateway.GetCallCount);
    }

    /// <summary>
    /// Uma indisponibilidade externa não pode apagar o que já sabemos nem
    /// interromper a consulta das demais cobranças.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_KeepsWhatIsKnownWhenTheQueryFails()
    {
        var scenario = CreateScenario("RECEIVED", new DateOnly(2026, 10, 2));
        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);
        scenario.Gateway.FailEveryQuery();

        var item = scenario.DataStore.ChargeBatches.Values.Single().Items.Single();
        item.ApplyPaymentStatus("PENDING", null, DateTimeOffset.UtcNow);
        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);

        Assert.Equal(ChargePaymentStatus.Pending, item.PaymentStatus);
    }

    [Fact]
    public async Task SynchronizeAsync_SkipsAnItemWithoutACharge()
    {
        var scenario = CreateScenario("RECEIVED", null, markChargeCreated: false);

        await scenario.Service.SynchronizeAsync(OperatorId, CancellationToken.None);

        Assert.Equal(0, scenario.Gateway.GetCallCount);
    }

    private static TestScenario CreateScenario(
        string asaasStatus,
        DateOnly? paymentDate,
        bool markChargeCreated = true)
    {
        var dataStore = new InMemoryBillingDataStore();
        var chargeBatchRepository = new InMemoryChargeBatchRepository(dataStore);
        var gateway = new RecordingAsaasChargeGateway(asaasStatus, paymentDate);

        var billingPeriodId = Guid.NewGuid();
        var billingDraftId = Guid.NewGuid();
        var criadoEm = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        var chargeBatch = new ChargeBatch(
            billingPeriodId, new DateOnly(2026, 9, 28), OperatorId,
            AsaasEnvironment.Sandbox, null, [billingDraftId], criadoEm);
        chargeBatch.Approve(OperatorId, criadoEm);
        chargeBatch.StartProcessing(criadoEm);
        if (markChargeCreated)
        {
            chargeBatch.GetItem(billingDraftId).MarkChargeCreated("pay_teste", "https://asaas/boleto", true, criadoEm);
        }
        else
        {
            chargeBatch.GetItem(billingDraftId).MarkFailed("falhou", criadoEm);
        }

        chargeBatch.MarkCompleted(criadoEm);
        dataStore.ChargeBatches[chargeBatch.Id] = chargeBatch;

        return new TestScenario(
            new ChargePaymentSynchronizationService(
                chargeBatchRepository,
                new InMemoryChargeHistoryRepository(dataStore),
                gateway,
                new InMemoryAuditLogRepository(dataStore)),
            gateway,
            dataStore);
    }

    private sealed record TestScenario(
        ChargePaymentSynchronizationService Service,
        RecordingAsaasChargeGateway Gateway,
        InMemoryBillingDataStore DataStore);

    private sealed class RecordingAsaasChargeGateway(string asaasStatus, DateOnly? paymentDate)
        : IAsaasChargeGateway
    {
        private bool falhaEmTodasAsConsultas;

        public int GetCallCount { get; private set; }

        public void FailEveryQuery() => falhaEmTodasAsConsultas = true;

        public Task<AsaasChargeCreation> CreateChargeAsync(
            AsaasEnvironment asaasEnvironment,
            AsaasChargeRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsaasChargeCreation("pay_teste", "https://asaas/boleto"));
        }

        public Task<AsaasChargeState> GetChargeAsync(
            AsaasEnvironment asaasEnvironment,
            string asaasPaymentId,
            CancellationToken cancellationToken)
        {
            GetCallCount++;
            if (falhaEmTodasAsConsultas)
            {
                throw new ExternalOperationNotAllowedException("Falha simulada do Asaas.");
            }

            return Task.FromResult(new AsaasChargeState(asaasPaymentId, asaasStatus, paymentDate));
        }
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~ChargePaymentSynchronizationServiceTests"
```

Esperado: erro de compilação — `ChargePaymentSynchronizationService` não existe.

- [ ] **Step 3: Criar o serviço**

`Evoque.Billing.Api/Services/ChargePaymentSynchronizationService.cs`:

```csharp
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Integrations.Asaas;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Atualiza junto ao Asaas a situação das cobranças ainda em aberto. Acionado
/// pela tela: o produto cria a cobrança e não acompanha o pagamento sozinho.
/// </summary>
public sealed class ChargePaymentSynchronizationService(
    IChargeBatchRepository chargeBatchRepository,
    IChargeHistoryRepository chargeHistoryRepository,
    IAsaasChargeGateway asaasChargeGateway,
    IAuditLogRepository auditLogRepository)
{
    public async Task SynchronizeAsync(string operatorId, CancellationToken cancellationToken)
    {
        var entries = await chargeHistoryRepository.ListAsync(new ChargeHistoryFilter(), cancellationToken);

        // Uma consulta por cobrança, em série, como ChargeBatchService já faz por
        // item de lote. Aceitável no volume atual; uma resposta lenta do Asaas
        // numa cobrança atrasa as demais desta mesma chamada.
        foreach (var chargeBatchId in entries.Select(entry => entry.ChargeBatchId).Distinct())
        {
            var chargeBatch = await chargeBatchRepository.FindByIdAsync(chargeBatchId, cancellationToken);
            if (chargeBatch is null)
            {
                continue;
            }

            var mudouAlgumItem = false;
            foreach (var chargeBatchItem in chargeBatch.Items)
            {
                if (string.IsNullOrWhiteSpace(chargeBatchItem.AsaasPaymentId)
                    || chargeBatchItem.IsPaymentSettled)
                {
                    continue;
                }

                try
                {
                    var chargeState = await asaasChargeGateway.GetChargeAsync(
                        chargeBatch.AsaasEnvironment,
                        chargeBatchItem.AsaasPaymentId,
                        cancellationToken);
                    var situacaoAnterior = chargeBatchItem.PaymentStatus;
                    chargeBatchItem.ApplyPaymentStatus(
                        chargeState.Status,
                        chargeState.PaymentDate,
                        DateTimeOffset.UtcNow);
                    if (chargeBatchItem.PaymentStatus != situacaoAnterior)
                    {
                        mudouAlgumItem = true;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Uma indisponibilidade externa não apaga o que já sabemos
                    // nem interrompe a consulta das demais cobranças.
                    await auditLogRepository.AddAsync(
                        AuditLog.Create(
                            "charge-payment.query-failed",
                            operatorId,
                            DateTimeOffset.UtcNow,
                            chargeBatch.BillingPeriodId,
                            chargeBatchItem.BillingDraftId,
                            $"Não foi possível consultar a cobrança {chargeBatchItem.AsaasPaymentId}."),
                        cancellationToken);
                }
            }

            if (mudouAlgumItem)
            {
                await chargeBatchRepository.UpdateAsync(chargeBatch, cancellationToken);
            }
        }
    }
}
```

- [ ] **Step 4: Expor o endpoint**

Em `ChargeHistoryController.cs`, acrescente o serviço ao construtor primário:

```csharp
public sealed class ChargeHistoryController(
    ChargeHistoryService chargeHistoryService,
    ChargePaymentSynchronizationService chargePaymentSynchronizationService) : ControllerBase
```

E o método:

```csharp
    [HttpPost("synchronize")]
    [ProducesResponseType<IReadOnlyCollection<ChargeHistoryEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<ChargeHistoryEntryResponse>>> SynchronizeAsync(
        SynchronizeChargeHistoryRequest request,
        CancellationToken cancellationToken)
    {
        await chargePaymentSynchronizationService.SynchronizeAsync(request.OperatorId, cancellationToken);
        var historico = await chargeHistoryService.ListAsync(new ChargeHistoryQuery(), cancellationToken);
        return Ok(historico);
    }
```

Em `ChargeHistoryContracts.cs`, acrescente:

```csharp
public sealed record SynchronizeChargeHistoryRequest(string OperatorId);
```

Em `Program.cs`, junto dos demais serviços:

```csharp
builder.Services.AddScoped<ChargePaymentSynchronizationService>();
```

- [ ] **Step 5: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: PASS com os quatro testes novos.

- [ ] **Step 6: O filtro de situação do boleto**

A spec pede filtrar por situação do boleto. Ele só faz sentido agora que a
situação existe, e fica no servidor, como o de ambiente.

O filtro casa um **conjunto**, não um valor: o Asaas devolve `RECEIVED` e
`CONFIRMED` para a mesma coisa do ponto de vista de quem opera, e a coluna já
mostra os dois como "Pago". Um filtro de valor único esconderia metade das
cobranças pagas.

Em `ChargeHistoryFilter`:

```csharp
    /// <summary>
    /// Situações aceitas. É um conjunto porque "Pago" são dois estados do Asaas,
    /// `Received` e `Confirmed`, que a tela já mostra com o mesmo rótulo.
    /// </summary>
    public IReadOnlyCollection<ChargePaymentStatus>? PaymentStatuses { get; init; }
```

Em `ChargeHistoryQuery`, acrescente o parâmetro ao fim, para não quebrar as
chamadas posicionais dos testes já escritos:

```csharp
public sealed record ChargeHistoryQuery(
    string? Search = null,
    string? Environment = null,
    int? Year = null,
    int? Month = null,
    string? PaymentStatus = null);
```

Em `ChargeHistoryService.ListAsync`, ao montar o filtro:

```csharp
            PaymentStatuses = ParsePaymentStatuses(query.PaymentStatus),
```

E o método, junto dos demais. Ele traduz o que a tela oferece, não o enum cru:

```csharp
    private static IReadOnlyCollection<ChargePaymentStatus>? ParsePaymentStatuses(
        string? requestedPaymentStatus)
    {
        return requestedPaymentStatus switch
        {
            null or "" => null,
            "pending" => [ChargePaymentStatus.Pending],
            "paid" => [ChargePaymentStatus.Received, ChargePaymentStatus.Confirmed],
            "overdue" => [ChargePaymentStatus.Overdue],
            "refunded" => [ChargePaymentStatus.Refunded],
            "unknown" => [ChargePaymentStatus.Unknown],
            _ => throw new ValidationException(
                "A situação do boleto informada no filtro não existe."),
        };
    }
```

Em `InMemoryChargeHistoryRepository.Filtrar`, junto dos demais:

```csharp
        if (filter.PaymentStatuses is not null)
        {
            entries = entries.Where(entry => filter.PaymentStatuses.Contains(entry.PaymentStatus));
        }
```

Em `MySqlChargeHistoryRepository.ListAsync`, junto das demais condições. Os
nomes de parâmetro são gerados por índice porque `IN` não aceita uma lista num
parâmetro só:

```csharp
        var paymentStatusParameterNames = filter.PaymentStatuses is null
            ? []
            : filter.PaymentStatuses
                .Select((_, index) => $"@paymentStatus{index}")
                .ToArray();

        if (paymentStatusParameterNames.Length > 0)
        {
            conditions.Add($"cbi.payment_status IN ({string.Join(", ", paymentStatusParameterNames)})");
        }
```

E os parâmetros correspondentes, depois de abrir o `command`:

```csharp
        if (filter.PaymentStatuses is not null)
        {
            foreach (var (paymentStatus, index) in filter.PaymentStatuses.Select((value, index) => (value, index)))
            {
                command.Parameters.AddWithValue(paymentStatusParameterNames[index], paymentStatus.ToString());
            }
        }
```

Em `src/lib/api.ts`, acrescente a `ChargeHistoryFilters`:

```ts
  paymentStatus?: string;
```

Na tela, ao lado dos botões de ambiente, os mesmos rótulos que a coluna usa:

```tsx
  const paymentOptions: Array<{ key: string; label: string }> = [
    { key: "all", label: "Todos" },
    { key: "pending", label: "Em aberto" },
    { key: "paid", label: "Pago" },
    { key: "overdue", label: "Vencido" },
  ];
```

As chaves são as que o serviço traduz, e "pago" cobre `Received` e `Confirmed`.
Trate o filtro como o de ambiente: estado próprio, `"all"` vira `undefined` na
chamada, e `onFiltersChange` recarrega.

Acrescente também um teste em `ChargeHistoryServiceTests`, provando o que motivou
o conjunto:

```csharp
    /// <summary>
    /// `RECEIVED` e `CONFIRMED` são a mesma coisa para quem opera. Um filtro de
    /// valor único esconderia metade das cobranças pagas.
    /// </summary>
    [Fact]
    public async Task ListAsync_FilteringByPaidFindsBothReceivedAndConfirmed()
    {
        var scenario = await CreateScenarioAsync();
        var chargeBatches = scenario.DataStore.ChargeBatches.Values.ToArray();
        chargeBatches[0].Items.Single().ApplyPaymentStatus("RECEIVED", null, DateTimeOffset.UtcNow);
        chargeBatches[1].Items.Single().ApplyPaymentStatus("CONFIRMED", null, DateTimeOffset.UtcNow);

        var historico = await scenario.Service.ListAsync(
            new ChargeHistoryQuery(PaymentStatus: "paid"),
            CancellationToken.None);

        Assert.Equal(2, historico.Count);
    }
```

- [ ] **Step 7: O botão de atualizar**

Em `src/lib/api.ts`:

```ts
  synchronizeChargeHistory: (operatorId: string) =>
    request<ChargeHistoryEntry[]>("/api/charge-history/synchronize", {
      method: "POST",
      body: JSON.stringify({ operatorId }),
    }),
```

Em `src/app/page.tsx`, acrescente o estado e a função, junto das demais:

```tsx
  const [isSynchronizingCharges, setIsSynchronizingCharges] = useState(false);

  async function synchronizeChargeHistory() {
    if (isSynchronizingCharges) return;

    setIsSynchronizingCharges(true);
    try {
      setChargeHistory(await api.synchronizeChargeHistory(operatorId));
      showNotice("Situação dos boletos atualizada junto ao Asaas.");
    } catch (error) {
      showError(error instanceof Error ? error.message : "Não foi possível consultar os boletos no Asaas.");
    } finally {
      setIsSynchronizingCharges(false);
    }
  }
```

Passe `isSynchronizing={isSynchronizingCharges}` e
`onSynchronize={() => void synchronizeChargeHistory()}` a `ChargeHistoryPage`,
acrescente os dois às props do componente, e o botão no `PageHeading`:

```tsx
      action={entries.length > 0 ? (
        <button className="button-secondary" disabled={isSynchronizing} onClick={onSynchronize}>
          <RefreshCw className={isSynchronizing ? "animate-spin" : ""} size={16} />
          Atualizar situação
        </button>
      ) : undefined}
```

- [ ] **Step 8: Compilar e commitar**

```bash
cd C:\prog\evoque\web\client
npm run build
```

Esperado: `Compiled successfully`.

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Services/ChargePaymentSynchronizationService.cs Evoque.Billing.Api/Controllers/ChargeHistoryController.cs Evoque.Billing.Api/Contracts/ChargeHistoryContracts.cs Evoque.Billing.Api/Program.cs Evoque.Billing.Api.Tests/ChargePaymentSynchronizationServiceTests.cs
git commit -m "Ask Asaas which slips were paid

Only charges still open are queried; a paid one does not change again. A
failed query keeps what is already known and does not stop the others,
the same rule the invoice sync already follows.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"

cd C:\prog\evoque\web\client
git add src/lib/api.ts src/app/page.tsx
git commit -m "Let the operator refresh payment status from the history

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX"
```

---

## O que a spec pede e este plano não entrega

O **filtro de competência** existe na API — `ChargeHistoryQuery` aceita `year` e
`month`, e os dois repositórios o aplicam — mas não ganha controle na tela. A
busca por empresa e o filtro de ambiente cobrem o caso que originou a feature, e
com 17 cobranças um seletor de competência não ajuda ninguém a achar nada.

Fica registrado como lacuna consciente, não como esquecimento: quando o
histórico crescer, o controle é acrescentar um seletor que já tem backend
pronto.

### Comportamentos que duas implementações garantem sozinhas

A regra de "isto é busca por CNPJ ou por nome" mora num lugar só,
`ChargeHistoryFilter`, depois de ter divergido uma vez e ter sido corrigida. As
outras não:

| Comportamento | Garantido por |
|---|---|
| Busca por CNPJ ou por nome | `ChargeHistoryFilter`, um lugar só |
| Dígitos extraídos do termo | `ChargeHistoryFilter`, um lugar só |
| Nota de maior sequência | duas implementações independentes |
| Ordem cronológica decrescente | duas implementações independentes |
| Total da prévia | duas implementações independentes |

As três últimas concordam hoje porque alguém leu as duas lado a lado, não porque
algo as obrigue. Não existe fixture MySQL neste repositório — nenhum repositório
tem — então o CI exercita só a versão em memória, que é justamente a que não roda
em produção.

É risco aceito, não descuido: montar fixture de banco foge ao escopo desta
feature e mudaria o padrão de teste de todo o projeto. Fica registrado porque a
busca por CNPJ já divergiu exatamente assim, e foi encontrada por leitura, não
por teste.

A **paginação** citada na spec também não entra. Ela é necessária na casa dos
milhares de registros; hoje são 17, e paginar agora seria código sem exercício.
O ponto de atenção fica em `MySqlChargeHistoryRepository.ListAsync`, que devolve
tudo o que casa com o filtro.

## Depois do plano

Atualizar `.agents/PENDENCIAS.md`: o item **2.1** passa a parcialmente atendido —
o histórico responde o que foi emitido e pago, mas a consulta ao log de
auditoria por operador e por período continua aberta, e o item **2.5** (operador
fixo em `"operador-web"`) segue reduzindo o valor de qualquer trilha.
