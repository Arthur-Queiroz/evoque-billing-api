using Evoque.Billing.Api.Domain;
using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class MySqlAuditLogRepository(MySqlConnectionFactory connectionFactory) : IAuditLogRepository
{
    internal const string InsertCommandText = """
        INSERT INTO audit_logs
            (id, action, operator_id, occurred_at, billing_period_id, billing_draft_id, details)
        VALUES
            (@id, @action, @operatorId, @occurredAt, @billingPeriodId, @billingDraftId, @details);
        """;

    public async Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(InsertCommandText, connection);
        AddParameters(command, auditLog);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static void AddParameters(MySqlCommand command, AuditLog auditLog)
    {
        command.Parameters.AddWithValue("@id", auditLog.Id.ToString());
        command.Parameters.AddWithValue("@action", auditLog.Action);
        command.Parameters.AddWithValue("@operatorId", auditLog.OperatorId);
        command.Parameters.AddWithValue("@occurredAt", auditLog.OccurredAt.UtcDateTime);
        command.Parameters.AddWithValue("@billingPeriodId", auditLog.BillingPeriodId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@billingDraftId", auditLog.BillingDraftId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@details", auditLog.Details);
    }

    public async Task<IReadOnlyCollection<AuditLog>> ListByBillingDraftIdAsync(
        Guid billingDraftId,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT id, action, operator_id, occurred_at, billing_period_id, billing_draft_id, details
            FROM audit_logs
            WHERE billing_draft_id = @billingDraftId
            ORDER BY occurred_at;
            """;

        var auditLogs = new List<AuditLog>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@billingDraftId", billingDraftId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            auditLogs.Add(new AuditLog(
                reader.GetGuid("id"),
                reader.GetString("action"),
                reader.GetString("operator_id"),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime("occurred_at"), DateTimeKind.Utc)),
                GetNullableGuid(reader, "billing_period_id"),
                GetNullableGuid(reader, "billing_draft_id"),
                reader.GetString("details")));
        }

        return auditLogs;
    }

    private static Guid? GetNullableGuid(MySqlDataReader reader, string columnName)
    {
        return reader.IsDBNull(reader.GetOrdinal(columnName)) ? null : reader.GetGuid(columnName);
    }
}
