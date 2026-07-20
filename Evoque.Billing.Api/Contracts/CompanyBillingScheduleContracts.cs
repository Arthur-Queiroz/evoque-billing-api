using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Contracts;

public sealed record UpsertCompanyBillingScheduleRequest(
    int BillingDay,
    bool IsActive,
    string OperatorId);

public sealed record CompanyBillingScheduleResponse(
    string ExternalCompanyId,
    int BillingDay,
    bool IsActive,
    string UpdatedBy,
    DateTimeOffset UpdatedAt)
{
    public static CompanyBillingScheduleResponse FromDomain(CompanyBillingSchedule companyBillingSchedule)
    {
        return new CompanyBillingScheduleResponse(
            companyBillingSchedule.ExternalCompanyId,
            companyBillingSchedule.BillingDay,
            companyBillingSchedule.IsActive,
            companyBillingSchedule.UpdatedBy,
            companyBillingSchedule.UpdatedAt);
    }
}

public sealed record CreateScheduledChargeBatchPreviewRequest(
    string OperatorId,
    DateOnly DueDate,
    string AsaasEnvironment);
