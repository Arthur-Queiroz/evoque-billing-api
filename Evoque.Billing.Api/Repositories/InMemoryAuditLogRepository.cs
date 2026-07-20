using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryAuditLogRepository(InMemoryBillingDataStore dataStore) : IAuditLogRepository
{
    public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken)
    {
        dataStore.AuditLogs[auditLog.Id] = auditLog;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<AuditLog>> ListByBillingDraftIdAsync(
        Guid billingDraftId,
        CancellationToken cancellationToken)
    {
        var auditLogs = dataStore.AuditLogs.Values
            .Where(auditLog => auditLog.BillingDraftId == billingDraftId)
            .OrderBy(auditLog => auditLog.OccurredAt)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<AuditLog>>(auditLogs);
    }
}
