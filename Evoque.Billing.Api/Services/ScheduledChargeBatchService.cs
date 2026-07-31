using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

public sealed class ScheduledChargeBatchService(
    IBillingPeriodRepository billingPeriodRepository,
    IBillingDraftRepository billingDraftRepository,
    ICompanyBillingScheduleRepository companyBillingScheduleRepository,
    ICompanyRepository companyRepository,
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

        CompanyBillingSchedule.ValidateClosingDay(request.ClosingDay);

        // O vencimento não pertence à competência: um período que fecha em 25/06
        // costuma vencer em 06/07. Ele só não pode ser anterior ao fechamento.
        var closingDate = ResolveClosingDate(billingPeriodReference, request.ClosingDay);
        if (request.DueDate < closingDate)
        {
            throw new ValidationException(
                $"O vencimento {request.DueDate:dd/MM/yyyy} é anterior ao fechamento "
                + $"{closingDate:dd/MM/yyyy} da competência selecionada.");
        }

        var scheduledCompanies = await companyBillingScheduleRepository.ListActiveByClosingDayAsync(
            request.ClosingDay,
            cancellationToken);
        if (scheduledCompanies.Count == 0)
        {
            throw new ValidationException(
                $"Não há empresas ativas com fechamento no dia {request.ClosingDay:00}.");
        }

        var scheduledCompanyIds = await ExcludeInactiveCatalogCompaniesAsync(
            scheduledCompanies,
            cancellationToken);
        if (scheduledCompanyIds.Count == 0)
        {
            throw new ValidationException(
                $"As empresas com fechamento no dia {request.ClosingDay:00} estão inativas no catálogo.");
        }

        var billingDrafts = await billingDraftRepository.ListByBillingPeriodIdAsync(billingPeriod.Id, cancellationToken);
        var billingDraftIds = billingDrafts
            .Where(billingDraft => billingDraft.Status == BillingDraftStatus.Approved)
            .Where(billingDraft => scheduledCompanyIds.Contains(billingDraft.ExternalCompanyId))
            .Select(billingDraft => billingDraft.Id)
            .ToArray();
        if (billingDraftIds.Length == 0)
        {
            throw new ValidationException(
                $"Não há prévias aprovadas para as empresas com fechamento no dia {request.ClosingDay:00}.");
        }

        return await chargeBatchService.CreatePreviewAsync(
            new CreateChargeBatchPreviewRequest(
                request.OperatorId,
                request.DueDate,
                request.AsaasEnvironment,
                billingDraftIds),
            cancellationToken);
    }

    /// <summary>
    /// A competência identifica o ciclo pelo mês em que o período fecha. Um
    /// fechamento no dia 25 da competência 06/2026 é 25/06/2026.
    /// </summary>
    private static DateOnly ResolveClosingDate(
        BillingPeriodReference billingPeriodReference,
        int closingDay)
    {
        return new DateOnly(billingPeriodReference.Year, billingPeriodReference.Month, closingDay);
    }

    /// <summary>
    /// Uma empresa inativada no catálogo não entra em lote novo, mesmo que uma
    /// agenda ativa antiga tenha sobrado. Agendas cujo CNPJ ainda não existe no
    /// catálogo continuam válidas, para não quebrar configurações anteriores.
    /// </summary>
    private async Task<HashSet<string>> ExcludeInactiveCatalogCompaniesAsync(
        IReadOnlyCollection<CompanyBillingSchedule> scheduledCompanies,
        CancellationToken cancellationToken)
    {
        var eligibleCompanyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var companyBillingSchedule in scheduledCompanies)
        {
            var company = await companyRepository.FindByTaxIdAsync(
                companyBillingSchedule.ExternalCompanyId,
                cancellationToken);
            if (company is not null && !company.IsActive)
            {
                continue;
            }

            eligibleCompanyIds.Add(companyBillingSchedule.ExternalCompanyId);
        }

        return eligibleCompanyIds;
    }
}
