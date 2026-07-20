using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<AuditLog>> ListByBillingDraftIdAsync(
        Guid billingDraftId,
        CancellationToken cancellationToken);
}
