using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Repositories;

public sealed class InMemoryCompanyBillingScheduleRepository(InMemoryBillingDataStore dataStore)
    : ICompanyBillingScheduleRepository
{
    public Task UpsertAsync(CompanyBillingSchedule companyBillingSchedule, CancellationToken cancellationToken)
    {
        dataStore.CompanyBillingSchedules[companyBillingSchedule.ExternalCompanyId] = companyBillingSchedule;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<CompanyBillingSchedule>> ListAsync(CancellationToken cancellationToken)
    {
        var schedules = dataStore.CompanyBillingSchedules.Values
            .OrderBy(schedule => schedule.BillingDay)
            .ThenBy(schedule => schedule.ExternalCompanyId)
            .ToArray();
        return Task.FromResult<IReadOnlyCollection<CompanyBillingSchedule>>(schedules);
    }

    public Task<IReadOnlyCollection<CompanyBillingSchedule>> ListActiveByBillingDayAsync(
        int billingDay,
        CancellationToken cancellationToken)
    {
        var schedules = dataStore.CompanyBillingSchedules.Values
            .Where(schedule => schedule.IsActive && schedule.BillingDay == billingDay)
            .OrderBy(schedule => schedule.ExternalCompanyId)
            .ToArray();
        return Task.FromResult<IReadOnlyCollection<CompanyBillingSchedule>>(schedules);
    }
}
