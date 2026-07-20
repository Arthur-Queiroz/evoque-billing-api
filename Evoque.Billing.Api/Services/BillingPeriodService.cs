using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

public sealed class BillingPeriodService(
    IBillingPeriodRepository billingPeriodRepository,
    IAuditLogRepository auditLogRepository)
{
    public async Task<BillingPeriod> CreateAsync(
        BillingPeriodReference reference,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var existingBillingPeriod = await billingPeriodRepository.FindByReferenceAsync(reference, cancellationToken);
        if (existingBillingPeriod is not null)
        {
            throw new ConflictException($"A competência {reference} já existe.");
        }

        var createdAt = DateTimeOffset.UtcNow;
        var billingPeriod = new BillingPeriod(reference, createdAt);

        await billingPeriodRepository.AddAsync(billingPeriod, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                "billing-period.created",
                operatorId,
                createdAt,
                billingPeriod.Id,
                null,
                $"Competência {reference} criada."),
            cancellationToken);

        return billingPeriod;
    }

    public async Task<BillingPeriod> GetByReferenceAsync(
        BillingPeriodReference reference,
        CancellationToken cancellationToken)
    {
        return await billingPeriodRepository.FindByReferenceAsync(reference, cancellationToken)
            ?? throw new NotFoundException($"A competência {reference} não foi encontrada.");
    }

    public Task<IReadOnlyCollection<BillingPeriod>> ListAsync(CancellationToken cancellationToken)
    {
        return billingPeriodRepository.ListAsync(cancellationToken);
    }
}
