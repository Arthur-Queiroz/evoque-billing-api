using Evoque.Billing.Api.Domain;
using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class MySqlCompanyCatalogImportRepository(MySqlConnectionFactory connectionFactory)
    : ICompanyCatalogImportRepository
{
    public async Task AddAsync(
        CompanyCatalogImport companyCatalogImport,
        IReadOnlyCollection<CompanyCatalogImportMember> importedMembers,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var importCommand = new MySqlCommand(
            """
            INSERT INTO company_catalog_imports
                (id, file_name, file_hash, operator_id, synchronized_at, analyzed_row_count,
                 discovered_company_count, created_company_count, updated_company_count,
                 unseen_company_count, warning_count)
            VALUES
                (@id, @fileName, @fileHash, @operatorId, @synchronizedAt, @analyzedRowCount,
                 @discoveredCompanyCount, @createdCompanyCount, @updatedCompanyCount,
                 @unseenCompanyCount, @warningCount);
            """,
            connection,
            transaction))
        {
            importCommand.Parameters.AddWithValue("@id", companyCatalogImport.Id);
            importCommand.Parameters.AddWithValue("@fileName", companyCatalogImport.FileName);
            importCommand.Parameters.AddWithValue("@fileHash", companyCatalogImport.FileHash);
            importCommand.Parameters.AddWithValue("@operatorId", companyCatalogImport.OperatorId);
            importCommand.Parameters.AddWithValue(
                "@synchronizedAt",
                companyCatalogImport.SynchronizedAt.UtcDateTime);
            importCommand.Parameters.AddWithValue("@analyzedRowCount", companyCatalogImport.AnalyzedRowCount);
            importCommand.Parameters.AddWithValue(
                "@discoveredCompanyCount",
                companyCatalogImport.DiscoveredCompanyCount);
            importCommand.Parameters.AddWithValue(
                "@createdCompanyCount",
                companyCatalogImport.CreatedCompanyCount);
            importCommand.Parameters.AddWithValue(
                "@updatedCompanyCount",
                companyCatalogImport.UpdatedCompanyCount);
            importCommand.Parameters.AddWithValue(
                "@unseenCompanyCount",
                companyCatalogImport.UnseenCompanyCount);
            importCommand.Parameters.AddWithValue("@warningCount", companyCatalogImport.WarningCount);
            await importCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var importedMember in importedMembers)
        {
            await using var memberCommand = new MySqlCommand(
                """
                INSERT INTO company_catalog_import_members
                    (import_id, company_tax_id, source_row_number, member_name, contract_name)
                VALUES
                    (@importId, @companyTaxId, @sourceRowNumber, @memberName, @contractName);
                """,
                connection,
                transaction);
            memberCommand.Parameters.AddWithValue("@importId", importedMember.ImportId);
            memberCommand.Parameters.AddWithValue("@companyTaxId", importedMember.CompanyTaxId);
            memberCommand.Parameters.AddWithValue("@sourceRowNumber", importedMember.SourceRowNumber);
            memberCommand.Parameters.AddWithValue("@memberName", importedMember.MemberName);
            memberCommand.Parameters.AddWithValue(
                "@contractName",
                (object?)importedMember.ContractName ?? DBNull.Value);
            await memberCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CompanyCatalogImport?> FindLatestAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            """
            SELECT id, file_name, file_hash, operator_id, synchronized_at, analyzed_row_count,
                   discovered_company_count, created_company_count, updated_company_count,
                   unseen_company_count, warning_count
            FROM company_catalog_imports
            ORDER BY synchronized_at DESC
            LIMIT 1;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CompanyCatalogImport(
            reader.GetGuid("id"),
            reader.GetString("file_name"),
            reader.GetString("file_hash"),
            reader.GetString("operator_id"),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime("synchronized_at"), DateTimeKind.Utc)),
            reader.GetInt32("analyzed_row_count"),
            reader.GetInt32("discovered_company_count"),
            reader.GetInt32("created_company_count"),
            reader.GetInt32("updated_company_count"),
            reader.GetInt32("unseen_company_count"),
            reader.GetInt32("warning_count"));
    }

    public async Task<IReadOnlyCollection<CompanyCatalogImportMember>> ListLatestMembersByCompanyAsync(
        string companyTaxId,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT members.import_id, members.company_tax_id, members.source_row_number,
                   members.member_name, members.contract_name
            FROM company_catalog_import_members AS members
            WHERE members.company_tax_id = @companyTaxId
              AND members.import_id = (
                  SELECT latest.import_id
                  FROM company_catalog_import_members AS latest
                  INNER JOIN company_catalog_imports AS imports ON imports.id = latest.import_id
                  WHERE latest.company_tax_id = @companyTaxId
                  ORDER BY imports.synchronized_at DESC
                  LIMIT 1
              )
            ORDER BY members.member_name;
            """;

        var members = new List<CompanyCatalogImportMember>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(commandText, connection);
        command.Parameters.AddWithValue("@companyTaxId", companyTaxId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var contractNameOrdinal = reader.GetOrdinal("contract_name");
            members.Add(new CompanyCatalogImportMember(
                reader.GetGuid("import_id"),
                reader.GetString("company_tax_id"),
                reader.GetInt32("source_row_number"),
                reader.GetString("member_name"),
                reader.IsDBNull(contractNameOrdinal) ? null : reader.GetString(contractNameOrdinal)));
        }

        return members;
    }
}
