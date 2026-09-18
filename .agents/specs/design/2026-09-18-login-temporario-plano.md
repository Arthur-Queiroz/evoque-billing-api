# Login temporário — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exigir identificação para usar o sistema, e fazer o operador registrado na auditoria ser quem de fato agiu.

**Architecture:** Autenticação por cookie do próprio ASP.NET Core, com os usuários vindo do ambiente. A exigência entra como política padrão, não como atributo espalhado. O operador deixa de vir do corpo da requisição e passa a ser lido do usuário autenticado.

**Tech Stack:** ASP.NET Core (net10.0), xUnit, Next.js/TypeScript.

**Spec:** `.agents/specs/design/2026-09-18-login-temporario-design.md`

---

## Por que esta ordem

As Tasks 1 a 4 fecham o buraco: ninguém usa o sistema sem se identificar. As
Tasks 5 a 7 consertam a auditoria, que é a segunda metade do problema.

Parar depois da Task 4 deixa o sistema protegido e a auditoria ainda mentindo.
É pior lugar para parar do que parecer, e vale saber disso antes de começar.

## Estrutura de arquivos

**Criar (API):**

| Arquivo | Responsabilidade |
|---|---|
| `Evoque.Billing.Api/Authentication/OperatorAccountOptions.cs` | Os usuários, como o ambiente os descreve |
| `Evoque.Billing.Api/Authentication/OperatorAuthenticationService.cs` | Esta senha confere para este usuário? |
| `Evoque.Billing.Api/Authentication/OperatorPrincipal.cs` | De onde o controller lê quem está agindo |
| `Evoque.Billing.Api/Controllers/SessionController.cs` | Entrar, sair, quem sou eu |
| `Evoque.Billing.Api/Contracts/SessionContracts.cs` | DTOs da sessão |
| `Evoque.Billing.Api.Tests/OperatorAuthenticationServiceTests.cs` | Testes da validação |
| `Evoque.Billing.Api.Tests/AuthorizationPolicyTests.cs` | Prova que a política padrão protege |

A pasta `Authentication/` existe para que o dia do Azure comece apagando um
diretório. É o desacoplamento pedido, visível na árvore de arquivos.

**Modificar (API):**

| Arquivo | Mudança |
|---|---|
| `Evoque.Billing.Api/Program.cs` | Esquema, política padrão, `ForwardedHeaders`, recusa subir sem usuário |
| `Evoque.Billing.Api/Contracts/*.cs` | 21 contratos de requisição perdem `OperatorId` |
| `Evoque.Billing.Api/Controllers/*.cs` | Passam a ler o operador do usuário autenticado |
| `Evoque.Billing.Api.Tests/*.cs` | 53 pontos que constroem esses contratos |
| `infra/env/production.env.example` | Bloco `AUTH__USERS__*` documentado |

**Modificar (Web), em `C:\prog\evoque\web\client`:**

| Arquivo | Mudança |
|---|---|
| `src/lib/api.ts` | Sessão, e 37 chamadas param de enviar o operador |
| `src/app/page.tsx` | Tela de login, porteiro, botão de sair, 23 usos de `operatorId` |

**Verificação (todas as tasks da API):**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Baseline: **191 testes verdes, zero warnings.**

---

### Task 1: Quem são os usuários, e a senha confere

Nada de HTTP nesta task. O serviço responde uma pergunta e é testável direto.

**Files:**
- Create: `Evoque.Billing.Api/Authentication/OperatorAccountOptions.cs`
- Create: `Evoque.Billing.Api/Authentication/OperatorAuthenticationService.cs`
- Test: `Evoque.Billing.Api.Tests/OperatorAuthenticationServiceTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

Crie `Evoque.Billing.Api.Tests/OperatorAuthenticationServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~OperatorAuthenticationServiceTests"
```

Esperado: erro de compilação — `OperatorAccountOptions`, `OperatorAccount` e
`OperatorAuthenticationService` não existem.

- [ ] **Step 3: Criar as opções**

`Evoque.Billing.Api/Authentication/OperatorAccountOptions.cs`:

```csharp
namespace Evoque.Billing.Api.Authentication;

/// <summary>
/// Os operadores autorizados, vindos do ambiente. Não há tabela nem cadastro:
/// são três ou quatro pessoas e esta camada inteira sai quando a autenticação
/// passar para o Azure.
/// </summary>
public sealed class OperatorAccountOptions
{
    public const string SectionName = "Auth";

