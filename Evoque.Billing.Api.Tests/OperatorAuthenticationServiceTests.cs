using Evoque.Billing.Api.Authentication;
using Microsoft.Extensions.Options;

namespace Evoque.Billing.Api.Tests;

public sealed class OperatorAuthenticationServiceTests
{
    [Fact]
    public void Authenticate_AcceptsTheConfiguredPassword()
    {
        var service = CreateService(("geovanna", "k7Qx2mVp9RtLw4Ha"));

        var authenticatedOperator = service.Authenticate("geovanna", "k7Qx2mVp9RtLw4Ha");

        Assert.Equal("geovanna", authenticatedOperator);
    }

    [Fact]
    public void Authenticate_RejectsTheWrongPassword()
    {
        var service = CreateService(("geovanna", "k7Qx2mVp9RtLw4Ha"));

        Assert.Null(service.Authenticate("geovanna", "outra-senha"));
    }

    [Fact]
    public void Authenticate_RejectsAnUnknownUser()
    {
        var service = CreateService(("geovanna", "k7Qx2mVp9RtLw4Ha"));

        Assert.Null(service.Authenticate("ninguem", "k7Qx2mVp9RtLw4Ha"));
    }

    /// <summary>
    /// O usuário é comparado sem diferenciar maiúsculas porque ninguém decora a
    /// caixa do próprio login, e errá-la não é tentativa de invasão. A senha,
    /// essa, diferencia.
    /// </summary>
    [Fact]
    public void Authenticate_IgnoresTheCaseOfTheUsernameButNotOfThePassword()
    {
        var service = CreateService(("geovanna", "k7Qx2mVp9RtLw4Ha"));

        Assert.Equal("geovanna", service.Authenticate("GEOVANNA", "k7Qx2mVp9RtLw4Ha"));
        Assert.Null(service.Authenticate("geovanna", "K7QX2MVP9RTLW4HA"));
    }

    /// <summary>
    /// O nome devolvido é o que está configurado, não o que foi digitado. É ele
    /// que vai para a auditoria, e "GEOVANNA" e "geovanna" precisam virar a
    /// mesma linha.
    /// </summary>
    [Fact]
    public void Authenticate_ReturnsTheConfiguredNameNotTheTypedOne()
    {
        var service = CreateService(("geovanna", "k7Qx2mVp9RtLw4Ha"));

        Assert.Equal("geovanna", service.Authenticate("GeoVanna", "k7Qx2mVp9RtLw4Ha"));
    }

    [Fact]
    public void Authenticate_RejectsAnEmptyUsernameOrPassword()
    {
        var service = CreateService(("geovanna", "k7Qx2mVp9RtLw4Ha"));

        Assert.Null(service.Authenticate("", "k7Qx2mVp9RtLw4Ha"));
        Assert.Null(service.Authenticate("geovanna", ""));
    }

    [Fact]
    public void Authenticate_TellsEachConfiguredOperatorApart()
    {
        var service = CreateService(
            ("geovanna", "k7Qx2mVp9RtLw4Ha"),
            ("arthur", "Zp3nB8yTq6WsVe1C"));

        Assert.Equal("arthur", service.Authenticate("arthur", "Zp3nB8yTq6WsVe1C"));
        Assert.Null(service.Authenticate("arthur", "k7Qx2mVp9RtLw4Ha"));
    }

    private static OperatorAuthenticationService CreateService(
        params (string Username, string Password)[] operatorAccounts)
    {
        var options = new OperatorAccountOptions
        {
            Users = operatorAccounts
                .Select(account => new OperatorAccount
                {
                    Username = account.Username,
                    Password = account.Password,
                })
                .ToArray(),
        };

        return new OperatorAuthenticationService(Options.Create(options));
    }
}
