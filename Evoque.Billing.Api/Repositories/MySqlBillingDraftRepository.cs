using Evoque.Billing.Api.Domain;
using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class MySqlBillingDraftRepository(MySqlConnectionFactory connectionFactory) : IBillingDraftRepository
{
    public async Task AddAsync(BillingDraft billingDraft, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string draftCommandText = """
            INSERT INTO billing_drafts
                (id, billing_period_id, external_company_id, company_name, company_tax_id,
                 asaas_customer_id, status, version, approved_by, approved_at, asaas_payment_id,
                 bank_slip_url, created_at, updated_at)
            VALUES
                (@id, @billingPeriodId, @externalCompanyId, @companyName, @companyTaxId,
                 @asaasCustomerId, @status, @version, @approvedBy, @approvedAt, @asaasPaymentId,
                 @bankSlipUrl, @createdAt, @updatedAt);
            """;

        await using (var draftCommand = new MySqlCommand(draftCommandText, connection, transaction))
        {
            AddDraftParameters(draftCommand, billingDraft);
            await draftCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertItemsAsync(connection, transaction, billingDraft, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<BillingDraft?> FindByIdAsync(Guid billingDraftId, CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT id, billing_period_id, external_company_id, company_name, company_tax_id,
                   asaas_customer_id, status, version, approved_by, approved_at, asaas_payment_id,
                   bank_slip_url, created_at, updated_at
            FROM billing_drafts
            WHERE id = @id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@id", billingDraftId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var billingDraftData = ReadBillingDraftData(reader);
        await reader.CloseAsync();
        var items = await ListItemsAsync(connection, billingDraftId, cancellationToken);
        return RestoreBillingDraft(billingDraftData, items);
    }

    public async Task<IReadOnlyCollection<BillingDraft>> ListByBillingPeriodIdAsync(
        Guid billingPeriodId,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT id, billing_period_id, external_company_id, company_name, company_tax_id,
                   asaas_customer_id, status, version, approved_by, approved_at, asaas_payment_id,
                   bank_slip_url, created_at, updated_at
            FROM billing_drafts
            WHERE billing_period_id = @billingPeriodId
            ORDER BY company_name;
            """;

        var billingDraftDataItems = new List<BillingDraftData>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using (var command = new MySqlCommand(commandText, connection))
        {
            command.Parameters.AddWithValue("@billingPeriodId", billingPeriodId.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                billingDraftDataItems.Add(ReadBillingDraftData(reader));
            }
        }

        var billingDrafts = new List<BillingDraft>();
        foreach (var billingDraftData in billingDraftDataItems)
        {
            var items = await ListItemsAsync(connection, billingDraftData.Id, cancellationToken);
            billingDrafts.Add(RestoreBillingDraft(billingDraftData, items));
        }

        return billingDrafts;
    }

    public async Task<IReadOnlyCollection<BillingDraft>> ListByExternalCompanyIdAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT id, billing_period_id, external_company_id, company_name, company_tax_id,
                   asaas_customer_id, status, version, approved_by, approved_at, asaas_payment_id,
                   bank_slip_url, created_at, updated_at
            FROM billing_drafts
            WHERE external_company_id = @externalCompanyId
            ORDER BY created_at DESC;
            """;

        var billingDraftDataItems = new List<BillingDraftData>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using (var command = new MySqlCommand(commandText, connection))
        {
            command.Parameters.AddWithValue("@externalCompanyId", externalCompanyId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                billingDraftDataItems.Add(ReadBillingDraftData(reader));
            }
        }

        var billingDrafts = new List<BillingDraft>();
        foreach (var billingDraftData in billingDraftDataItems)
        {
            var items = await ListItemsAsync(connection, billingDraftData.Id, cancellationToken);
            billingDrafts.Add(RestoreBillingDraft(billingDraftData, items));
        }

        return billingDrafts;
    }

    public async Task UpdateAsync(BillingDraft billingDraft, CancellationToken cancellationToken)
    {
        const string commandText = """
            UPDATE billing_drafts
            SET status = @status,
                approved_by = @approvedBy,
                approved_at = @approvedAt,
                asaas_payment_id = @asaasPaymentId,
                bank_slip_url = @bankSlipUrl,
                updated_at = @updatedAt
            WHERE id = @id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@id", billingDraft.Id.ToString());
        command.Parameters.AddWithValue("@status", billingDraft.Status.ToString());
        command.Parameters.AddWithValue("@approvedBy", billingDraft.ApprovedBy ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@approvedAt", billingDraft.ApprovedAt?.UtcDateTime ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@asaasPaymentId", billingDraft.AsaasPaymentId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@bankSlipUrl", billingDraft.BankSlipUrl ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@updatedAt", billingDraft.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddDraftParameters(MySqlCommand command, BillingDraft billingDraft)
    {
        command.Parameters.AddWithValue("@id", billingDraft.Id.ToString());
        command.Parameters.AddWithValue("@billingPeriodId", billingDraft.BillingPeriodId.ToString());
        command.Parameters.AddWithValue("@externalCompanyId", billingDraft.ExternalCompanyId);
        command.Parameters.AddWithValue("@companyName", billingDraft.CompanyName);
        command.Parameters.AddWithValue("@companyTaxId", billingDraft.CompanyTaxId);
        command.Parameters.AddWithValue("@asaasCustomerId", billingDraft.AsaasCustomerId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@status", billingDraft.Status.ToString());
        command.Parameters.AddWithValue("@version", billingDraft.Version);
        command.Parameters.AddWithValue("@approvedBy", billingDraft.ApprovedBy ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@approvedAt", billingDraft.ApprovedAt?.UtcDateTime ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@asaasPaymentId", billingDraft.AsaasPaymentId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@bankSlipUrl", billingDraft.BankSlipUrl ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", billingDraft.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("@updatedAt", billingDraft.UpdatedAt.UtcDateTime);
    }

    private static async Task InsertItemsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        BillingDraft billingDraft,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            INSERT INTO billing_draft_items
                (id, billing_draft_id, item_order, description, quantity, unit_amount, external_member_id)
            VALUES
                (@id, @billingDraftId, @itemOrder, @description, @quantity, @unitAmount, @externalMemberId);
            """;

        var itemOrder = 0;
        foreach (var item in billingDraft.Items)
        {
            await using var command = new MySqlCommand(commandText, connection, transaction);
            command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("@billingDraftId", billingDraft.Id.ToString());
            command.Parameters.AddWithValue("@itemOrder", itemOrder++);
            command.Parameters.AddWithValue("@description", item.Description);
            command.Parameters.AddWithValue("@quantity", item.Quantity);
            command.Parameters.AddWithValue("@unitAmount", item.UnitAmount);
            command.Parameters.AddWithValue("@externalMemberId", item.ExternalMemberId ?? (object)DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyCollection<BillingDraftItem>> ListItemsAsync(
        MySqlConnection connection,
        Guid billingDraftId,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT description, quantity, unit_amount, external_member_id
            FROM billing_draft_items
            WHERE billing_draft_id = @billingDraftId
            ORDER BY item_order;
            """;

        var items = new List<BillingDraftItem>();
        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@billingDraftId", billingDraftId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new BillingDraftItem(
                reader.GetString("description"),
                reader.GetDecimal("quantity"),
                reader.GetDecimal("unit_amount"),
                reader.IsDBNull(reader.GetOrdinal("external_member_id"))
                    ? null
                    : reader.GetString("external_member_id")));
        }

        return items;
    }

    private static BillingDraftData ReadBillingDraftData(MySqlDataReader reader)
    {
        return new BillingDraftData(
            Guid.Parse(reader.GetString("id")),
            Guid.Parse(reader.GetString("billing_period_id")),
            reader.GetString("external_company_id"),
            reader.GetString("company_name"),
            reader.GetString("company_tax_id"),
            GetNullableString(reader, "asaas_customer_id"),
            Enum.Parse<BillingDraftStatus>(reader.GetString("status")),
            reader.GetInt32("version"),
            GetNullableString(reader, "approved_by"),
            GetNullableUtcDateTime(reader, "approved_at"),
            GetNullableString(reader, "asaas_payment_id"),
            GetNullableString(reader, "bank_slip_url"),
            GetUtcDateTime(reader, "created_at"),
            GetUtcDateTime(reader, "updated_at"));
    }

    private static BillingDraft RestoreBillingDraft(
        BillingDraftData billingDraftData,
        IReadOnlyCollection<BillingDraftItem> items)
    {
        return BillingDraft.Restore(
            billingDraftData.Id,
            billingDraftData.BillingPeriodId,
            billingDraftData.ExternalCompanyId,
            billingDraftData.CompanyName,
            billingDraftData.CompanyTaxId,
            billingDraftData.AsaasCustomerId,
            items,
            billingDraftData.Status,
            billingDraftData.Version,
            billingDraftData.ApprovedBy,
            billingDraftData.ApprovedAt,
            billingDraftData.AsaasPaymentId,
            billingDraftData.BankSlipUrl,
            billingDraftData.CreatedAt,
            billingDraftData.UpdatedAt);
    }

    private static string? GetNullableString(MySqlDataReader reader, string columnName)
    {
        return reader.IsDBNull(reader.GetOrdinal(columnName)) ? null : reader.GetString(columnName);
    }

    private static DateTimeOffset? GetNullableUtcDateTime(MySqlDataReader reader, string columnName)
    {
        return reader.IsDBNull(reader.GetOrdinal(columnName)) ? null : GetUtcDateTime(reader, columnName);
    }

    private static DateTimeOffset GetUtcDateTime(MySqlDataReader reader, string columnName)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(columnName), DateTimeKind.Utc));
    }

    private sealed record BillingDraftData(
        Guid Id,
        Guid BillingPeriodId,
        string ExternalCompanyId,
        string CompanyName,
        string CompanyTaxId,
        string? AsaasCustomerId,
        BillingDraftStatus Status,
        int Version,
        string? ApprovedBy,
        DateTimeOffset? ApprovedAt,
        string? AsaasPaymentId,
        string? BankSlipUrl,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
