using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Authentication;

/// <summary>
/// Responde se um usuário e uma senha conferem. Não conhece HTTP, cookie nem
/// <c>HttpContext</c>, e por isso é testável sem subir aplicação.
/// </summary>
public sealed class OperatorAuthenticationService(IOptions<OperatorAccountOptions> operatorAccountOptions)
{
    /// <summary>
    /// Devolve o nome configurado do operador, ou <c>null</c> quando não
    /// confere. O nome vem da configuração e não do que foi digitado, porque é
    /// ele que vai para a auditoria: "GEOVANNA" e "geovanna" precisam virar a
    /// mesma linha.
    /// </summary>
    public string? Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var operatorAccount = operatorAccountOptions.Value.Users.FirstOrDefault(account =>
            string.Equals(account.Username, username, StringComparison.OrdinalIgnoreCase));

        // A comparação acontece mesmo sem o usuário existir, contra um valor
        // descartável. Sair mais cedo aqui faria a resposta a um usuário
        // inexistente chegar mais rápido, e isso conta a quem está tentando
        // quais nomes valem a pena atacar — a mensagem é a mesma, o tempo não
        // seria.
        // O preenchimento acompanha o tamanho do que foi digitado, e não é
        // detalhe: `FixedTimeEquals` é constante só entre entradas do mesmo
        // tamanho — com tamanhos diferentes ele devolve `false` de saída. Contra
        // uma senha esperada vazia, o usuário inexistente respondia de 10 a 15
        // vezes mais rápido que uma senha errada do mesmo comprimento, que é
        // exatamente o que este trecho existe para evitar.
        var expectedPassword = operatorAccount?.Password ?? new string('\0', password.Length);
        var passwordMatches = FixedTimeEquals(expectedPassword, password);

        return operatorAccount is not null && passwordMatches ? operatorAccount.Username : null;
    }

    private static bool FixedTimeEquals(string expectedPassword, string providedPassword)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedPassword),
            Encoding.UTF8.GetBytes(providedPassword));
    }
}
