using Evoque.Billing.Api.Repositories;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Evoque.Billing.Api.Services;

public sealed class BillingDatabaseHealthCheck(IServiceProvider serviceProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionFactory = serviceProvider.GetService<MySqlConnectionFactory>();
        if (connectionFactory is null)
        {
            return HealthCheckResult.Healthy("A API está usando o armazenamento em memória.");
        }

        try
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("A conexão com o banco está disponível.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Não foi possível consultar o banco de faturamento.", exception);
        }
    }
}
