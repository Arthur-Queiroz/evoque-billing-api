using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

public sealed class BillingDraftService(
    IBillingPeriodRepository billingPeriodRepository,
    IBillingDraftRepository billingDraftRepository,
    IChargeBatchRepository chargeBatchRepository,
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
        var version = await ResolveNextVersionAsync(
            billingPeriod.Id,
            command.ExternalCompanyId,
            cancellationToken);

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
            version,
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

    public async Task<BillingDraft> CancelAsync(
        Guid billingDraftId,
        string reason,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var billingDraft = await GetBillingDraftAsync(billingDraftId, cancellationToken);
        await EnsureNoChargeWasCreatedAsync(billingDraft, cancellationToken);

        var cancelledAt = DateTimeOffset.UtcNow;
        billingDraft.Cancel(operatorId, reason, cancelledAt);
        await billingDraftRepository.UpdateAsync(billingDraft, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                "billing-draft.cancelled",
                operatorId,
                cancelledAt,
                billingDraft.BillingPeriodId,
                billingDraft.Id,
                $"Prévia versão {billingDraft.Version} cancelada. Motivo: {billingDraft.CancellationReason}"),
            cancellationToken);

        return billingDraft;
    }

    public async Task<BillingDraft> SupersedeSandboxDraftAsync(
        Guid billingDraftId,
        string reason,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var billingDraft = await GetBillingDraftAsync(billingDraftId, cancellationToken);
        var chargeBatches = await chargeBatchRepository.ListByBillingPeriodIdAsync(
            billingDraft.BillingPeriodId,
            cancellationToken);
        var batchesWithCreatedCharge = chargeBatches
            .Where(chargeBatch => chargeBatch.Items.Any(item =>
                item.BillingDraftId == billingDraft.Id
                && !string.IsNullOrWhiteSpace(item.AsaasPaymentId)))
            .ToArray();
        if (batchesWithCreatedCharge.Length == 0)
        {
            throw new ConflictException(
                "Esta prévia não possui cobrança Sandbox criada. Cancele-a em vez de substituí-la.");
        }

        if (batchesWithCreatedCharge.Any(chargeBatch =>
                chargeBatch.AsaasEnvironment == AsaasEnvironment.Production))
        {
            throw new ConflictException(
                "Uma prévia com cobrança criada no Asaas Produção não pode ser substituída.");
        }

        var supersededAt = DateTimeOffset.UtcNow;
        billingDraft.Supersede(operatorId, reason, supersededAt);
        await billingDraftRepository.UpdateAsync(billingDraft, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                "billing-draft.superseded-after-sandbox",
                operatorId,
                supersededAt,
                billingDraft.BillingPeriodId,
                billingDraft.Id,
                $"Prévia versão {billingDraft.Version} substituída após teste Sandbox. Motivo: {billingDraft.SupersessionReason}"),
            cancellationToken);

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
        if (billingPeriod.Status == BillingPeriodStatus.ChargesCreated)
        {
            throw new ConflictException("Não é possível criar prévias em uma competência encerrada.");
        }
    }

    private async Task<int> ResolveNextVersionAsync(
        Guid billingPeriodId,
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var existingBillingDrafts = await billingDraftRepository.ListByBillingPeriodIdAsync(
            billingPeriodId,
            cancellationToken);
        var companyDrafts = existingBillingDrafts
            .Where(existingBillingDraft =>
                string.Equals(
                    existingBillingDraft.ExternalCompanyId,
                    externalCompanyId,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (companyDrafts.Any(existingBillingDraft =>
                existingBillingDraft.Status is not BillingDraftStatus.Cancelled
                    and not BillingDraftStatus.Superseded))
        {
            throw new ConflictException(
                "Já existe uma prévia para esta empresa e competência.");
        }

        return companyDrafts.Length == 0
            ? 1
            : companyDrafts.Max(existingBillingDraft => existingBillingDraft.Version) + 1;
    }

    private async Task EnsureNoChargeWasCreatedAsync(
        BillingDraft billingDraft,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(billingDraft.AsaasPaymentId))
        {
            throw new ConflictException("Uma prévia com cobrança criada no Asaas não pode ser cancelada.");
        }

        var chargeBatches = await chargeBatchRepository.ListByBillingPeriodIdAsync(
            billingDraft.BillingPeriodId,
            cancellationToken);
        var createdCharge = chargeBatches
            .SelectMany(chargeBatch => chargeBatch.Items)
            .FirstOrDefault(item =>
                item.BillingDraftId == billingDraft.Id
                && !string.IsNullOrWhiteSpace(item.AsaasPaymentId));
        if (createdCharge is not null)
        {
            throw new ConflictException("Uma prévia com cobrança criada no Asaas não pode ser cancelada.");
        }
    }

    private async Task MarkBillingPeriodApprovedWhenReadyAsync(
        Guid billingPeriodId,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var billingDrafts = await billingDraftRepository.ListByBillingPeriodIdAsync(billingPeriodId, cancellationToken);
        if (billingDrafts.Count == 0
            || billingDrafts.Any(billingDraft =>
                billingDraft.Status is not BillingDraftStatus.Approved
                    and not BillingDraftStatus.ChargeCreated
                    and not BillingDraftStatus.Cancelled
                    and not BillingDraftStatus.Superseded))
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
