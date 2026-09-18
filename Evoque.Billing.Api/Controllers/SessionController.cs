using System.Security.Claims;
using Evoque.Billing.Api.Authentication;
using Evoque.Billing.Api.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Evoque.Billing.Api.Controllers;

/// <summary>
/// Entrar, sair e saber quem está logado. É a camada temporária: quando a
/// autenticação passar para o Azure, este controller inteiro deixa de existir.
/// </summary>
[ApiController]
[Route("api/session")]
public sealed class SessionController(
    OperatorAuthenticationService operatorAuthenticationService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> SignInAsync(SignInRequest request)
    {
        var operatorId = operatorAuthenticationService.Authenticate(request.Username, request.Password);
        if (operatorId is null)
        {
            // Uma mensagem só para os dois casos. Dizer "usuário não existe"
            // entregaria a lista de nomes válidos a quem está tentando.
            return Unauthorized(new { error = "Usuário ou senha inválidos." });
        }

        var claimsIdentity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, operatorId)],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity));

        return Ok(new SessionResponse(operatorId));
    }

    [HttpGet]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<SessionResponse> Get()
    {
        return Ok(new SessionResponse(User.GetOperatorId()));
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SignOutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}