    public IReadOnlyCollection<OperatorAccount> Users { get; init; } = [];
}

public sealed class OperatorAccount
{
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Em texto, sem hash. O mesmo arquivo guarda a chave de produção do Asaas,
    /// com que se cria cobrança real — proteger a senha de quem já tem aquilo
    /// não muda nada. A contrapartida é que estas senhas são geradas, nunca
    /// escolhidas: ninguém reusa dezesseis caracteres aleatórios, e é o reuso
    /// que faria um vazamento machucar fora deste sistema.
    /// </summary>
    public string Password { get; init; } = string.Empty;
}
```

- [ ] **Step 4: Criar o serviço**

`Evoque.Billing.Api/Authentication/OperatorAuthenticationService.cs`:

```csharp
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
        var expectedPassword = operatorAccount?.Password ?? string.Empty;
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
```

- [ ] **Step 5: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: PASS, com os 8 casos novos somados aos 191.

- [ ] **Step 6: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Authentication/ Evoque.Billing.Api.Tests/OperatorAuthenticationServiceTests.cs
git commit -F - <<'EOF'
Answer whether a username and password check out

Kept clear of HTTP so it can be tested without standing an application
up. The comparison runs even for a user that does not exist: leaving
early would answer faster for unknown names, and that tells whoever is
trying which ones are worth attacking, however identical the message is.

The name returned is the configured one, not the typed one, because it
is what reaches the audit trail and GEOVANNA and geovanna have to become
the same line.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 2: A aplicação não sobe sem operador configurado

**Files:**
- Modify: `Evoque.Billing.Api/Authentication/OperatorAccountOptions.cs`
- Test: `Evoque.Billing.Api.Tests/OperatorAuthenticationServiceTests.cs`

- [ ] **Step 1: Escrever o teste que falha**

Acrescente em `OperatorAuthenticationServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~OperatorAuthenticationServiceTests"
```

Esperado: erro de compilação — `Validate` não existe.

- [ ] **Step 3: Implementar**

Acrescente a `OperatorAccountOptions`:

```csharp
    /// <summary>
    /// Recusa uma configuração que deixaria o sistema aberto ou ambíguo. É
    /// chamada na subida, de propósito: um erro aqui derruba o deploy, e é
    /// exatamente o que se quer quando a alternativa é subir sem proteção.
    /// </summary>
    public void Validate()
    {
        if (Users.Count == 0)
        {
            throw new InvalidOperationException(
                "Nenhum operador configurado. Defina ao menos AUTH__USERS__0__USERNAME e "
                + "AUTH__USERS__0__PASSWORD no ambiente.");
        }

        foreach (var operatorAccount in Users)
        {
            if (string.IsNullOrWhiteSpace(operatorAccount.Username)
                || string.IsNullOrWhiteSpace(operatorAccount.Password))
            {
                throw new InvalidOperationException(
                    "Todo operador configurado precisa de AUTH__USERS__n__USERNAME e "
                    + "AUTH__USERS__n__PASSWORD preenchidos.");
            }
        }

        var distinctUsernameCount = Users
            .Select(operatorAccount => operatorAccount.Username.ToLowerInvariant())
            .Distinct()
            .Count();
        if (distinctUsernameCount != Users.Count)
        {
            throw new InvalidOperationException(
                "Há operadores repetidos em AUTH__USERS. Cada usuário precisa de um nome único.");
        }
    }
```

- [ ] **Step 4: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --no-restore
```

- [ ] **Step 5: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Authentication/OperatorAccountOptions.cs Evoque.Billing.Api.Tests/OperatorAuthenticationServiceTests.cs
git commit -F - <<'EOF'
Refuse to start with no operator configured

An empty user list could be read as "no restriction", and the failure
mode is a deploy that quietly removes the protection. Failing to start
is loud and reversible; serving the system open is neither.

Duplicate usernames are refused too: authentication would depend on the
order the environment happened to be written, and the audit trail could
not tell the two apart.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 3: O cookie, a política padrão e os endpoints de sessão

**Files:**
- Create: `Evoque.Billing.Api/Authentication/OperatorPrincipal.cs`
- Create: `Evoque.Billing.Api/Contracts/SessionContracts.cs`
- Create: `Evoque.Billing.Api/Controllers/SessionController.cs`
- Modify: `Evoque.Billing.Api/Program.cs`

