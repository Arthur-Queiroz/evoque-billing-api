using Evoque.Billing.Api.Domain;
using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class MySqlFiscalInvoiceRepository(MySqlConnectionFactory connectionFactory) : IFiscalInvoiceRepository
{
    private const string SelectColumns = """
        SELECT id, billing_draft_id, billing_period_id, sequence_number, asaas_payment_id,
               asaas_invoice_id, status, value, effective_date, retains_iss, service_description,
               error_message, created_at, updated_at
        FROM fiscal_invoices
        """;

    public async Task AddAsync(FiscalInvoice fiscalInvoice, CancellationToken cancellationToken)
    {
        const string commandText = """
            INSERT INTO fiscal_invoices
                (id, billing_draft_id, billing_period_id, sequence_number, asaas_payment_id,
                 asaas_invoice_id, status, value, effective_date, retains_iss, service_description,
                 error_message, created_at, updated_at)
            VALUES
                (@id, @billingDraftId, @billingPeriodId, @sequenceNumber, @asaasPaymentId,
                 @asaasInvoiceId, @status, @value, @effectiveDate, @retainsIss, @serviceDescription,
                 @errorMessage, @createdAt, @updatedAt);
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        AddParameters(command, fiscalInvoice);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (MySqlException mySqlException) when (mySqlException.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            // A chave única (billing_draft_id, sequence_number) é a defesa final
            // contra nota duplicada na prefeitura. Traduzir para ConflictException
            // evita vazar MySqlException para o service e mantém o mesmo contrato
            // do InMemoryFiscalInvoiceRepository.
            throw new ConflictException("Já existe uma nota fiscal com essa sequência para a prévia.");
        }
    }

    public async Task UpdateAsync(FiscalInvoice fiscalInvoice, CancellationToken cancellationToken)
    {
        const string commandText = """
            UPDATE fiscal_invoices
            SET asaas_invoice_id = @asaasInvoiceId,
                status = @status,
                error_message = @errorMessage,
                updated_at = @updatedAt
            WHERE id = @id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@id", fiscalInvoice.Id.ToString());
        command.Parameters.AddWithValue("@asaasInvoiceId", (object?)fiscalInvoice.AsaasInvoiceId ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", fiscalInvoice.Status.ToString());
        command.Parameters.AddWithValue("@errorMessage", (object?)fiscalInvoice.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("@updatedAt", fiscalInvoice.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<FiscalInvoice?> FindByIdAsync(Guid fiscalInvoiceId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand($"{SelectColumns} WHERE id = @id;", connection);
        command.Parameters.AddWithValue("@id", fiscalInvoiceId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadFiscalInvoice(reader) : null;
    }

    public async Task<IReadOnlyCollection<FiscalInvoice>> ListByBillingDraftIdAsync(
        Guid billingDraftId,
        CancellationToken cancellationToken)
    {
        var fiscalInvoices = new List<FiscalInvoice>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"{SelectColumns} WHERE billing_draft_id = @billingDraftId ORDER BY sequence_number;",
            connection);
        command.Parameters.AddWithValue("@billingDraftId", billingDraftId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            fiscalInvoices.Add(ReadFiscalInvoice(reader));
        }

        return fiscalInvoices;
    }

    public async Task<IReadOnlyCollection<FiscalInvoice>> ListByBillingPeriodIdAsync(
        Guid billingPeriodId,
        CancellationToken cancellationToken)
    {
        var fiscalInvoices = new List<FiscalInvoice>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"{SelectColumns} WHERE billing_period_id = @billingPeriodId ORDER BY created_at DESC;",
            connection);
        command.Parameters.AddWithValue("@billingPeriodId", billingPeriodId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            fiscalInvoices.Add(ReadFiscalInvoice(reader));
        }

        return fiscalInvoices;
    }

    private static void AddParameters(MySqlCommand command, FiscalInvoice fiscalInvoice)
    {
        command.Parameters.AddWithValue("@id", fiscalInvoice.Id.ToString());
        command.Parameters.AddWithValue("@billingDraftId", fiscalInvoice.BillingDraftId.ToString());
        command.Parameters.AddWithValue("@billingPeriodId", fiscalInvoice.BillingPeriodId.ToString());
        command.Parameters.AddWithValue("@sequenceNumber", fiscalInvoice.Sequence);
        command.Parameters.AddWithValue("@asaasPaymentId", fiscalInvoice.AsaasPaymentId);
        command.Parameters.AddWithValue("@asaasInvoiceId", (object?)fiscalInvoice.AsaasInvoiceId ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", fiscalInvoice.Status.ToString());
        command.Parameters.AddWithValue("@value", fiscalInvoice.TotalAmount);
        command.Parameters.AddWithValue("@effectiveDate", fiscalInvoice.EffectiveDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@retainsIss", fiscalInvoice.RetainsIss);
        command.Parameters.AddWithValue("@serviceDescription", fiscalInvoice.ServiceDescription);
        command.Parameters.AddWithValue("@errorMessage", (object?)fiscalInvoice.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", fiscalInvoice.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAt", fiscalInvoice.UpdatedAt.UtcDateTime);
    }

    private static FiscalInvoice ReadFiscalInvoice(MySqlDataReader reader)
    {
        return FiscalInvoice.Restore(
            reader.GetGuid("id"),
            reader.GetGuid("billing_draft_id"),
            reader.GetGuid("billing_period_id"),
            reader.GetInt32("sequence_number"),
            reader.GetString("asaas_payment_id"),
            GetNullableString(reader, "asaas_invoice_id"),
            Enum.Parse<FiscalInvoiceStatus>(reader.GetString("status")),
            reader.GetDecimal("value"),
            DateOnly.FromDateTime(reader.GetDateTime("effective_date")),
            reader.GetBoolean("retains_iss"),
            reader.GetString("service_description"),
            GetNullableString(reader, "error_message"),
            GetUtcDateTime(reader, "created_at"),
            GetUtcDateTime(reader, "updated_at"));
    }

    private static string? GetNullableString(MySqlDataReader reader, string columnName)
    {
        return reader.IsDBNull(reader.GetOrdinal(columnName)) ? null : reader.GetString(columnName);
    }

    private static DateTimeOffset GetUtcDateTime(MySqlDataReader reader, string columnName)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(columnName), DateTimeKind.Utc));
    }
}
