using Evoque.Billing.Api.Domain;
using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class MySqlChargeBatchRepository(MySqlConnectionFactory connectionFactory) : IChargeBatchRepository
{
    public async Task AddAsync(ChargeBatch chargeBatch, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string batchCommandText = """
            INSERT INTO charge_batches
                (id, billing_period_id, due_date, operator_id, retry_of_charge_batch_id,
                 asaas_environment, status, approved_by, approved_at, created_at, updated_at)
            VALUES
                (@id, @billingPeriodId, @dueDate, @operatorId, @retryOfChargeBatchId,
                 @asaasEnvironment, @status, @approvedBy, @approvedAt, @createdAt, @updatedAt);
            """;
        await using (var batchCommand = new MySqlCommand(batchCommandText, connection, transaction))
        {
            AddChargeBatchParameters(batchCommand, chargeBatch);
            await batchCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var chargeBatchItem in chargeBatch.Items)
        {
            await InsertOrUpdateItemAsync(connection, transaction, chargeBatch.Id, chargeBatchItem, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ChargeBatch?> FindByIdAsync(Guid chargeBatchId, CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT id, billing_period_id, due_date, operator_id, retry_of_charge_batch_id,
                   asaas_environment, status, approved_by, approved_at, created_at, updated_at
            FROM charge_batches
            WHERE id = @id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@id", chargeBatchId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var chargeBatchData = ReadChargeBatchData(reader);
        await reader.CloseAsync();
        var chargeBatchItems = await ListItemsAsync(connection, chargeBatchId, cancellationToken);
        return RestoreChargeBatch(chargeBatchData, chargeBatchItems);
    }

    public async Task<IReadOnlyCollection<ChargeBatch>> ListByBillingPeriodIdAsync(
        Guid billingPeriodId,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT id, billing_period_id, due_date, operator_id, retry_of_charge_batch_id,
                   asaas_environment, status, approved_by, approved_at, created_at, updated_at
            FROM charge_batches
            WHERE billing_period_id = @billingPeriodId
            ORDER BY created_at DESC;
            """;

        var chargeBatchDataItems = new List<ChargeBatchData>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using (var command = new MySqlCommand(commandText, connection))
        {
            command.Parameters.AddWithValue("@billingPeriodId", billingPeriodId.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                chargeBatchDataItems.Add(ReadChargeBatchData(reader));
            }
        }

        var chargeBatches = new List<ChargeBatch>();
        foreach (var chargeBatchData in chargeBatchDataItems)
        {
            var chargeBatchItems = await ListItemsAsync(connection, chargeBatchData.Id, cancellationToken);
            chargeBatches.Add(RestoreChargeBatch(chargeBatchData, chargeBatchItems));
        }

        return chargeBatches;
    }

    public async Task UpdateAsync(ChargeBatch chargeBatch, CancellationToken cancellationToken)
    {
        const string batchCommandText = """
            UPDATE charge_batches
            SET status = @status,
                approved_by = @approvedBy,
                approved_at = @approvedAt,
                updated_at = @updatedAt
            WHERE id = @id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var batchCommand = new MySqlCommand(batchCommandText, connection, transaction))
        {
            batchCommand.Parameters.AddWithValue("@id", chargeBatch.Id.ToString());
            batchCommand.Parameters.AddWithValue("@status", chargeBatch.Status.ToString());
            batchCommand.Parameters.AddWithValue("@approvedBy", chargeBatch.ApprovedBy ?? (object)DBNull.Value);
            batchCommand.Parameters.AddWithValue("@approvedAt", chargeBatch.ApprovedAt?.UtcDateTime ?? (object)DBNull.Value);
            batchCommand.Parameters.AddWithValue("@updatedAt", chargeBatch.UpdatedAt.UtcDateTime);
            await batchCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var chargeBatchItem in chargeBatch.Items)
        {
            await InsertOrUpdateItemAsync(connection, transaction, chargeBatch.Id, chargeBatchItem, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static void AddChargeBatchParameters(MySqlCommand command, ChargeBatch chargeBatch)
    {
        command.Parameters.AddWithValue("@id", chargeBatch.Id.ToString());
        command.Parameters.AddWithValue("@billingPeriodId", chargeBatch.BillingPeriodId.ToString());
        command.Parameters.AddWithValue("@dueDate", chargeBatch.DueDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@operatorId", chargeBatch.OperatorId);
        command.Parameters.AddWithValue("@retryOfChargeBatchId", chargeBatch.RetryOfChargeBatchId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@asaasEnvironment", chargeBatch.AsaasEnvironment.ToString());
        command.Parameters.AddWithValue("@status", chargeBatch.Status.ToString());
        command.Parameters.AddWithValue("@approvedBy", chargeBatch.ApprovedBy ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@approvedAt", chargeBatch.ApprovedAt?.UtcDateTime ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", chargeBatch.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAt", chargeBatch.UpdatedAt.UtcDateTime);
    }

    private static async Task InsertOrUpdateItemAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid chargeBatchId,
        ChargeBatchItem chargeBatchItem,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            INSERT INTO charge_batch_items
                (charge_batch_id, billing_draft_id, status, asaas_payment_id, bank_slip_url,
                 error_message, updated_at)
            VALUES
                (@chargeBatchId, @billingDraftId, @status, @asaasPaymentId, @bankSlipUrl,
                 @errorMessage, @updatedAt)
            ON DUPLICATE KEY UPDATE
                status = VALUES(status),
                asaas_payment_id = VALUES(asaas_payment_id),
                bank_slip_url = VALUES(bank_slip_url),
                error_message = VALUES(error_message),
                updated_at = VALUES(updated_at);
            """;

        await using var command = new MySqlCommand(commandText, connection, transaction);
        command.Parameters.AddWithValue("@chargeBatchId", chargeBatchId.ToString());
        command.Parameters.AddWithValue("@billingDraftId", chargeBatchItem.BillingDraftId.ToString());
        command.Parameters.AddWithValue("@status", chargeBatchItem.Status.ToString());
        command.Parameters.AddWithValue("@asaasPaymentId", chargeBatchItem.AsaasPaymentId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@bankSlipUrl", chargeBatchItem.BankSlipUrl ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@errorMessage", chargeBatchItem.ErrorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@updatedAt", chargeBatchItem.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyCollection<ChargeBatchItem>> ListItemsAsync(
        MySqlConnection connection,
        Guid chargeBatchId,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT billing_draft_id, status, asaas_payment_id, bank_slip_url, error_message, updated_at
            FROM charge_batch_items
            WHERE charge_batch_id = @chargeBatchId
            ORDER BY billing_draft_id;
            """;

        var chargeBatchItems = new List<ChargeBatchItem>();
        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@chargeBatchId", chargeBatchId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chargeBatchItems.Add(ChargeBatchItem.Restore(
                reader.GetGuid("billing_draft_id"),
                Enum.Parse<ChargeBatchItemStatus>(reader.GetString("status")),
                GetNullableString(reader, "asaas_payment_id"),
                GetNullableString(reader, "bank_slip_url"),
                GetNullableString(reader, "error_message"),
                GetUtcDateTime(reader, "updated_at")));
        }

        return chargeBatchItems;
    }

    private static ChargeBatchData ReadChargeBatchData(MySqlDataReader reader)
    {
        return new ChargeBatchData(
            reader.GetGuid("id"),
            reader.GetGuid("billing_period_id"),
            DateOnly.FromDateTime(reader.GetDateTime("due_date")),
            reader.GetString("operator_id"),
            GetNullableGuid(reader, "retry_of_charge_batch_id"),
            Enum.Parse<AsaasEnvironment>(reader.GetString("asaas_environment")),
            Enum.Parse<ChargeBatchStatus>(reader.GetString("status")),
            GetNullableString(reader, "approved_by"),
            GetNullableUtcDateTime(reader, "approved_at"),
            GetUtcDateTime(reader, "created_at"),
            GetUtcDateTime(reader, "updated_at"));
    }

    private static ChargeBatch RestoreChargeBatch(
        ChargeBatchData chargeBatchData,
        IReadOnlyCollection<ChargeBatchItem> chargeBatchItems)
    {
        return ChargeBatch.Restore(
            chargeBatchData.Id,
            chargeBatchData.BillingPeriodId,
            chargeBatchData.DueDate,
            chargeBatchData.OperatorId,
            chargeBatchData.AsaasEnvironment,
            chargeBatchData.RetryOfChargeBatchId,
            chargeBatchData.Status,
            chargeBatchItems,
            chargeBatchData.ApprovedBy,
            chargeBatchData.ApprovedAt,
            chargeBatchData.CreatedAt,
            chargeBatchData.UpdatedAt);
    }

    private static string? GetNullableString(MySqlDataReader reader, string columnName)
    {
        return reader.IsDBNull(reader.GetOrdinal(columnName)) ? null : reader.GetString(columnName);
    }

    private static Guid? GetNullableGuid(MySqlDataReader reader, string columnName)
    {
        return reader.IsDBNull(reader.GetOrdinal(columnName)) ? null : reader.GetGuid(columnName);
    }

    private static DateTimeOffset GetUtcDateTime(MySqlDataReader reader, string columnName)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(columnName), DateTimeKind.Utc));
    }

    private static DateTimeOffset? GetNullableUtcDateTime(MySqlDataReader reader, string columnName)
    {
        return reader.IsDBNull(reader.GetOrdinal(columnName)) ? null : GetUtcDateTime(reader, columnName);
    }

    private sealed record ChargeBatchData(
        Guid Id,
        Guid BillingPeriodId,
        DateOnly DueDate,
        string OperatorId,
        Guid? RetryOfChargeBatchId,
        AsaasEnvironment AsaasEnvironment,
        ChargeBatchStatus Status,
        string? ApprovedBy,
        DateTimeOffset? ApprovedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