- [ ] **Step 1: Criar o acesso ao operador autenticado**

`Evoque.Billing.Api/Authentication/OperatorPrincipal.cs`:

```csharp
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
```

- [ ] **Step 2: Criar os DTOs**

`Evoque.Billing.Api/Contracts/SessionContracts.cs`:

```csharp
namespace Evoque.Billing.Api.Contracts;

public sealed record SignInRequest(string Username, string Password);

public sealed record SessionResponse(string OperatorId);
```

- [ ] **Step 3: Criar o controller**

`Evoque.Billing.Api/Controllers/SessionController.cs`:

```csharp
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
    public async Task<ActionResult<SessionResponse>> SignInAsync(
        SignInRequest request,
        CancellationToken cancellationToken)
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
```

- [ ] **Step 4: Ligar no `Program.cs`**

Acrescente os `using` no topo:

```csharp
using Evoque.Billing.Api.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
```

Depois de `builder.Services.AddControllers();`:

```csharp
// A Cloudflare termina o TLS na borda e entrega HTTP ao Nginx. Sem isto a
// aplicação acredita que a requisição chegou por HTTP e se recusa a emitir um
// cookie `Secure`: o login funcionaria local e falharia em produção.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.Configure<OperatorAccountOptions>(
    builder.Configuration.GetSection(OperatorAccountOptions.SectionName));
builder.Services.AddScoped<OperatorAuthenticationService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "evoque.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // Sem isto o ASP.NET Core responde 302 para uma tela de login que não
        // existe nesta API. O portal precisa de 401 para saber que deve pedir
        // credencial de novo.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

// Exigir autenticação por padrão, e não por [Authorize] em cada controller.
// Assim um controller novo nasce protegido e liberar acesso vira ato explícito;
// o contrário falha no dia em que alguém esquecer um.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

Logo depois de `var app = builder.Build();`, antes de qualquer outro middleware:

```csharp
app.Services.GetRequiredService<IOptions<OperatorAccountOptions>>().Value.Validate();
```

Isso exige `using Microsoft.Extensions.Options;` no topo.

No pipeline, substitua o trecho atual por:

```csharp
app.UseForwardedHeaders();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseCors("WebClient");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
```

`UseAuthentication` precisa vir **antes** de `UseAuthorization`: sem isso não há
usuário para a política avaliar e toda requisição vira 401.

`/health` precisa do `AllowAnonymous` explícito porque a política padrão passa a
valer também para ele, e o Compose usa esse endpoint para saber se o contêiner
subiu — protegê-lo faria o contêiner parecer morto.

- [ ] **Step 5: Compilar e rodar a suíte**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: build limpo, suíte verde. Os testes não passam por HTTP, então a
política não afeta nenhum deles.

- [ ] **Step 6: Subir a aplicação e exercitar a sessão**

```bash
cd C:\prog\evoque\api\Evoque.Billing.Api
ASPNETCORE_URLS=http://localhost:5207 \
ASPNETCORE_ENVIRONMENT=Development \
AUTH__USERS__0__USERNAME=teste \
AUTH__USERS__0__PASSWORD=senha-de-teste-16 \
dotnet run --no-launch-profile
```

Noutro terminal:

```bash
curl.exe -s -o /dev/null -w "sem cookie: %{http_code}\n" http://localhost:5207/api/companies
curl.exe -s -o /dev/null -w "health: %{http_code}\n" http://localhost:5207/health
curl.exe -s -c C:\Users\arthu\AppData\Local\Temp\claude\cookies.txt -o /dev/null -w "login errado: %{http_code}\n" \
  -X POST -H "Content-Type: application/json" \
  -d '{"username":"teste","password":"errada"}' http://localhost:5207/api/session
curl.exe -s -c C:\Users\arthu\AppData\Local\Temp\claude\cookies.txt -o /dev/null -w "login certo: %{http_code}\n" \
  -X POST -H "Content-Type: application/json" \
  -d '{"username":"teste","password":"senha-de-teste-16"}' http://localhost:5207/api/session
