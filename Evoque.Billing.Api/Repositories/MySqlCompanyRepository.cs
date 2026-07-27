using Evoque.Billing.Api.Domain;
using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class MySqlCompanyRepository(MySqlConnectionFactory connectionFactory) : ICompanyRepository
{
    private const string SelectColumns = """
        SELECT tax_id, display_name, evo_name, legal_name, trade_name, registration_status,
               registry_street, registry_number, registry_complement, registry_neighborhood,
               registry_city, registry_state, registry_postal_code,
               registry_lookup_status, registry_last_checked_at,
               is_active, source, last_imported_member_count, first_seen_at, last_seen_at,
               last_import_id, requires_review_after_reappearing,
               asaas_sandbox_customer_id, asaas_production_customer_id,
               created_by, created_at, updated_by, updated_at
        FROM companies
        """;

    public async Task<Company?> FindByTaxIdAsync(string taxId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand($"{SelectColumns} WHERE tax_id = @taxId;", connection);
        command.Parameters.AddWithValue("@taxId", taxId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCompany(reader) : null;
    }

    public async Task<IReadOnlyCollection<Company>> ListAsync(CancellationToken cancellationToken)
    {
        var companies = new List<Company>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"{SelectColumns} ORDER BY display_name, tax_id;",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            companies.Add(ReadCompany(reader));
        }

        return companies;
    }

    public async Task UpsertAsync(Company company, CancellationToken cancellationToken)
    {
        const string commandText = """
            INSERT INTO companies
                (tax_id, display_name, evo_name, legal_name, trade_name, registration_status,
                 registry_street, registry_number, registry_complement, registry_neighborhood,
                 registry_city, registry_state, registry_postal_code,
                 registry_lookup_status, registry_last_checked_at,
                 is_active, source, last_imported_member_count, first_seen_at, last_seen_at,
                 last_import_id, requires_review_after_reappearing,
                 asaas_sandbox_customer_id, asaas_production_customer_id,
                 created_by, created_at, updated_by, updated_at)
            VALUES
                (@taxId, @displayName, @evoName, @legalName, @tradeName, @registrationStatus,
                 @registryStreet, @registryNumber, @registryComplement, @registryNeighborhood,
                 @registryCity, @registryState, @registryPostalCode,
                 @registryLookupStatus, @registryLastCheckedAt,
                 @isActive, @source, @lastImportedMemberCount, @firstSeenAt, @lastSeenAt,
                 @lastImportId, @requiresReviewAfterReappearing,
                 @asaasSandboxCustomerId, @asaasProductionCustomerId,
                 @createdBy, @createdAt, @updatedBy, @updatedAt)
            ON DUPLICATE KEY UPDATE
                display_name = VALUES(display_name),
                evo_name = VALUES(evo_name),
                legal_name = VALUES(legal_name),
                trade_name = VALUES(trade_name),
                registration_status = VALUES(registration_status),
                registry_street = VALUES(registry_street),
                registry_number = VALUES(registry_number),
                registry_complement = VALUES(registry_complement),
                registry_neighborhood = VALUES(registry_neighborhood),
                registry_city = VALUES(registry_city),
                registry_state = VALUES(registry_state),
                registry_postal_code = VALUES(registry_postal_code),
                registry_lookup_status = VALUES(registry_lookup_status),
                registry_last_checked_at = VALUES(registry_last_checked_at),
                is_active = VALUES(is_active),
                source = VALUES(source),
                last_imported_member_count = VALUES(last_imported_member_count),
                first_seen_at = VALUES(first_seen_at),
                last_seen_at = VALUES(last_seen_at),
                last_import_id = VALUES(last_import_id),
                requires_review_after_reappearing = VALUES(requires_review_after_reappearing),
                asaas_sandbox_customer_id = VALUES(asaas_sandbox_customer_id),
                asaas_production_customer_id = VALUES(asaas_production_customer_id),
                updated_by = VALUES(updated_by),
                updated_at = VALUES(updated_at);
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        AddParameters(command, company);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(MySqlCommand command, Company company)
    {
        command.Parameters.AddWithValue("@taxId", company.TaxId);
        command.Parameters.AddWithValue("@displayName", company.DisplayName);
        command.Parameters.AddWithValue("@evoName", (object?)company.EvoName ?? DBNull.Value);
        command.Parameters.AddWithValue("@legalName", (object?)company.LegalName ?? DBNull.Value);
        command.Parameters.AddWithValue("@tradeName", (object?)company.TradeName ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@registrationStatus",
            (object?)company.RegistrationStatus ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@registryStreet",
            (object?)company.RegistryAddress?.Street ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@registryNumber",
            (object?)company.RegistryAddress?.Number ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@registryComplement",
            (object?)company.RegistryAddress?.Complement ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@registryNeighborhood",
            (object?)company.RegistryAddress?.Neighborhood ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@registryCity",
            (object?)company.RegistryAddress?.City ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@registryState",
            (object?)company.RegistryAddress?.State ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@registryPostalCode",
            (object?)company.RegistryAddress?.PostalCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@registryLookupStatus", company.RegistryLookupStatus.ToString());
        command.Parameters.AddWithValue(
            "@registryLastCheckedAt",
            (object?)company.RegistryLastCheckedAt?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@isActive", company.IsActive);
        command.Parameters.AddWithValue("@source", company.Source.ToString());
        command.Parameters.AddWithValue("@lastImportedMemberCount", company.LastImportedMemberCount);
        command.Parameters.AddWithValue("@firstSeenAt", (object?)company.FirstSeenAt?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastSeenAt", (object?)company.LastSeenAt?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastImportId", (object?)company.LastImportId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@requiresReviewAfterReappearing",
            company.RequiresReviewAfterReappearing);
        command.Parameters.AddWithValue(
            "@asaasSandboxCustomerId",
            (object?)company.AsaasSandboxCustomerId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@asaasProductionCustomerId",
            (object?)company.AsaasProductionCustomerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdBy", company.CreatedBy);
        command.Parameters.AddWithValue("@createdAt", company.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("@updatedBy", company.UpdatedBy);
        command.Parameters.AddWithValue("@updatedAt", company.UpdatedAt.UtcDateTime);
    }

    private static Company ReadCompany(MySqlDataReader reader)
    {
        return Company.Restore(
            reader.GetString("tax_id"),
            reader.GetString("display_name"),
            ReadNullableString(reader, "evo_name"),
            ReadNullableString(reader, "legal_name"),
            ReadNullableString(reader, "trade_name"),
            ReadNullableString(reader, "registration_status"),
            ReadRegistryAddress(reader),
            Enum.Parse<CompanyRegistryLookupStatus>(reader.GetString("registry_lookup_status")),
            ReadNullableTimestamp(reader, "registry_last_checked_at"),
            reader.GetBoolean("is_active"),
            Enum.Parse<CompanySource>(reader.GetString("source")),
            reader.GetInt32("last_imported_member_count"),
            ReadNullableTimestamp(reader, "first_seen_at"),
            ReadNullableTimestamp(reader, "last_seen_at"),
            reader.IsDBNull(reader.GetOrdinal("last_import_id"))
                ? null
                : reader.GetGuid("last_import_id"),
            reader.GetBoolean("requires_review_after_reappearing"),
            ReadNullableString(reader, "asaas_sandbox_customer_id"),
            ReadNullableString(reader, "asaas_production_customer_id"),
            reader.GetString("created_by"),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc)),
            reader.GetString("updated_by"),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)));
    }

    /// <summary>
    /// O endereço só existe quando o cadastro público já respondeu. A cidade é
    /// usada como sentinela porque nenhum endereço retornado vem sem município.
    /// </summary>
    private static CompanyRegistryAddress? ReadRegistryAddress(MySqlDataReader reader)
    {
        var city = ReadNullableString(reader, "registry_city");
        if (city is null)
        {
            return null;
        }

        return new CompanyRegistryAddress(
            ReadNullableString(reader, "registry_street") ?? string.Empty,
            ReadNullableString(reader, "registry_number") ?? string.Empty,
            ReadNullableString(reader, "registry_complement") ?? string.Empty,
            ReadNullableString(reader, "registry_neighborhood") ?? string.Empty,
            city,
            ReadNullableString(reader, "registry_state") ?? string.Empty,
            ReadNullableString(reader, "registry_postal_code") ?? string.Empty);
    }

    private static string? ReadNullableString(MySqlDataReader reader, string columnName)
    {
        var columnOrdinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(columnOrdinal) ? null : reader.GetString(columnOrdinal);
    }

    private static DateTimeOffset? ReadNullableTimestamp(MySqlDataReader reader, string columnName)
    {
        var columnOrdinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(columnOrdinal)
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(columnOrdinal), DateTimeKind.Utc));
    }
}
