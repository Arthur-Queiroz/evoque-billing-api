using Evoque.Billing.Api.Domain;
using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class MySqlCompanyBillingScheduleRepository(MySqlConnectionFactory connectionFactory)
    : ICompanyBillingScheduleRepository
{
    public async Task UpsertAsync(CompanyBillingSchedule companyBillingSchedule, CancellationToken cancellationToken)
    {
        const string commandText = """
            INSERT INTO company_billing_schedules
                (external_company_id, billing_day, is_active, updated_by, updated_at)
            VALUES
                (@externalCompanyId, @billingDay, @isActive, @updatedBy, @updatedAt)
            ON DUPLICATE KEY UPDATE
                billing_day = VALUES(billing_day),
                is_active = VALUES(is_active),
                updated_by = VALUES(updated_by),
                updated_at = VALUES(updated_at);
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        AddParameters(command, companyBillingSchedule);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<IReadOnlyCollection<CompanyBillingSchedule>> ListAsync(CancellationToken cancellationToken)
    {
        return ListByConditionAsync("", null, cancellationToken);
    }

    public Task<IReadOnlyCollection<CompanyBillingSchedule>> ListActiveByBillingDayAsync(
        int billingDay,
        CancellationToken cancellationToken)
    {
        return ListByConditionAsync("WHERE is_active = 1 AND billing_day = @billingDay", billingDay, cancellationToken);
    }

    private async Task<IReadOnlyCollection<CompanyBillingSchedule>> ListByConditionAsync(
        string whereClause,
        int? billingDay,
        CancellationToken cancellationToken)
    {
        var commandText = $"""
            SELECT external_company_id, billing_day, is_active, updated_by, updated_at
            FROM company_billing_schedules
            {whereClause}
            ORDER BY billing_day, external_company_id;
            """;

        var schedules = new List<CompanyBillingSchedule>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        if (billingDay is not null)
        {
            command.Parameters.AddWithValue("@billingDay", billingDay.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            schedules.Add(new CompanyBillingSchedule(
                reader.GetString("external_company_id"),
                reader.GetInt32("billing_day"),
                reader.GetBoolean("is_active"),
                reader.GetString("updated_by"),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc))));
        }

        return schedules;
    }

    private static void AddParameters(MySqlCommand command, CompanyBillingSchedule companyBillingSchedule)
    {
        command.Parameters.AddWithValue("@externalCompanyId", companyBillingSchedule.ExternalCompanyId);
        command.Parameters.AddWithValue("@billingDay", companyBillingSchedule.BillingDay);
        command.Parameters.AddWithValue("@isActive", companyBillingSchedule.IsActive);
        command.Parameters.AddWithValue("@updatedBy", companyBillingSchedule.UpdatedBy);
        command.Parameters.AddWithValue("@updatedAt", companyBillingSchedule.UpdatedAt.UtcDateTime);
    }
}