curl.exe -s -b C:\Users\arthu\AppData\Local\Temp\claude\cookies.txt -o /dev/null -w "com cookie: %{http_code}\n" http://localhost:5207/api/companies
```

Esperado, nesta ordem: `401`, `200`, `401`, `200`, `200`.

O `401` da primeira linha é a prova que importa: é o endpoint que hoje responde
`200` para a internet aberta.

Encerre a aplicação com `Ctrl+C`.

- [ ] **Step 7: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Authentication/OperatorPrincipal.cs Evoque.Billing.Api/Contracts/SessionContracts.cs Evoque.Billing.Api/Controllers/SessionController.cs Evoque.Billing.Api/Program.cs
git commit -F - <<'EOF'
Require a signed-in operator for everything but health and signing in

GET /api/charge-history answered 200 to the open internet, and so did
approving a draft and executing a batch. The requirement lands as the
fallback policy rather than an attribute per controller, so a new
controller is protected by default and opening one up is deliberate.

The cookie scheme is redirected to answer 401 instead of 302: there is
no login page in this API to redirect to, and the portal needs the
status code to know it must ask again.

ForwardedHeaders goes in because Cloudflare terminates TLS at the edge
and hands the Nginx plain HTTP. Without it the application believes the
request arrived over HTTP and declines to issue a Secure cookie, which
works locally and fails in production.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 4: Provar que a política padrão protege

A política é invisível: nenhum teste a exercita por acidente, e ninguém percebe
tê-la perdido. Este teste existe para isso, e sobe a aplicação de verdade.

**Files:**
- Test: `Evoque.Billing.Api.Tests/AuthorizationPolicyTests.cs`
- Modify: `Evoque.Billing.Api.Tests/Evoque.Billing.Api.Tests.csproj`

- [ ] **Step 1: Acrescentar o pacote de testes de integração**

```bash
cd C:\prog\evoque\api
dotnet add Evoque.Billing.Api.Tests package Microsoft.AspNetCore.Mvc.Testing
```

`Program.cs` já termina com `public partial class Program;`, que é o que
`WebApplicationFactory<Program>` precisa. Não altere essa linha.

- [ ] **Step 2: Escrever os testes que falham**

Crie `Evoque.Billing.Api.Tests/AuthorizationPolicyTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Evoque.Billing.Api.Tests;

/// <summary>
/// A política padrão não é exercitada por nenhum outro teste — os demais falam
/// com services, não por HTTP. Sem estes casos, perdê-la não quebraria nada e
/// ninguém notaria até o sistema estar aberto de novo.
/// </summary>
public sealed class AuthorizationPolicyTests : IClassFixture<AuthenticatedApiFactory>
{
    private readonly AuthenticatedApiFactory factory;

