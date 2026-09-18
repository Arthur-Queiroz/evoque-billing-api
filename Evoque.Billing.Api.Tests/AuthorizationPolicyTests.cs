using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Evoque.Billing.Api.Tests;

/// <summary>
/// A política padrão não é exercitada por nenhum outro teste — os demais falam
/// com services, não por HTTP. Sem estes casos, perdê-la não quebraria nada e
/// ninguém notaria até o sistema estar aberto de novo.
/// </summary>
public sealed class AuthorizationPolicyTests(AuthenticatedApiFactory factory)
    : IClassFixture<AuthenticatedApiFactory>
{
    [Fact]
    public async Task BusinessEndpoint_RefusesAnAnonymousRequest()
    {
        using var httpClient = factory.CreateAuthenticatedHttpClient();

        var response = await httpClient.GetAsync("/api/companies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_StaysOpenSoTheContainerIsNotReportedDead()
    {
        using var httpClient = factory.CreateAuthenticatedHttpClient();

        var response = await httpClient.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignIn_IsReachableWithoutBeingSignedIn()
    {
        using var httpClient = factory.CreateAuthenticatedHttpClient();

        var response = await httpClient.PostAsJsonAsync(
            "/api/session",
            new { username = "teste", password = "errada" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// O caminho inteiro: entra, recebe o cookie, e então o mesmo endpoint que
    /// recusou a requisição anônima responde.
    /// </summary>
    [Fact]
    public async Task SignedInOperator_ReachesABusinessEndpoint()
    {
        using var httpClient = factory.CreateAuthenticatedHttpClient();

        var signInResponse = await httpClient.PostAsJsonAsync(
            "/api/session",
            new { username = "teste", password = AuthenticatedApiFactory.OperatorPassword });
        Assert.Equal(HttpStatusCode.OK, signInResponse.StatusCode);

        var response = await httpClient.GetAsync("/api/companies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignedOutOperator_IsRefusedAgain()
    {
        using var httpClient = factory.CreateAuthenticatedHttpClient();
        await httpClient.PostAsJsonAsync(
            "/api/session",
            new { username = "teste", password = AuthenticatedApiFactory.OperatorPassword });

        var signOutResponse = await httpClient.DeleteAsync("/api/session");
        Assert.Equal(HttpStatusCode.NoContent, signOutResponse.StatusCode);

        var response = await httpClient.GetAsync("/api/companies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>
/// Sobe a aplicação sem connection string, para que ela use os repositórios em
/// memória, e com um operador conhecido no ambiente.
/// </summary>
public sealed class AuthenticatedApiFactory : WebApplicationFactory<Program>
{
    public const string OperatorPassword = "senha-de-teste-16";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Users:0:Username"] = "teste",
                ["Auth:Users:0:Password"] = OperatorPassword,
                ["ConnectionStrings:BillingDatabase"] = "",
            });
        });

        return base.CreateHost(builder);
    }

    /// <summary>
    /// O cookie de sessão é sempre `Secure` (`CookieSecurePolicy.Always`, ver
    /// Program.cs, e por decisão deliberada — não é para ser afrouxada aqui).
    /// Comprovado: com o `HttpClient` padrão de `CreateClient()`, cuja
    /// `BaseAddress` é `http://localhost`, o login respondia 200 e emitia o
    /// cookie, mas o container de cookies do cliente nunca o reenviava na
    /// chamada seguinte — `/api/companies` voltava 401 mesmo autenticado, e
    /// `DELETE /api/session` também voltava 401 por não carregar o cookie.
    /// Apontar a `BaseAddress` para `https://` resolve isso sem tocar na
    /// política do cookie: o `TestServer` não fala TLS de fato, mas o
    /// `CookieContainer` do cliente decide guardar/reenviar o cookie a partir
    /// do esquema da URL, não de uma verificação real de certificado.
    /// </summary>
    public HttpClient CreateAuthenticatedHttpClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
    }
}
