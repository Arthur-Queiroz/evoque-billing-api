using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

public sealed class CompanyBillingScheduleService(
    ICompanyBillingScheduleRepository companyBillingScheduleRepository,
    IAuditLogRepository auditLogRepository)
{
    public async Task<CompanyBillingScheduleResponse> UpsertAsync(
        string externalCompanyId,
        UpsertCompanyBillingScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        var companyBillingSchedule = CompanyBillingSchedule.Create(
            externalCompanyId,
            request.BillingDay,
            request.IsActive,
            request.OperatorId,
            updatedAt);
        await companyBillingScheduleRepository.UpsertAsync(companyBillingSchedule, cancellationToken);
        await auditLogRepository.AddAsync(
            AuditLog.Create(
                "company-billing-schedule.updated",
                request.OperatorId,
                updatedAt,
                null,
                null,
                $"Agenda da empresa {companyBillingSchedule.ExternalCompanyId} atualizada para dia {companyBillingSchedule.BillingDay:00}; ativa: {companyBillingSchedule.IsActive}."),
            cancellationToken);

        return CompanyBillingScheduleResponse.FromDomain(companyBillingSchedule);
    }

    public async Task<IReadOnlyCollection<CompanyBillingScheduleResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var companyBillingSchedules = await companyBillingScheduleRepository.ListAsync(cancellationToken);
        return companyBillingSchedules.Select(CompanyBillingScheduleResponse.FromDomain).ToArray();
    }
}
