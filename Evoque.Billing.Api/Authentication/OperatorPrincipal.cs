using System.Security.Claims;

namespace Evoque.Billing.Api.Authentication;

/// <summary>
/// De onde o controller lê quem está agindo. Existe para que essa leitura tenha
/// um lugar só: no dia em que a identidade vier do Azure, o que muda é o que
/// preenche o <see cref="ClaimsPrincipal"/>, não os controllers.
/// </summary>
public static class OperatorPrincipal
{
    public static string GetOperatorId(this ClaimsPrincipal principal)
    {
        return principal.Identity?.Name
            ?? throw new InvalidOperationException(
                "Requisição autenticada sem nome de operador. A política padrão deveria ter barrado.");
    }
}
