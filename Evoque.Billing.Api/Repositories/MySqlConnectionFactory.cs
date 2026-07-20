using MySqlConnector;

namespace Evoque.Billing.Api.Repositories;

public sealed class MySqlConnectionFactory(string connectionString)
{
    public MySqlConnection CreateConnection()
    {
        return new MySqlConnection(connectionString);
    }
}
