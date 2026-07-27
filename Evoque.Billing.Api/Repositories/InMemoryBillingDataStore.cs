using System.Collections.Concurrent;
using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryBillingDataStore
{
    public ConcurrentDictionary<string, BillingPeriod> BillingPeriods { get; } = new();

    public ConcurrentDictionary<Guid, BillingDraft> BillingDrafts { get; } = new();

    public ConcurrentDictionary<Guid, AuditLog> AuditLogs { get; } = new();

    public ConcurrentDictionary<Guid, ChargeBatch> ChargeBatches { get; } = new();

    public ConcurrentDictionary<string, CompanyBillingSchedule> CompanyBillingSchedules { get; } = new();

    public ConcurrentDictionary<string, Company> Companies { get; } = new();

    public ConcurrentDictionary<Guid, CompanyCatalogImport> CompanyCatalogImports { get; } = new();

    public ConcurrentDictionary<Guid, IReadOnlyCollection<CompanyCatalogImportMember>>
        CompanyCatalogImportMembers { get; } = new();
}
