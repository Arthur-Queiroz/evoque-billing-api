using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Contracts;

public sealed record UpsertCompanyBillingScheduleRequest(
    int ClosingDay,
    bool IsActive,
    string OperatorId);

public sealed record CompanyBillingScheduleResponse(
    string ExternalCompanyId,
    int ClosingDay,
    bool IsActive,
    string UpdatedBy,
    DateTimeOffset UpdatedAt)
{
    public static CompanyBillingScheduleResponse FromDomain(CompanyBillingSchedule companyBillingSchedule)
    {
        return new CompanyBillingScheduleResponse(
            companyBillingSchedule.ExternalCompanyId,
            companyBillingSchedule.ClosingDay,
            companyBillingSchedule.IsActive,
            companyBillingSchedule.UpdatedBy,
            companyBillingSchedule.UpdatedAt);
    }
}

/// <summary>
/// Lote agendado de um ciclo. <paramref name="ClosingDay"/> escolhe as empresas
/// cujo período fecha naquele dia; <paramref name="DueDate"/> é o vencimento
/// enviado ao Asaas e é independente — costuma cair alguns dias depois do
/// fechamento, quase sempre no mês seguinte.
/// </summary>
public sealed record CreateScheduledChargeBatchPreviewRequest(
    string OperatorId,
    int ClosingDay,
    DateOnly DueDate,
    string AsaasEnvironment);
