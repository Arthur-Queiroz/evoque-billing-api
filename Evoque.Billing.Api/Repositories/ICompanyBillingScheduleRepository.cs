using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public interface ICompanyBillingScheduleRepository
{
    Task UpsertAsync(CompanyBillingSchedule companyBillingSchedule, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CompanyBillingSchedule>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CompanyBillingSchedule>> ListActiveByBillingDayAsync(
        int billingDay,
        CancellationToken cancellationToken);
}
