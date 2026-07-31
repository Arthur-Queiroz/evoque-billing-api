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
            .OrderBy(schedule => schedule.ClosingDay)
            .ThenBy(schedule => schedule.ExternalCompanyId)
            .ToArray();
        return Task.FromResult<IReadOnlyCollection<CompanyBillingSchedule>>(schedules);
    }

    public Task<IReadOnlyCollection<CompanyBillingSchedule>> ListActiveByClosingDayAsync(
        int closingDay,
        CancellationToken cancellationToken)
    {
        var schedules = dataStore.CompanyBillingSchedules.Values
            .Where(schedule => schedule.IsActive && schedule.ClosingDay == closingDay)
            .OrderBy(schedule => schedule.ExternalCompanyId)
            .ToArray();
        return Task.FromResult<IReadOnlyCollection<CompanyBillingSchedule>>(schedules);
    }
}
