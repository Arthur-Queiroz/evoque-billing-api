using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

public sealed class BillingDraftService(
    IBillingPeriodRepository billingPeriodRepository,
    IBillingDraftRepository billingDraftRepository,
    IAuditLogRepository auditLogRepository)
{
    public async Task<BillingDraft> CreateAsync(
        BillingPeriodReference reference,
        CreateBillingDraftCommand command,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var billingPeriod = await GetBillingPeriodAsync(reference, cancellationToken);
        ValidateNewDraft(billingPeriod);

        var items = command.Items
            .Select(item => new BillingDraftItem(item.Description, item.Quantity, item.UnitAmount, item.ExternalMemberId))
            .ToArray();

        foreach (var item in items)
        {
            item.Validate();
        }

        var createdAt = DateTimeOffset.UtcNow;
        var billingDraft = new BillingDraft(
            billingPeriod.Id,
            command.ExternalCompanyId,
            command.CompanyName,
            command.CompanyTaxId,
            command.AsaasCustomerId,
            items,
            createdAt);

        billingPeriod.MarkAwaitingReview(createdAt);
        await billingDraftRepository.AddAsync(billingDraft, cancellationToken);
        await billingPeriodRepository.UpdateAsync(billingPeriod, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                "billing-draft.created",
                operatorId,
                createdAt,
                billingPeriod.Id,
                billingDraft.Id,
                $"Prévia criada para {billingDraft.CompanyName} no valor de {billingDraft.TotalAmount:F2}."),
            cancellationToken);

        return billingDraft;
    }

    public async Task<BillingDraft> ApproveAsync(
        Guid billingDraftId,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var billingDraft = await GetBillingDraftAsync(billingDraftId, cancellationToken);
        var approvedAt = DateTimeOffset.UtcNow;

        billingDraft.Approve(operatorId, approvedAt);
        await billingDraftRepository.UpdateAsync(billingDraft, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                "billing-draft.approved",
                operatorId,
                approvedAt,
                billingDraft.BillingPeriodId,
                billingDraft.Id,
                $"Prévia versão {billingDraft.Version} aprovada."),
            cancellationToken);

        await MarkBillingPeriodApprovedWhenReadyAsync(billingDraft.BillingPeriodId, operatorId, cancellationToken);
        return billingDraft;
    }

    public async Task<IReadOnlyCollection<BillingDraft>> ListAsync(
        BillingPeriodReference reference,
        CancellationToken cancellationToken)
    {
        var billingPeriod = await GetBillingPeriodAsync(reference, cancellationToken);
        return await billingDraftRepository.ListByBillingPeriodIdAsync(billingPeriod.Id, cancellationToken);
    }

    public Task<BillingDraft> GetByIdAsync(Guid billingDraftId, CancellationToken cancellationToken)
    {
        return GetBillingDraftAsync(billingDraftId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<AuditLog>> ListAuditLogsAsync(
        Guid billingDraftId,
        CancellationToken cancellationToken)
    {
        _ = await GetBillingDraftAsync(billingDraftId, cancellationToken);
        return await auditLogRepository.ListByBillingDraftIdAsync(billingDraftId, cancellationToken);
    }

    private async Task<BillingPeriod> GetBillingPeriodAsync(
        BillingPeriodReference reference,
        CancellationToken cancellationToken)
    {
        return await billingPeriodRepository.FindByReferenceAsync(reference, cancellationToken)
            ?? throw new NotFoundException($"A competência {reference} não foi encontrada.");
    }

    private async Task<BillingDraft> GetBillingDraftAsync(Guid billingDraftId, CancellationToken cancellationToken)
    {
        return await billingDraftRepository.FindByIdAsync(billingDraftId, cancellationToken)
            ?? throw new NotFoundException("A prévia de faturamento não foi encontrada.");
    }

    private static void ValidateNewDraft(BillingPeriod billingPeriod)
    {
        if (billingPeriod.Status is BillingPeriodStatus.Approved or BillingPeriodStatus.ChargesCreated)
        {
            throw new ConflictException("Não é possível criar prévias em uma competência aprovada ou faturada.");
        }
    }

    private async Task MarkBillingPeriodApprovedWhenReadyAsync(
        Guid billingPeriodId,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var billingDrafts = await billingDraftRepository.ListByBillingPeriodIdAsync(billingPeriodId, cancellationToken);
        if (billingDrafts.Count == 0 || billingDrafts.Any(billingDraft => billingDraft.Status != BillingDraftStatus.Approved))
        {
            return;
        }

        var billingPeriod = (await billingPeriodRepository.ListAsync(cancellationToken))
            .SingleOrDefault(currentBillingPeriod => currentBillingPeriod.Id == billingPeriodId)
            ?? throw new NotFoundException("A competência da prévia não foi encontrada.");

        var approvedAt = DateTimeOffset.UtcNow;
        billingPeriod.MarkApproved(approvedAt);
        await billingPeriodRepository.UpdateAsync(billingPeriod, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                "billing-period.approved",
                operatorId,
                approvedAt,
                billingPeriod.Id,
                null,
                "Todas as prévias da competência foram aprovadas."),
            cancellationToken);
    }
}

public sealed record CreateBillingDraftCommand(
    string ExternalCompanyId,
    string CompanyName,
    string CompanyTaxId,
    string? AsaasCustomerId,
    IReadOnlyCollection<CreateBillingDraftItemCommand> Items);

public sealed record CreateBillingDraftItemCommand(
    string Description,
    decimal Quantity,
    decimal UnitAmount,
    string? ExternalMemberId);
