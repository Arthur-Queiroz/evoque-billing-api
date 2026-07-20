using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

public sealed class ScheduledChargeBatchService(
    IBillingPeriodRepository billingPeriodRepository,
    IBillingDraftRepository billingDraftRepository,
    ICompanyBillingScheduleRepository companyBillingScheduleRepository,
    ChargeBatchService chargeBatchService)
{
    public async Task<ChargeBatchResponse> CreatePreviewAsync(
        BillingPeriodReference billingPeriodReference,
        CreateScheduledChargeBatchPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var billingPeriod = await billingPeriodRepository.FindByReferenceAsync(
            billingPeriodReference,
            cancellationToken)
            ?? throw new NotFoundException("A competência solicitada não foi encontrada.");

        if (request.DueDate.Year != billingPeriodReference.Year || request.DueDate.Month != billingPeriodReference.Month)
        {
            throw new ValidationException("O vencimento do lote precisa pertencer à competência selecionada.");
        }

        var scheduledCompanies = await companyBillingScheduleRepository.ListActiveByBillingDayAsync(
            request.DueDate.Day,
            cancellationToken);
        if (scheduledCompanies.Count == 0)
        {
            throw new ValidationException($"Não há empresas ativas configuradas para faturamento no dia {request.DueDate.Day:00}.");
        }

        var scheduledCompanyIds = scheduledCompanies
            .Select(companyBillingSchedule => companyBillingSchedule.ExternalCompanyId)
            .ToHashSet(StringComparer.Ordinal);
        var billingDrafts = await billingDraftRepository.ListByBillingPeriodIdAsync(billingPeriod.Id, cancellationToken);
        var billingDraftIds = billingDrafts
            .Where(billingDraft => billingDraft.Status == BillingDraftStatus.Approved)
            .Where(billingDraft => scheduledCompanyIds.Contains(billingDraft.ExternalCompanyId))
            .Select(billingDraft => billingDraft.Id)
            .ToArray();
        if (billingDraftIds.Length == 0)
        {
            throw new ValidationException(
                $"Não há prévias aprovadas para as empresas agendadas no dia {request.DueDate.Day:00}.");
        }

        return await chargeBatchService.CreatePreviewAsync(
            new CreateChargeBatchPreviewRequest(
                request.OperatorId,
                request.DueDate,
                request.AsaasEnvironment,
                billingDraftIds),
            cancellationToken);
    }
}