    public AuthorizationPolicyTests(AuthenticatedApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task BusinessEndpoint_RefusesAnAnonymousRequest()
    {
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("/api/companies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_StaysOpenSoTheContainerIsNotReportedDead()
    {
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignIn_IsReachableWithoutBeingSignedIn()
    {
        using var httpClient = factory.CreateClient();

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
        using var httpClient = factory.CreateClient();

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
        using var httpClient = factory.CreateClient();
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
}
```

A chave `ConnectionStrings:BillingDatabase` é a que `Program.cs` lê em
`GetConnectionString("BillingDatabase")`. Deixá-la vazia é o que faz a aplicação
escolher os repositórios em memória, e é por isso que estes testes não precisam
de banco.

O `CreateClient` do `WebApplicationFactory` guarda cookies entre chamadas por
padrão, que é o que faz os dois últimos testes funcionarem.

- [ ] **Step 3: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~AuthorizationPolicyTests"
```

Esperado: PASS, 5 casos.

Se `BusinessEndpoint_RefusesAnAnonymousRequest` falhar com `200`, a política
padrão não está valendo — confira a ordem de `UseAuthentication` e
`UseAuthorization` no `Program.cs`.

- [ ] **Step 4: Provar que o teste morde**

Comente a linha `options.FallbackPolicy = ...` no `Program.cs` e rode de novo.
`BusinessEndpoint_RefusesAnAnonymousRequest` deve falhar com `200`. Restaure a
linha.

Um teste de proteção que passa com a proteção removida não protege nada, e já
aconteceu nesta base.

- [ ] **Step 5: Rodar a suíte inteira**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

- [ ] **Step 6: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api.Tests/AuthorizationPolicyTests.cs Evoque.Billing.Api.Tests/Evoque.Billing.Api.Tests.csproj
git commit -F - <<'EOF'
Pin the fallback policy with a test that fails without it

Every other test in this suite talks to services, not HTTP, so nothing
exercises the authorization policy by accident. Losing it would break no
test and nobody would notice until the system was open again.

Verified by removing the policy and watching the anonymous request come
back 200 instead of 401.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 5: O operador sai dos contratos de faturamento

Os services **não mudam** — já recebem `operatorId` como parâmetro. Muda quem
preenche esse parâmetro.

Esta task cobre `BillingDraftContracts`, `BillingPeriodContracts`,
`ChargeHistoryContracts` e `FiscalInvoiceContracts`, com seus controllers e
testes. A Task 6 cobre os de empresa. A divisão é por arquivo de contrato,
porque cada grupo compila por conta própria.

**Files:**
- Modify: `Evoque.Billing.Api/Contracts/BillingDraftContracts.cs`
- Modify: `Evoque.Billing.Api/Contracts/BillingPeriodContracts.cs`
- Modify: `Evoque.Billing.Api/Contracts/ChargeHistoryContracts.cs`
- Modify: `Evoque.Billing.Api/Contracts/FiscalInvoiceContracts.cs`
- Modify: `Evoque.Billing.Api/Controllers/BillingDraftsController.cs`
- Modify: `Evoque.Billing.Api/Controllers/BillingPeriodsController.cs`
- Modify: `Evoque.Billing.Api/Controllers/ChargeHistoryController.cs`
- Modify: `Evoque.Billing.Api/Controllers/FiscalInvoicesController.cs`
- Modify: os testes que constroem esses contratos

- [ ] **Step 1: Remover `OperatorId` dos contratos de requisição**

Em cada um dos quatro arquivos de contrato, remova o parâmetro `string OperatorId`
dos records de **requisição**.

**Não toque nos de resposta.** `AuditLogResponse` e qualquer outro que exponha
`OperatorId` mostram dado gravado — quem de fato agiu —, não alegação de quem
chama. Se o record tem `FromDomain` ou é devolvido por um endpoint, é resposta.

Quatro records ficam sem nenhum campo: `ApproveBillingDraftRequest`,
`CreateBillingPeriodRequest`, `SynchronizeChargeHistoryRequest` e
`ApproveChargeBatchRequest`. **Apague os records**, em vez de deixar um tipo
vazio sugerindo que um dia haverá algo ali. Os endpoints correspondentes passam
a não receber corpo.

- [ ] **Step 2: Ler o operador nos controllers**

Acrescente `using Evoque.Billing.Api.Authentication;` em cada controller e
troque cada `request.OperatorId` por `User.GetOperatorId()`.

Nos endpoints cujo record foi apagado, remova também o parâmetro do método. Por
exemplo, em `BillingPeriodsController`:

```csharp
    public async Task<ActionResult<BillingPeriodResponse>> CreateAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var billingPeriod = await billingPeriodService.CreateAsync(
            new BillingPeriodReference(year, month),
            User.GetOperatorId(),
            cancellationToken);
```

- [ ] **Step 3: Compilar e deixar o compilador listar os testes**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore 2>&1 | grep "error CS"
```

Cada erro aponta um ponto de teste que constrói um contrato com o argumento a
mais. Corrija todos removendo o argumento. **Não remova a constante
`OperatorId`** dos arquivos de teste onde ela ainda alimenta chamadas a services
e a métodos de domínio — essas continuam recebendo o operador por parâmetro.

- [ ] **Step 4: Rodar a suíte**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: build limpo e todos os testes verdes. A contagem não muda: nenhum
teste foi acrescentado ou removido, só ajustado.

- [ ] **Step 5: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Contracts/ Evoque.Billing.Api/Controllers/ Evoque.Billing.Api.Tests/
git commit -F - <<'EOF'
Take the billing operator from the session, not the request body

While the client said who it was, anyone could post OperatorId
"geovanna" and the system recorded the approval as hers. The audit trail
answered "who authorized this?" with whatever the caller typed.

Services are untouched: they already took the operator as a parameter,
which is the right shape. Only the controllers changed, and they now
read it from the authenticated user.

Four request records held nothing but the operator and are deleted
rather than left empty, because an empty record suggests something will
go there.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 6: O operador sai dos contratos de empresa

Mesma mudança, nos arquivos restantes.

**Files:**
- Modify: `Evoque.Billing.Api/Contracts/CompanyContracts.cs`
- Modify: `Evoque.Billing.Api/Contracts/CompanyBillingScheduleContracts.cs`
- Modify: `Evoque.Billing.Api/Contracts/CompanyCatalogImportContracts.cs`
- Modify: `Evoque.Billing.Api/Controllers/CompaniesController.cs`
- Modify: `Evoque.Billing.Api/Controllers/CompanyBillingSchedulesController.cs`
- Modify: `Evoque.Billing.Api/Controllers/CompanyCatalogImportsController.cs`
- Modify: `Evoque.Billing.Api/Controllers/CorporateMembersController.cs`
- Modify: os testes que constroem esses contratos

- [ ] **Step 1: Remover `OperatorId` dos contratos de requisição**

Liste-os sem depender de memória:

```bash
cd C:\prog\evoque\api
grep -n "OperatorId" Evoque.Billing.Api/Contracts/CompanyContracts.cs Evoque.Billing.Api/Contracts/CompanyBillingScheduleContracts.cs Evoque.Billing.Api/Contracts/CompanyCatalogImportContracts.cs
```

Remova o campo de todo record de requisição que aparecer — incluindo
`CreateCompanyRequest`, `UpdateCompanyRequest`, `CompanyOperatorRequest`,
`SetCompanyIssRetentionRequest`, `SynchronizeCompanyAsaasSandboxRequest`,
`UpsertCompanyBillingScheduleRequest` e
`CreateScheduledChargeBatchPreviewRequest`.

`CompanyOperatorRequest` só carrega o operador: **apague o record** e remova o
parâmetro dos métodos que o recebiam.

Em `CompanyCatalogImportContracts.cs`, `CompanyCatalogImportResponse` expõe
`OperatorId` como dado gravado — **mantenha**.

- [ ] **Step 2: Ler o operador nos controllers**

`using Evoque.Billing.Api.Authentication;` e `request.OperatorId` vira
`User.GetOperatorId()`. Onde o record foi apagado, remova o parâmetro do método.

Atenção ao upload de planilha: os endpoints que recebem `IFormFile` passam o
operador por `[FromForm]`. Ali também sai — o operador vem da sessão, não do
formulário.

- [ ] **Step 3: Compilar e corrigir os testes apontados**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore 2>&1 | grep "error CS"
```

`CompanyCatalogServiceTests` concentra a maioria: 58 ocorrências de
`OperatorId`, das quais só as que constroem contratos mudam.

- [ ] **Step 4: Verificar que não sobrou operador em requisição**

```bash
cd C:\prog\evoque\api
grep -rn "OperatorId" Evoque.Billing.Api/Contracts/
```

Esperado: só ocorrências em records de **resposta**. Qualquer `Request` que
ainda tenha `OperatorId` é um endpoint em que a auditoria continua falsificável.

- [ ] **Step 5: Rodar a suíte**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

- [ ] **Step 6: Documentar o ambiente**

Em `infra/env/production.env.example`, acrescente ao fim:

```text
# Operadores do portal. Sem ao menos um, a API se recusa a subir — uma lista
# vazia seria indistinguível de "sem restrição".
# As senhas são geradas, nunca escolhidas: ninguém reusa dezesseis caracteres
# aleatórios, e é o reuso que faria um vazamento atingir outros sistemas.
AUTH__USERS__0__USERNAME=troque-pelo-usuario
AUTH__USERS__0__PASSWORD=troque-por-uma-senha-gerada
```

E em `infra/docker-compose.production.yml`, no bloco `environment` do serviço
`api`, repasse as variáveis para o contêiner seguindo o padrão das que já estão
lá.

- [ ] **Step 7: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/ Evoque.Billing.Api.Tests/ infra/
git commit -F - <<'EOF'
Take the company operator from the session too

Same change as the billing contracts, including the spreadsheet upload
endpoints, where the operator arrived as a form field and is now read
from the session.

CompanyCatalogImportResponse keeps its OperatorId: that one is stored
data about who ran an import, not a claim by whoever is calling.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 7: A tela de login e o porteiro

**Files:**
- Modify: `C:\prog\evoque\web\client\src\lib\api.ts`
- Modify: `C:\prog\evoque\web\client\src\app\page.tsx`

- [ ] **Step 1: A sessão no cliente HTTP**

Em `src/lib/api.ts`, junto das demais interfaces:

```ts
export interface Session {
  operatorId: string;
}
```

Acrescente um helper para respostas sem corpo, ao lado de `requestOptional`:

```ts
async function requestWithoutResponse(path: string, options?: RequestInit): Promise<void> {
  const response = await fetch(apiUrl(path), {
    ...options,
    headers: { Accept: "application/json", ...options?.headers },
  });
  if (!response.ok) {
    throw new Error(`A API respondeu com erro ${response.status} ao chamar ${path}.`);
  }
}
```

E, junto dos demais métodos de `api`:

```ts
  getSession: () => request<Session>("/api/session"),
  signIn: (username: string, password: string) =>
    request<Session>("/api/session", {
      method: "POST",
      body: JSON.stringify({ username, password }),
    }),
  signOut: () => requestWithoutResponse("/api/session", { method: "DELETE" }),
```

- [ ] **Step 2: Remover o operador das chamadas**

Apague a constante `const operatorId = "operador-web";` de `page.tsx` e remova
`operatorId` dos corpos enviados em `api.ts` e dos argumentos em `page.tsx`.

O TypeScript aponta cada ponto quando as interfaces de requisição perderem o
campo. Onde o corpo ficar vazio — porque só tinha o operador — troque por
`method: "POST"` sem `body`.

- [ ] **Step 3: A tela**

Em `src/app/page.tsx`, junto dos demais componentes de página:

```tsx
function LoginPage({ isSigningIn, onSignIn }: {
  isSigningIn: boolean;
  onSignIn: (username: string, password: string) => void;
}) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");

  return <main className="flex min-h-screen items-center justify-center bg-slate-50 p-6">
    <form
      className="panel w-full max-w-sm p-7"
      onSubmit={(event) => {
        event.preventDefault();
        onSignIn(username, password);
      }}
    >
      <h1 className="text-xl font-extrabold">Evoque Cobranças</h1>
      <p className="mt-1 text-sm text-slate-500">Entre para continuar.</p>

      <label className="mt-6 block text-sm font-bold" htmlFor="username">Usuário</label>
      <input
        autoComplete="username"
        autoFocus
        className="field mt-1.5 w-full"
        id="username"
        onChange={(event) => setUsername(event.target.value)}
        value={username}
      />

      <label className="mt-4 block text-sm font-bold" htmlFor="password">Senha</label>
      <input
        autoComplete="current-password"
        className="field mt-1.5 w-full"
        id="password"
        onChange={(event) => setPassword(event.target.value)}
        type="password"
        value={password}
      />

      <button
        className="button-primary mt-6 w-full justify-center"
        disabled={isSigningIn || !username || !password}
        type="submit"
      >
        {isSigningIn ? "Entrando..." : "Entrar"}
      </button>
    </form>
  </main>;
}
```

O `<form>` com `onSubmit` faz o Enter funcionar, que é como se preenche um login.
Os `autoComplete` deixam o gerenciador de senhas do navegador reconhecer os
campos — importante porque as senhas aqui são geradas, não decoradas.

- [ ] **Step 4: O porteiro**

Em `BillingApplication`, junto dos demais estados:

```tsx
  const [session, setSession] = useState<Session | null>(null);
  const [isCheckingSession, setIsCheckingSession] = useState(true);
  const [isSigningIn, setIsSigningIn] = useState(false);
```

A verificação inicial, como primeiro efeito do componente:

```tsx
  useEffect(() => {
    void (async () => {
      try {
        setSession(await api.getSession());
      } catch {
        // 401 aqui é o caso normal de quem ainda não entrou, não um erro a
        // mostrar.
        setSession(null);
      } finally {
        setIsCheckingSession(false);
      }
    })();
  }, []);
```

E as duas ações:

```tsx
  async function signIn(username: string, password: string) {
    setIsSigningIn(true);
    try {
      setSession(await api.signIn(username, password));
    } catch (error) {
      showError(error instanceof Error ? error.message : "Não foi possível entrar.");
    } finally {
      setIsSigningIn(false);
    }
  }

  async function signOut() {
    try {
      await api.signOut();
    } finally {
      // Mesmo que a chamada falhe, o portal volta ao login: insistir em mostrar
      // a aplicação para quem pediu para sair é pior que uma sessão órfã no
      // servidor, que expira sozinha em 8 horas.
      setSession(null);
    }
  }
```

O `useEffect` que hoje carrega os dados iniciais precisa passar a depender da
sessão, para não disparar chamadas que voltariam 401 antes do login:

```tsx
  useEffect(() => {
    if (session === null) return;
    // ... o conteúdo atual da carga inicial
  }, [session]);
```

E antes do `return` da aplicação:

```tsx
  if (isCheckingSession) {
    return <main className="flex min-h-screen items-center justify-center text-sm text-slate-500">
      Carregando...
    </main>;
  }

  if (session === null) {
    return <>
      {errorMessage && <div className="mx-auto max-w-sm pt-6"><Callout tone="error" onDismiss={() => setErrorMessage(null)}>{errorMessage}</Callout></div>}
      <LoginPage isSigningIn={isSigningIn} onSignIn={(username, password) => void signIn(username, password)} />
    </>;
  }
```

- [ ] **Step 5: Quem está logado, e sair**

No rodapé do `Sidebar`, depois da lista de itens:

```tsx
      <div className="mt-auto border-t border-slate-200 pt-4">
        <p className="px-3 text-xs font-bold uppercase tracking-wide text-slate-400">Operador</p>
        <p className="mt-1 px-3 text-sm font-bold">{operatorId}</p>
        <button className="mt-2 px-3 text-sm font-bold text-slate-500 hover:text-charcoal" onClick={onSignOut}>
          Sair
        </button>
      </div>
```

Acrescente `operatorId: string` e `onSignOut: () => void` às props do `Sidebar` e
passe `session.operatorId` e `() => void signOut()` no ponto onde ele é
renderizado.

Mostrar o nome não é enfeite: é como alguém percebe que entrou com o usuário
errado antes de aprovar um lote no nome de outra pessoa.

- [ ] **Step 6: A sessão que expira no meio do uso**

Em `src/lib/api.ts`, dentro de `request`, antes do tratamento de erro atual:

```ts
  if (response.status === 401) {
    // A sessão de 8 horas expira com o portal aberto. Sem isto, a tela mostraria
    // "erro 401" em cada ação e o operador não saberia que bastava entrar de
    // novo.
    window.dispatchEvent(new CustomEvent("evoque:session-expired"));
    throw new Error("Sua sessão expirou. Entre novamente.");
  }
```

E em `page.tsx`, junto dos demais efeitos:

```tsx
  useEffect(() => {
    function handleSessionExpired() {
      setSession(null);
    }

    window.addEventListener("evoque:session-expired", handleSessionExpired);
    return () => window.removeEventListener("evoque:session-expired", handleSessionExpired);
  }, []);
```

- [ ] **Step 7: Compilar**

```bash
cd C:\prog\evoque\web\client
npm run build
```

Esperado: `Compiled successfully`.

- [ ] **Step 8: Exercitar ponta a ponta**

Suba a API como na Task 3, Step 6, e o portal com `npm run dev`. Confira:

1. abrir o portal mostra a tela de login, não a aplicação;
2. senha errada mostra "Usuário ou senha inválidos.";
3. senha certa entra, e o nome do operador aparece;
4. recarregar a página continua logado;
5. sair volta para o login;
6. aprovar algo e conferir a auditoria mostra o nome de quem entrou, não
   `operador-web`.

O item 6 é o que esta feature inteira existe para conseguir.

- [ ] **Step 9: Commit**

```bash
cd C:\prog\evoque\web\client
git add src/lib/api.ts src/app/page.tsx
git commit -F - <<'EOF'
Ask who is using the portal, and stop claiming to be operador-web

The portal sent a hardcoded operator with every action, so approving a
draft and typing CONFIRMAR were recorded under a name nobody holds. It
now sends nothing and the server reads the signed-in operator.

A 401 from any call returns the portal to the login screen. The session
lasts eight hours and expires with the tab open; without this the
operator would see "erro 401" on every action with no hint that signing
in again was all it took.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

## Depois do plano

Atualizar `.agents/PENDENCIAS.md`: o item **2.5** passa a resolvido, e o **2.1**
deixa de ser prejudicado por ele — uma tela de auditoria passa a mostrar nomes
de verdade.

Registrar como pendência operacional, fora de código: ligar **"Always Use
HTTPS"** no painel da Cloudflare. Hoje `http://evoque.devarthur.com.br` responde
`200` sem redirecionar, e o cookie `Secure` não é enviado por HTTP — quem chegar
pelo endereço sem `https://` não consegue entrar, com sintoma confuso.

Combinar com a Evoque **quem são os operadores** e gerar as senhas antes do
deploy. Sem ao menos um configurado, a API não sobe: o deploy falha em vez de
subir desprotegido, e é preciso saber disso antes e não durante.
