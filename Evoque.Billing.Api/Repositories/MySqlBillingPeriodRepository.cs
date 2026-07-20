using Evoque.Billing.Api.Domain;
using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class MySqlBillingPeriodRepository(MySqlConnectionFactory connectionFactory) : IBillingPeriodRepository
{
    public async Task AddAsync(BillingPeriod billingPeriod, CancellationToken cancellationToken)
    {
        const string commandText = """
            INSERT INTO billing_periods
                (id, reference_year, reference_month, status, created_at, updated_at)
            VALUES
                (@id, @year, @month, @status, @createdAt, @updatedAt);
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@id", billingPeriod.Id.ToString());
        command.Parameters.AddWithValue("@year", billingPeriod.Reference.Year);
        command.Parameters.AddWithValue("@month", billingPeriod.Reference.Month);
        command.Parameters.AddWithValue("@status", billingPeriod.Status.ToString());
        command.Parameters.AddWithValue("@createdAt", billingPeriod.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAt", billingPeriod.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<BillingPeriod?> FindByReferenceAsync(
        BillingPeriodReference reference,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT id, reference_year, reference_month, status, created_at, updated_at
            FROM billing_periods
            WHERE reference_year = @year AND reference_month = @month;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@year", reference.Year);
        command.Parameters.AddWithValue("@month", reference.Month);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadBillingPeriod(reader) : null;
    }

    public async Task<IReadOnlyCollection<BillingPeriod>> ListAsync(CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT id, reference_year, reference_month, status, created_at, updated_at
            FROM billing_periods
            ORDER BY reference_year DESC, reference_month DESC;
            """;

        var billingPeriods = new List<BillingPeriod>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            billingPeriods.Add(ReadBillingPeriod(reader));
        }

        return billingPeriods;
    }

    public async Task UpdateAsync(BillingPeriod billingPeriod, CancellationToken cancellationToken)
    {
        const string commandText = """
            UPDATE billing_periods
            SET status = @status, updated_at = @updatedAt
            WHERE id = @id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@id", billingPeriod.Id.ToString());
        command.Parameters.AddWithValue("@status", billingPeriod.Status.ToString());
        command.Parameters.AddWithValue("@updatedAt", billingPeriod.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static BillingPeriod ReadBillingPeriod(MySqlDataReader reader)
    {
        return BillingPeriod.Restore(
            Guid.Parse(reader.GetString("id")),
            new BillingPeriodReference(reader.GetInt32("reference_year"), reader.GetInt32("reference_month")),
            Enum.Parse<BillingPeriodStatus>(reader.GetString("status")),
            ReadUtcDateTime(reader, "created_at"),
            ReadUtcDateTime(reader, "updated_at"));
    }

    private static DateTimeOffset ReadUtcDateTime(MySqlDataReader reader, string columnName)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(columnName), DateTimeKind.Utc));
    }
}
