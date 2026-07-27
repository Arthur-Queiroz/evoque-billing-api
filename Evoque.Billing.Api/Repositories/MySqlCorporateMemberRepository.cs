using Evoque.Billing.Api.Domain;
using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class MySqlCorporateMemberRepository(MySqlConnectionFactory connectionFactory)
    : ICorporateMemberRepository
{
    public async Task<IReadOnlyCollection<CorporateMember>> ListAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var contractsByMemberId = await ReadContractsAsync(connection, cancellationToken);
        var corporateMembers = new List<CorporateMember>();
        await using var command = new MySqlCommand("""
            SELECT evo_member_id, member_name, company_tax_id, is_active,
                   first_seen_at, last_seen_at, deactivated_at, last_import_id,
                   updated_by, updated_at
            FROM corporate_members
            ORDER BY member_name, evo_member_id;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var evoMemberId = reader.GetInt64("evo_member_id");
            corporateMembers.Add(CorporateMember.Restore(
                evoMemberId,
                reader.GetString("member_name"),
                reader.GetString("company_tax_id"),
                contractsByMemberId.GetValueOrDefault(evoMemberId, []),
                reader.GetBoolean("is_active"),
                ReadTimestamp(reader, "first_seen_at"),
                ReadTimestamp(reader, "last_seen_at"),
                ReadNullableTimestamp(reader, "deactivated_at"),
                reader.GetGuid("last_import_id"),
                reader.GetString("updated_by"),
                ReadTimestamp(reader, "updated_at")));
        }

        return corporateMembers;
    }

    public async Task UpsertManyAsync(
        IReadOnlyCollection<CorporateMember> corporateMembers,
        CancellationToken cancellationToken)
    {
        if (corporateMembers.Count == 0)
        {
            return;
        }

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var corporateMember in corporateMembers)
        {
            await UpsertMemberAsync(connection, transaction, corporateMember, cancellationToken);
            await ReplaceContractsAsync(connection, transaction, corporateMember, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<Dictionary<long, IReadOnlyCollection<CorporateMemberContract>>>
        ReadContractsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var contractsByMemberId = new Dictionary<long, List<CorporateMemberContract>>();
        await using var command = new MySqlCommand("""
            SELECT evo_member_id, contract_key, evo_contract_id, contract_name
            FROM corporate_member_contracts
            ORDER BY evo_member_id, contract_name, contract_key;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var evoMemberId = reader.GetInt64("evo_member_id");
            if (!contractsByMemberId.TryGetValue(evoMemberId, out var contracts))
            {
                contracts = [];
                contractsByMemberId.Add(evoMemberId, contracts);
            }

            contracts.Add(new CorporateMemberContract(
                reader.GetString("contract_key"),
                ReadNullableString(reader, "evo_contract_id"),
                ReadNullableString(reader, "contract_name")));
        }

        return contractsByMemberId.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyCollection<CorporateMemberContract>)entry.Value);
    }

    private static async Task UpsertMemberAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CorporateMember corporateMember,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("""
            INSERT INTO corporate_members
                (evo_member_id, member_name, company_tax_id, is_active,
                 first_seen_at, last_seen_at, deactivated_at, last_import_id,
                 updated_by, updated_at)
            VALUES
                (@evoMemberId, @memberName, @companyTaxId, @isActive,
                 @firstSeenAt, @lastSeenAt, @deactivatedAt, @lastImportId,
                 @updatedBy, @updatedAt)
            ON DUPLICATE KEY UPDATE
                member_name = VALUES(member_name),
                company_tax_id = VALUES(company_tax_id),
                is_active = VALUES(is_active),
                last_seen_at = VALUES(last_seen_at),
                deactivated_at = VALUES(deactivated_at),
                last_import_id = VALUES(last_import_id),
                updated_by = VALUES(updated_by),
                updated_at = VALUES(updated_at);
            """, connection, transaction);
        command.Parameters.AddWithValue("@evoMemberId", corporateMember.EvoMemberId);
        command.Parameters.AddWithValue("@memberName", corporateMember.MemberName);
        command.Parameters.AddWithValue("@companyTaxId", corporateMember.CompanyTaxId);
        command.Parameters.AddWithValue("@isActive", corporateMember.IsActive);
        command.Parameters.AddWithValue("@firstSeenAt", corporateMember.FirstSeenAt.UtcDateTime);
        command.Parameters.AddWithValue("@lastSeenAt", corporateMember.LastSeenAt.UtcDateTime);
        command.Parameters.AddWithValue(
            "@deactivatedAt",
            (object?)corporateMember.DeactivatedAt?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastImportId", corporateMember.LastImportId);
        command.Parameters.AddWithValue("@updatedBy", corporateMember.UpdatedBy);
        command.Parameters.AddWithValue("@updatedAt", corporateMember.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceContractsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CorporateMember corporateMember,
        CancellationToken cancellationToken)
    {
        await using (var deleteCommand = new MySqlCommand(
            "DELETE FROM corporate_member_contracts WHERE evo_member_id = @evoMemberId;",
            connection,
            transaction))
        {
            deleteCommand.Parameters.AddWithValue("@evoMemberId", corporateMember.EvoMemberId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var contract in corporateMember.Contracts)
        {
            await using var insertCommand = new MySqlCommand("""
                INSERT INTO corporate_member_contracts
                    (evo_member_id, contract_key, evo_contract_id, contract_name)
                VALUES
                    (@evoMemberId, @contractKey, @evoContractId, @contractName);
                """, connection, transaction);
            insertCommand.Parameters.AddWithValue("@evoMemberId", corporateMember.EvoMemberId);
            insertCommand.Parameters.AddWithValue("@contractKey", contract.ContractKey);
            insertCommand.Parameters.AddWithValue(
                "@evoContractId",
                (object?)contract.EvoContractId ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue(
                "@contractName",
                (object?)contract.ContractName ?? DBNull.Value);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string? ReadNullableString(MySqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset ReadTimestamp(MySqlDataReader reader, string columnName)
    {
        return new DateTimeOffset(
            DateTime.SpecifyKind(reader.GetDateTime(columnName), DateTimeKind.Utc));
    }

    private static DateTimeOffset? ReadNullableTimestamp(
        MySqlDataReader reader,
        string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(
                DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    }
}
