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
    /// Ninguém decora a caixa do próprio login, e errá-la não é tentativa de
    /// invasão.
    /// </summary>
    [Fact]
    public void Authenticate_IgnoresTheCaseOfTheUsername()
    {
        var service = CreateService(("geovanna", "k7Qx2mVp9RtLw4Ha"));

        Assert.Equal("geovanna", service.Authenticate("GEOVANNA", "k7Qx2mVp9RtLw4Ha"));
    }

    /// <summary>
    /// A senha, ao contrário do usuário, diferencia maiúsculas. Separado do
    /// teste acima para que uma falha diga qual das duas regras quebrou.
    /// </summary>
    [Fact]
    public void Authenticate_DoesNotIgnoreTheCaseOfThePassword()
    {
        var service = CreateService(("geovanna", "k7Qx2mVp9RtLw4Ha"));

        Assert.Null(service.Authenticate("geovanna", "K7QX2MVP9RTLW4HA"));
    }

    /// <summary>
    /// Um campo ausente no corpo JSON chega como <c>null</c>, e a Task 3 vai
    /// expor este serviço num endpoint anônimo. Recusar sem lançar é o
    /// comportamento certo, e nada mais o prova.
    /// </summary>
    [Fact]
    public void Authenticate_RejectsNullCredentialsWithoutThrowing()
    {
        var service = CreateService(("geovanna", "k7Qx2mVp9RtLw4Ha"));

        Assert.Null(service.Authenticate(null!, "k7Qx2mVp9RtLw4Ha"));
        Assert.Null(service.Authenticate("geovanna", null!));
    }

    /// <summary>
    /// A recusa usa `IsNullOrWhiteSpace`, não `IsNullOrEmpty`, de propósito.
    /// Sem este teste, trocar um pelo outro passaria despercebido.
    /// </summary>
    [Fact]
    public void Authenticate_RejectsWhitespaceOnlyCredentials()
    {
        var service = CreateService(("geovanna", "k7Qx2mVp9RtLw4Ha"));

        Assert.Null(service.Authenticate("   ", "k7Qx2mVp9RtLw4Ha"));
        Assert.Null(service.Authenticate("geovanna", "   "));
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

    /// <summary>
    /// Uma lista vazia poderia ser lida como "sem restrição", e o modo de falha
    /// seria um deploy que remove a proteção em silêncio. Falhar na subida é
    /// barulhento e reversível; abrir o sistema não é.
    /// </summary>
    [Fact]
    public void Validate_RefusesToStartWithoutAnyOperator()
    {
        var options = new OperatorAccountOptions { Users = [] };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("AUTH__USERS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RefusesAnOperatorWithoutUsernameOrPassword()
    {
        var withoutUsername = new OperatorAccountOptions
        {
            Users = [new OperatorAccount { Username = "", Password = "k7Qx2mVp9RtLw4Ha" }],
        };
        var withoutPassword = new OperatorAccountOptions
        {
            Users = [new OperatorAccount { Username = "geovanna", Password = "" }],
        };

        Assert.Throws<InvalidOperationException>(withoutUsername.Validate);
        Assert.Throws<InvalidOperationException>(withoutPassword.Validate);
    }

    /// <summary>
    /// Dois operadores com o mesmo nome fariam a autenticação depender da ordem
    /// em que o ambiente foi escrito, e a auditoria não distinguiria os dois.
    /// </summary>
    [Fact]
    public void Validate_RefusesTwoOperatorsWithTheSameUsername()
    {
        var options = new OperatorAccountOptions
        {
            Users =
            [
                new OperatorAccount { Username = "geovanna", Password = "k7Qx2mVp9RtLw4Ha" },
                new OperatorAccount { Username = "GEOVANNA", Password = "Zp3nB8yTq6WsVe1C" },
            ],
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_AcceptsAWellFormedConfiguration()
    {
        var options = new OperatorAccountOptions
        {
            Users =
            [
                new OperatorAccount { Username = "geovanna", Password = "k7Qx2mVp9RtLw4Ha" },
                new OperatorAccount { Username = "arthur", Password = "Zp3nB8yTq6WsVe1C" },
            ],
        };

        options.Validate();
    }
}
