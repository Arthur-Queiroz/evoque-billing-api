# Valor por empresa — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gerar as prévias de faturamento a partir da base de colaboradores e de um valor por empresa, sem planilha montada à mão.

**Architecture:** A empresa ganha um valor por colaborador, semeado na própria migration. Um serviço novo lê a base de colaboradores e o catálogo e cria as prévias; ele não conhece planilha, e o leitor de planilha não conhece valor por empresa.

**Tech Stack:** ASP.NET Core (net10.0), MySqlConnector, xUnit, Next.js/TypeScript.

**Spec:** `.agents/specs/design/2026-09-18-valor-por-empresa-design.md`

---

## Ordem deliberada

As Tasks 1 a 3 põem o valor no catálogo e o deixam visível. As Tasks 4 a 6
geram as prévias. As Tasks 7 e 8 são a tela.

Parar depois da Task 3 deixa o sistema com o dado guardado e nada usando —
inútil, mas não perigoso. Parar depois da Task 6 deixa a geração funcionando só
por API, sem tela.

## Estrutura de arquivos

**Criar (API):**

| Arquivo | Responsabilidade |
|---|---|
| `Evoque.Billing.Api/Domain/BillableCorporateContracts.cs` | Quais contratos a empresa paga |
| `Evoque.Billing.Api/Services/CorporateBillingDraftService.cs` | Gera as prévias da competência |
| `Evoque.Billing.Api/Contracts/CorporateBillingDraftContracts.cs` | DTOs da geração |
| `Evoque.Billing.Api.Tests/BillableCorporateContractsTests.cs` | Testes da lista |
| `Evoque.Billing.Api.Tests/CorporateBillingDraftServiceTests.cs` | Testes da geração |

**Modificar (API):**

| Arquivo | Mudança |
|---|---|
| `Evoque.Billing.Api/Domain/Company.cs` | `AmountPerMember` e `SetAmountPerMember` |
| `Evoque.Billing.Api/Repositories/DatabaseSchemaInitializer.cs` | Migration `014`, com os 28 valores |
| `Evoque.Billing.Api/Repositories/MySqlCompanyRepository.cs` | Persiste e lê a coluna |
| `Evoque.Billing.Api/Contracts/CompanyContracts.cs` | Expõe e recebe o valor |
| `Evoque.Billing.Api/Services/CompanyCatalogService.cs` | Grava o valor |
| `Evoque.Billing.Api/Controllers/BillingDraftsController.cs` | Endpoint de geração |
| `Evoque.Billing.Api/Program.cs` | Registro do serviço |

**Modificar (Web), em `C:\prog\evoque\web\client`:**

| Arquivo | Mudança |
|---|---|
| `src/lib/api.ts` | Campo no tipo `Company`, e a chamada de geração |
| `src/app/page.tsx` | Valor no cadastro, e a tela de geração |

**Verificação (todas as tasks da API):**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Confirme a baseline antes de começar; ela mudou várias vezes nesta branch.

---

### Task 1: Quais contratos a empresa paga

A lista vive no código, não em configuração. Mudar quem é cobrado é mudança de
regra de faturamento e precisa aparecer num diff.

**Files:**
- Create: `Evoque.Billing.Api/Domain/BillableCorporateContracts.cs`
- Test: `Evoque.Billing.Api.Tests/BillableCorporateContractsTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

Crie `Evoque.Billing.Api.Tests/BillableCorporateContractsTests.cs`:

```csharp
using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Tests;

public sealed class BillableCorporateContractsTests
{
    [Theory]
    [InlineData("EVOQUE CORPORATIVO - COBRANÇA INTERMEDIADA")]
    [InlineData("EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO")]
    public void Includes_TheTwoContractsTheCompanyPays(string contractName)
    {
        Assert.True(BillableCorporateContracts.Includes(contractName));
    }

    /// <summary>
    /// Este vem do EVO com valor preenchido: quem paga é a própria pessoa. Um
    /// `contains("CORPORATIVO")` o incluiria, e a empresa seria cobrada por
    /// alguém que já pagou.
    /// </summary>
    [Fact]
    public void Excludes_TheCorporateContractThatThePersonPays()
    {
        Assert.False(BillableCorporateContracts.Includes("EVOQUE CORPORATIVO RECORRENTE - 39,95"));
    }

    /// <summary>
    /// Cortesia encerrada. Não é cobrada de ninguém.
    /// </summary>
    [Fact]
    public void Excludes_TheDiscontinuedCourtesy()
    {
        Assert.False(BillableCorporateContracts.Includes("VIP (até 6 meses) - EVOQUE CORPORATIVO"));
    }

    [Theory]
    [InlineData("EVOPASS RECORRENTE 79,90")]
    [InlineData("Transferido da filial EVOQUE  RIBEIRÃO")]
    [InlineData("")]
    [InlineData(null)]
    public void Excludes_WhatIsNotCorporateBilling(string? contractName)
    {
        Assert.False(BillableCorporateContracts.Includes(contractName));
    }

    /// <summary>
    /// O EVO exporta o nome do contrato como foi digitado lá. Caixa e espaço
    /// sobrando não podem decidir se alguém é cobrado.
    /// </summary>
    [Theory]
    [InlineData("evoque corporativo - folha de pagamento")]
    [InlineData("  EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO  ")]
    public void Includes_IgnoringCaseAndSurroundingSpace(string contractName)
    {
        Assert.True(BillableCorporateContracts.Includes(contractName));
    }

    /// <summary>
    /// A lista é o dado que diz quem é cobrado. Se alguém acrescentar um
    /// contrato sem pensar, este teste falha e obriga a decisão a ser explícita.
    /// </summary>
    [Fact]
    public void HasExactlyTheTwoContractsWeKnowAbout()
    {
        Assert.Equal(2, BillableCorporateContracts.All.Count);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~BillableCorporateContractsTests"
```

Esperado: erro de compilação — `BillableCorporateContracts` não existe.

- [ ] **Step 3: Implementar**

`Evoque.Billing.Api/Domain/BillableCorporateContracts.cs`:

```csharp
namespace Evoque.Billing.Api.Domain;

/// <summary>
/// Os contratos que a empresa paga. Um colaborador só entra em prévia se o
/// contrato dele estiver aqui.
/// </summary>
/// <remarks>
/// A lista é explícita, e não um padrão de texto, porque o EVO tem contratos
/// cujo nome diz "CORPORATIVO" e que a empresa não paga: `EVOQUE CORPORATIVO
/// RECORRENTE - 39,95` sai da exportação com valor preenchido, ou seja, quem
/// paga é a própria pessoa.
///
/// Ela vive no código, não em configuração de ambiente, porque mudar quem é
/// cobrado é mudança de regra de faturamento: deve aparecer num diff e passar
/// por revisão, não ser editada na VPS. Um contrato novo no EVO não entra em
/// cobrança sozinho, e a tela de geração mostra que ele ficou de fora.
/// </remarks>
public static class BillableCorporateContracts
{
    public static IReadOnlyCollection<string> All { get; } =
    [
        "EVOQUE CORPORATIVO - COBRANÇA INTERMEDIADA",
        "EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO",
    ];

    public static bool Includes(string? contractName)
    {
        if (string.IsNullOrWhiteSpace(contractName))
        {
            return false;
        }

        return All.Any(billable =>
            string.Equals(billable, contractName.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: PASS, com os 9 casos novos.

- [ ] **Step 5: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Domain/BillableCorporateContracts.cs Evoque.Billing.Api.Tests/BillableCorporateContractsTests.cs
git commit -F - <<'EOF'
Name the contracts a company actually pays for

An explicit list rather than a pattern, because EVO has contracts whose
name says CORPORATIVO that the company does not pay: EVOQUE CORPORATIVO
RECORRENTE - 39,95 comes out of the export with a value filled in, which
means the person already paid. Matching on "CORPORATIVO" would bill the
company for them.

It lives in code rather than environment configuration. Changing who
gets billed is a billing rule and should show up in a diff, not in a
variable somebody edits on the VPS.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 2: A empresa guarda quanto paga por colaborador

**Files:**
- Modify: `Evoque.Billing.Api/Domain/Company.cs`
- Test: `Evoque.Billing.Api.Tests/CompanyCatalogServiceTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

Acrescente em `Evoque.Billing.Api.Tests/CompanyCatalogServiceTests.cs`, dentro da
classe existente:

```csharp
    /// <summary>
    /// O valor é opcional no cadastro de propósito: exigi-lo travaria cadastrar
    /// uma empresa antes de alguém saber quanto foi combinado. Quem recusa é a
    /// geração de prévia, não o cadastro.
    /// </summary>
    [Fact]
    public void NewCompany_HasNoAmountPerMemberYet()
    {
        var company = new Company(
            OpenSportsTaxId,
            "Open Sports",
            null,
            CompanySource.Manual,
            true,
            OperatorId,
            DateTimeOffset.UtcNow);

        Assert.Null(company.AmountPerMember);
        Assert.False(company.CanBeBilled);
    }

    [Fact]
    public void SetAmountPerMember_StoresTheAgreedAmount()
    {
        var company = new Company(
            OpenSportsTaxId,
            "Open Sports",
            null,
            CompanySource.Manual,
            true,
            OperatorId,
            DateTimeOffset.UtcNow);

        company.SetAmountPerMember(89.90m, OperatorId, DateTimeOffset.UtcNow);

        Assert.Equal(89.90m, company.AmountPerMember);
        Assert.True(company.CanBeBilled);
    }

    /// <summary>
    /// Zero e negativo não são preços. Aceitá-los produziria prévia de valor
    /// zero, que passa em toda validação seguinte e vira boleto sem sentido.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-89.90)]
    public void SetAmountPerMember_RefusesSomethingThatIsNotAPrice(decimal amount)
    {
        var company = new Company(
            OpenSportsTaxId,
            "Open Sports",
            null,
            CompanySource.Manual,
            true,
            OperatorId,
            DateTimeOffset.UtcNow);

        Assert.Throws<ValidationException>(
            () => company.SetAmountPerMember(amount, OperatorId, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Limpar o valor é diferente de zerar: uma empresa que saiu do corporativo
    /// deixa de ter preço, e a geração passa a recusá-la.
    /// </summary>
    [Fact]
    public void SetAmountPerMember_AcceptsNullToClearIt()
    {
        var company = new Company(
            OpenSportsTaxId,
            "Open Sports",
            null,
            CompanySource.Manual,
            true,
            OperatorId,
            DateTimeOffset.UtcNow);
        company.SetAmountPerMember(89.90m, OperatorId, DateTimeOffset.UtcNow);

        company.SetAmountPerMember(null, OperatorId, DateTimeOffset.UtcNow);

        Assert.Null(company.AmountPerMember);
        Assert.False(company.CanBeBilled);
    }

    /// <summary>
    /// Empresa inativa não é faturada, tenha preço ou não.
    /// </summary>
    [Fact]
    public void CanBeBilled_IsFalseForAnInactiveCompany()
    {
        var company = new Company(
            OpenSportsTaxId,
            "Open Sports",
            null,
            CompanySource.Manual,
            true,
            OperatorId,
            DateTimeOffset.UtcNow);
        company.SetAmountPerMember(89.90m, OperatorId, DateTimeOffset.UtcNow);

        company.Deactivate(OperatorId, DateTimeOffset.UtcNow);

        Assert.False(company.CanBeBilled);
    }
```

Leia o topo de `CompanyCatalogServiceTests.cs` antes: ele já tem constantes
`OperatorId` e um CNPJ de Open Sports. Use os nomes que estiverem lá em vez de
criar novos. Se o construtor de `Company` tiver outra assinatura, ajuste as
chamadas — o que não muda são as asserções.

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~CompanyCatalogServiceTests"
```

Esperado: erro de compilação — `AmountPerMember`, `SetAmountPerMember` e
`CanBeBilled` não existem.

- [ ] **Step 3: Implementar**

Em `Evoque.Billing.Api/Domain/Company.cs`, junto de `RetainsIss` (por volta da
linha 117):

```csharp
    /// <summary>
    /// Quanto a empresa paga por colaborador, combinado fora do sistema. O EVO
    /// não exporta esse valor: os contratos corporativos saem zerados porque o
    /// desconto acontece em folha.
    ///
    /// Nulo é o estado de uma empresa cujo valor ninguém informou ainda, e é
    /// diferente de zero — que não é preço nenhum.
    /// </summary>
    public decimal? AmountPerMember { get; private set; }

    /// <summary>
    /// Empresa pronta para gerar prévia. Faturar exige preço; cadastrar, não.
    /// </summary>
    public bool CanBeBilled => IsActive && AmountPerMember is > 0m;
```

Junto de `SetIssRetention` (por volta da linha 312):

```csharp
    public void SetAmountPerMember(decimal? amountPerMember, string operatorId, DateTimeOffset updatedAt)
    {
        if (amountPerMember is <= 0m)
        {
            throw new ValidationException(
                "O valor por colaborador deve ser maior que zero. Para retirar o valor, informe vazio.");
        }

        AmountPerMember = amountPerMember;
        RegisterUpdate(operatorId, updatedAt);
    }
```

Em `Restore`, acrescente `decimal? amountPerMember,` **depois de** `bool retainsIss,`
e `AmountPerMember = amountPerMember,` no inicializador de objeto.

- [ ] **Step 4: Corrigir os chamadores de `Restore`**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore 2>&1 | grep "error CS"
```

Cada erro é um ponto que constrói `Company.Restore`. Passe `null` na posição
nova, exceto no repositório MySQL — esse é a Task 3 e lerá a coluna de verdade.

- [ ] **Step 5: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

- [ ] **Step 6: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Domain/Company.cs Evoque.Billing.Api.Tests/CompanyCatalogServiceTests.cs Evoque.Billing.Api/Repositories/
git commit -F - <<'EOF'
Let a company carry what it pays per member

EVO does not export this: corporate contracts come out at zero because
the discount happens in payroll. Until now a human supplied the number
by hand every cycle, which is why no import format ever fixed it.

Null and zero are kept apart. Null means nobody has told us the price;
zero would be a price, and a zero-value draft passes every later check
and becomes a meaningless charge.

Optional to register, required to bill. Demanding it at registration
would block adding a company before anyone knows what was agreed, and
the rule today is that a CNPJ is enough.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 3: A coluna, a migration e os 28 valores

A migration `010` já semeou `retains_iss` com um `UPDATE`. Esta segue o mesmo
caminho: os valores entram versionados e revisáveis, em vez de digitados na
produção depois.

**Files:**
- Modify: `Evoque.Billing.Api/Repositories/DatabaseSchemaInitializer.cs`
- Modify: `Evoque.Billing.Api/Repositories/MySqlCompanyRepository.cs`

- [ ] **Step 1: Acrescentar a migration**

Em `DatabaseSchemaInitializer.cs`, junto dos demais identificadores:

```csharp
    private const string CompanyAmountPerMemberMigrationId = "014_add_company_amount_per_member";
```

Em `ApplyLatestMigrationsAsync`, ao final:

```csharp
        await AddCompanyAmountPerMemberAsync(connection, cancellationToken);
```

E o método:

```csharp
    /// <summary>
    /// Quanto cada empresa paga por colaborador. Os valores vêm do controle
    /// operacional da Evoque, conferidos em 18/09/2026 contra a exportação do
    /// EVO: 26 das 28 empresas cobráveis estavam lá, e as duas restantes foram
    /// informadas pela operação.
    ///
    /// Semeia aqui, e não por endpoint depois, porque assim os valores ficam
    /// versionados e revisáveis — e porque uma migration que roda sozinha no
    /// deploy não depende de alguém lembrar de um passo manual.
    /// </summary>
    private static async Task AddCompanyAmountPerMemberAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (await IsAppliedAsync(connection, CompanyAmountPerMemberMigrationId, cancellationToken))
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // A checagem existe porque a 009 já foi editada depois de aplicada neste
        // projeto, e a subida seguinte morreu com "duplicate column".
        if (!await ColumnExistsAsync(connection, transaction, "companies", "amount_per_member", cancellationToken))
        {
            await ExecuteAsync(connection, """
                ALTER TABLE companies
                ADD COLUMN amount_per_member DECIMAL(18, 2) NULL AFTER retains_iss;
                """, transaction, cancellationToken);
        }

        await ExecuteAsync(connection, """
            UPDATE companies SET amount_per_member = CASE tax_id
                WHEN '43322169000170' THEN 109.90
                WHEN '56087276000103' THEN 89.90
                WHEN '45871604000141' THEN 89.90
                WHEN '48885518000186' THEN 89.90
                WHEN '02346076000107' THEN 89.90
                WHEN '01919617000178' THEN 89.90
                WHEN '03203383000193' THEN 89.90
                WHEN '30368366000189' THEN 89.90
                WHEN '05872500000137' THEN 89.90
                WHEN '05872500000307' THEN 89.90
                WHEN '58757725000109' THEN 79.90
                WHEN '03868609000175' THEN 79.90
                WHEN '17193367000171' THEN 79.90
                WHEN '04902653000117' THEN 79.90
                WHEN '57482887000119' THEN 79.90
                WHEN '14357167000119' THEN 79.90
                WHEN '60524566000144' THEN 79.90
                WHEN '34426978000131' THEN 79.90
                WHEN '64877996000109' THEN 79.90
                WHEN '10899502000150' THEN 79.90
                WHEN '09406784000127' THEN 79.90
                WHEN '01330329000183' THEN 59.90
                WHEN '58515495000171' THEN 59.90
                WHEN '34818653000102' THEN 59.90
                WHEN '04026384000172' THEN 59.90
                WHEN '04967119000199' THEN 59.90
                WHEN '00618730000150' THEN 59.90
                WHEN '53164208000102' THEN 59.90
                ELSE amount_per_member
            END;
            """, transaction, cancellationToken);

        await InsertMigrationAsync(
            connection,
            transaction,
            CompanyAmountPerMemberMigrationId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
```

O `ELSE amount_per_member` preserva o valor de qualquer empresa fora da lista,
em vez de zerá-la. Uma empresa que não está no `CASE` simplesmente continua sem
valor.

- [ ] **Step 2: Persistir e ler a coluna**

Em `MySqlCompanyRepository.cs`:

No `INSERT ... ON DUPLICATE KEY UPDATE` de empresa, acrescente `amount_per_member`
à lista de colunas, `@amountPerMember` à de valores, e
`amount_per_member = VALUES(amount_per_member),` ao bloco de atualização.

O parâmetro, junto dos demais:

```csharp
        command.Parameters.AddWithValue(
            "@amountPerMember",
            company.AmountPerMember.HasValue ? company.AmountPerMember.Value : (object)DBNull.Value);
```

No `SELECT` que lê empresas, acrescente `amount_per_member`, e na chamada a
`Company.Restore`, na posição depois de `retains_iss`:

```csharp
            reader.IsDBNull(reader.GetOrdinal("amount_per_member"))
                ? null
                : reader.GetDecimal("amount_per_member"),
```

Confira se há mais de um `SELECT` de empresa no arquivo — todos precisam da
coluna, senão uns leem o valor e outros o perdem.

- [ ] **Step 3: Compilar e rodar a suíte**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

- [ ] **Step 4: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Repositories/
git commit -F - <<'EOF'
Persist the per-member amount, and seed the twenty-eight we know

Migration 014 adds the column and fills it, the way 010 seeded ISS
retention. The values come from the Evoque operational control sheet,
checked on 18/09/2026 against the EVO export: 26 of the 28 billable
companies were there, and the operation supplied the other two.

Seeding here rather than through an endpoint keeps the numbers in
version control and off a manual checklist somebody has to remember
after a deploy.

ELSE amount_per_member preserves anything already set, so a company
outside the list keeps whatever it had instead of being wiped.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 4: O valor no contrato HTTP e no cadastro

**Files:**
- Modify: `Evoque.Billing.Api/Contracts/CompanyContracts.cs`
- Modify: `Evoque.Billing.Api/Services/CompanyCatalogService.cs`
- Test: `Evoque.Billing.Api.Tests/CompanyCatalogServiceTests.cs`

- [ ] **Step 1: Escrever o teste que falha**

Acrescente em `CompanyCatalogServiceTests.cs`:

```csharp
    /// <summary>
    /// O valor precisa atravessar o cadastro e voltar na leitura. Sem isto, ele
    /// existiria no domínio e seria invisível para quem opera.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_StoresAndReturnsTheAmountPerMember()
    {
        var services = CreateServices();
        await services.CompanyCatalogService.CreateAsync(
            new CreateCompanyRequest(OpenSportsTaxId, "Open Sports", 20),
            CancellationToken.None);

        var updated = await services.CompanyCatalogService.UpdateAsync(
            OpenSportsTaxId,
            new UpdateCompanyRequest("Open Sports", 20, 89.90m),
            CancellationToken.None);

        Assert.Equal(89.90m, updated.AmountPerMember);

        var listed = await services.CompanyCatalogService.GetAsync(OpenSportsTaxId, CancellationToken.None);
        Assert.Equal(89.90m, listed.AmountPerMember);
    }
```

**Ajuste as assinaturas ao que existe.** `CreateCompanyRequest` e
`UpdateCompanyRequest` perderam `OperatorId` recentemente, e os nomes dos
métodos do service podem diferir. Leia `CompanyContracts.cs` e
`CompanyCatalogService.cs` antes de escrever; o que não muda é a asserção de que
o valor vai e volta.

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~CompanyCatalogServiceTests"
```

- [ ] **Step 3: Implementar**

Em `CompanyContracts.cs`, acrescente `decimal? AmountPerMember` como **último**
parâmetro de `UpdateCompanyRequest` e de `CompanyResponse`, e ao mapeamento de
`CompanyResponse`. Último de propósito: assim as construções posicionais
existentes continuam compilando.

Em `CompanyCatalogService`, no método que aplica a atualização, chame
`company.SetAmountPerMember(request.AmountPerMember, operatorId, updatedAt);`
junto das demais alterações.

Não acrescente o valor a `CreateCompanyRequest`. Cadastrar continua exigindo só
o CNPJ; informar o valor é uma edição.

- [ ] **Step 4: Rodar e commitar**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
git add Evoque.Billing.Api/ Evoque.Billing.Api.Tests/
git commit -F - <<'EOF'
Expose the per-member amount to whoever operates the catalogue

It goes on the update contract, not on create: registering a company
still needs only its CNPJ, and the price is something you learn later.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 5: Gerar as prévias da competência

O coração da feature. Este serviço não lê planilha.

**Files:**
- Create: `Evoque.Billing.Api/Contracts/CorporateBillingDraftContracts.cs`
- Create: `Evoque.Billing.Api/Services/CorporateBillingDraftService.cs`
- Test: `Evoque.Billing.Api.Tests/CorporateBillingDraftServiceTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

Crie `Evoque.Billing.Api.Tests/CorporateBillingDraftServiceTests.cs`:

```csharp
using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;
using Evoque.Billing.Api.Services;

namespace Evoque.Billing.Api.Tests;

public sealed class CorporateBillingDraftServiceTests
{
    private const string OperatorId = "maria";
    private const string OpenSportsTaxId = "56087276000103";
    private const string WebPradoTaxId = "43322169000170";

    /// <summary>
    /// Os dois números conferidos contra o fechamento que a Geovanna montou à
    /// mão: Open Sports 24 x 89,90 = 2.157,60 e Web Prado 4 x 109,90 = 439,60.
    /// O segundo é o mesmo total registrado em PROJECT_CONTEXT como validação
    /// de julho/2026. Se este teste falhar, o cálculo divergiu do que a operação
    /// já conferiu.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ReproducesTheClosingsDoneByHand()
    {
        var scenario = CreateScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddCompany(WebPradoTaxId, "Web Prado", 109.90m);
        scenario.AddMembers(OpenSportsTaxId, 24);
        scenario.AddMembers(WebPradoTaxId, 4);

        var resultado = await scenario.Service.GenerateAsync(
            new BillingPeriodReference(2026, 9), OperatorId, CancellationToken.None);

        var openSports = resultado.Created.Single(d => d.CompanyTaxId == OpenSportsTaxId);
        var webPrado = resultado.Created.Single(d => d.CompanyTaxId == WebPradoTaxId);
        Assert.Equal(2157.60m, openSports.TotalAmount);
        Assert.Equal(439.60m, webPrado.TotalAmount);
    }

    [Fact]
    public async Task GenerateAsync_CreatesOneItemPerMember()
    {
        var scenario = CreateScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 3);

        var resultado = await scenario.Service.GenerateAsync(
            new BillingPeriodReference(2026, 9), OperatorId, CancellationToken.None);

        Assert.Equal(3, resultado.Created.Single().MemberCount);
    }

    /// <summary>
    /// Empresa sem preço não vira prévia de valor zero — vira pendência com
    /// nome, para alguém resolver.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_SkipsACompanyWithoutAPriceAndSaysWhich()
    {
        var scenario = CreateScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", amountPerMember: null);
        scenario.AddMembers(OpenSportsTaxId, 24);

        var resultado = await scenario.Service.GenerateAsync(
            new BillingPeriodReference(2026, 9), OperatorId, CancellationToken.None);

        Assert.Empty(resultado.Created);
        var pendencia = Assert.Single(resultado.Skipped);
        Assert.Equal(OpenSportsTaxId, pendencia.CompanyTaxId);
        Assert.Contains("valor", pendencia.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_SkipsAnInactiveCompany()
    {
        var scenario = CreateScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m, isActive: false);
        scenario.AddMembers(OpenSportsTaxId, 24);

        var resultado = await scenario.Service.GenerateAsync(
            new BillingPeriodReference(2026, 9), OperatorId, CancellationToken.None);

        Assert.Empty(resultado.Created);
        Assert.Single(resultado.Skipped);
    }

    /// <summary>
    /// O assinante EVOPASS paga a própria academia. Cobrá-lo da empresa seria
    /// cobrar duas vezes pela mesma pessoa.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_IgnoresAMemberWhoPaysForThemselves()
    {
        var scenario = CreateScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 2);
        scenario.AddMember(OpenSportsTaxId, "EVOPASS RECORRENTE 79,90");

        var resultado = await scenario.Service.GenerateAsync(
            new BillingPeriodReference(2026, 9), OperatorId, CancellationToken.None);

        Assert.Equal(2, resultado.Created.Single().MemberCount);
    }

    [Fact]
    public async Task GenerateAsync_IgnoresAnInactiveMember()
    {
        var scenario = CreateScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 2);
        scenario.AddMember(OpenSportsTaxId, "EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO", isActive: false);

        var resultado = await scenario.Service.GenerateAsync(
            new BillingPeriodReference(2026, 9), OperatorId, CancellationToken.None);

        Assert.Equal(2, resultado.Created.Single().MemberCount);
    }

    /// <summary>
    /// Um contrato corporativo que o sistema não conhece não entra em cobrança
    /// sozinho, e também não some: aparece para alguém decidir.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ReportsAnUnknownCorporateContract()
    {
        var scenario = CreateScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 2);
        scenario.AddMember(OpenSportsTaxId, "EVOQUE CORPORATIVO RECORRENTE - 39,95");

        var resultado = await scenario.Service.GenerateAsync(
            new BillingPeriodReference(2026, 9), OperatorId, CancellationToken.None);

        Assert.Equal(2, resultado.Created.Single().MemberCount);
        Assert.Contains(
            resultado.UnknownContracts,
            contrato => contrato.Contains("39,95", StringComparison.Ordinal));
    }

    /// <summary>
    /// Clicar duas vezes não pode duplicar cobrança. A prévia existente é
    /// preservada e informada.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_DoesNotDuplicateWhenRunTwice()
    {
        var scenario = CreateScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", 89.90m);
        scenario.AddMembers(OpenSportsTaxId, 24);
        var competencia = new BillingPeriodReference(2026, 9);
        await scenario.Service.GenerateAsync(competencia, OperatorId, CancellationToken.None);

        var segunda = await scenario.Service.GenerateAsync(competencia, OperatorId, CancellationToken.None);

        Assert.Empty(segunda.Created);
        Assert.Single(segunda.Skipped);
        Assert.Single(scenario.DataStore.BillingDrafts);
    }

    /// <summary>
    /// Uma empresa que não pode ser faturada não impede as outras.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_KeepsGoingAfterACompanyIsSkipped()
    {
        var scenario = CreateScenario();
        scenario.AddCompany(OpenSportsTaxId, "Open Sports", amountPerMember: null);
        scenario.AddCompany(WebPradoTaxId, "Web Prado", 109.90m);
        scenario.AddMembers(OpenSportsTaxId, 24);
        scenario.AddMembers(WebPradoTaxId, 4);

        var resultado = await scenario.Service.GenerateAsync(
            new BillingPeriodReference(2026, 9), OperatorId, CancellationToken.None);

        Assert.Equal(WebPradoTaxId, resultado.Created.Single().CompanyTaxId);
        Assert.Single(resultado.Skipped);
    }

    private static TestScenario CreateScenario()
    {
        var dataStore = new InMemoryBillingDataStore();
        return new TestScenario(dataStore);
    }

    private sealed class TestScenario
    {
        private int proximoMembro = 1;

        public TestScenario(InMemoryBillingDataStore dataStore)
        {
            DataStore = dataStore;
            Service = new CorporateBillingDraftService(
                new InMemoryBillingPeriodRepository(dataStore),
                new InMemoryCompanyRepository(dataStore),
                new InMemoryCorporateMemberRepository(dataStore),
                new InMemoryBillingDraftRepository(dataStore),
                new InMemoryAuditLogRepository(dataStore));
        }

        public InMemoryBillingDataStore DataStore { get; }

        public CorporateBillingDraftService Service { get; }

        public void AddCompany(string taxId, string name, decimal? amountPerMember, bool isActive = true)
        {
            var company = new Company(
                taxId, name, null, CompanySource.Manual, true, OperatorId, DateTimeOffset.UtcNow);
            if (amountPerMember is not null)
            {
                company.SetAmountPerMember(amountPerMember, OperatorId, DateTimeOffset.UtcNow);
            }

            if (!isActive)
            {
                company.Deactivate(OperatorId, DateTimeOffset.UtcNow);
            }

            DataStore.Companies[taxId] = company;
        }

        public void AddMembers(string companyTaxId, int quantos)
        {
            for (var indice = 0; indice < quantos; indice++)
            {
                AddMember(companyTaxId, "EVOQUE CORPORATIVO - FOLHA DE PAGAMENTO");
            }
        }

        public void AddMember(string companyTaxId, string contractName, bool isActive = true)
        {
            var evoMemberId = proximoMembro++;
            var member = CorporateMember.Create(
                evoMemberId,
                $"Colaborador {evoMemberId}",
                companyTaxId,
                [new CorporateMemberContract($"c{evoMemberId}", null, contractName)],
                Guid.NewGuid(),
                OperatorId,
                DateTimeOffset.UtcNow);
            if (!isActive)
            {
                member.Deactivate(Guid.NewGuid(), OperatorId, DateTimeOffset.UtcNow);
            }

            DataStore.CorporateMembers[evoMemberId] = member;
        }
    }
}
```

**Leia `CorporateMember.Create` e `InMemoryCorporateMemberRepository` antes de
rodar.** As assinaturas acima são a intenção; se as reais diferirem, ajuste o
helper — nunca as asserções. Se não existir `InMemoryCompanyRepository` ou
`InMemoryCorporateMemberRepository`, procure os nomes reais em
`Evoque.Billing.Api/Repositories/`.

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd C:\prog\evoque\api
dotnet test Evoque.Billing.slnx --filter "FullyQualifiedName~CorporateBillingDraftServiceTests"
```

Esperado: erro de compilação — `CorporateBillingDraftService` não existe.

- [ ] **Step 3: Criar os DTOs**

`Evoque.Billing.Api/Contracts/CorporateBillingDraftContracts.cs`:

```csharp
namespace Evoque.Billing.Api.Contracts;

/// <summary>
/// O resultado da geração diz o que foi criado e, com igual destaque, o que não
/// foi. Erro silencioso aqui é receita que ninguém procura.
/// </summary>
public sealed record GenerateCorporateBillingDraftsResponse(
    IReadOnlyCollection<GeneratedBillingDraftResponse> Created,
    IReadOnlyCollection<SkippedCompanyResponse> Skipped,
    IReadOnlyCollection<string> UnknownContracts,
    IReadOnlyCollection<MemberWithoutCompanyResponse> MembersWithoutCompany);

public sealed record GeneratedBillingDraftResponse(
    Guid BillingDraftId,
    string CompanyTaxId,
    string CompanyName,
    int MemberCount,
    decimal AmountPerMember,
    decimal TotalAmount);

public sealed record SkippedCompanyResponse(
    string CompanyTaxId,
    string CompanyName,
    int MemberCount,
    string Reason);

public sealed record MemberWithoutCompanyResponse(
    long EvoMemberId,
    string MemberName);
```

- [ ] **Step 4: Criar o serviço**

`Evoque.Billing.Api/Services/CorporateBillingDraftService.cs`:

```csharp
using Evoque.Billing.Api.Contracts;
using Evoque.Billing.Api.Domain;
using Evoque.Billing.Api.Repositories;

namespace Evoque.Billing.Api.Services;

/// <summary>
/// Cria as prévias da competência a partir da base de colaboradores e do valor
/// combinado com cada empresa.
///
/// Não lê planilha. O que ele produz é exatamente a composição que a importação
/// do EVO já mostrou ao operador — e é por isso que a conferência acontece lá,
/// antes, e não aqui.
/// </summary>
public sealed class CorporateBillingDraftService(
    IBillingPeriodRepository billingPeriodRepository,
    ICompanyRepository companyRepository,
    ICorporateMemberRepository corporateMemberRepository,
    IBillingDraftRepository billingDraftRepository,
    IAuditLogRepository auditLogRepository)
{
    public async Task<GenerateCorporateBillingDraftsResponse> GenerateAsync(
        BillingPeriodReference billingPeriodReference,
        string operatorId,
        CancellationToken cancellationToken)
    {
        var billingPeriod = await billingPeriodRepository.FindByReferenceAsync(
            billingPeriodReference,
            cancellationToken)
            ?? throw new NotFoundException("A competência solicitada não foi encontrada.");

        var existingDrafts = await billingDraftRepository.ListByBillingPeriodIdAsync(
            billingPeriod.Id,
            cancellationToken);
        var companiesAlreadyDrafted = existingDrafts
            .Select(billingDraft => billingDraft.CompanyTaxId)
            .ToHashSet(StringComparer.Ordinal);

        var companies = await companyRepository.ListAsync(cancellationToken);
        var companiesByTaxId = companies.ToDictionary(company => company.TaxId, StringComparer.Ordinal);
        var members = await corporateMemberRepository.ListAsync(cancellationToken);

        var created = new List<GeneratedBillingDraftResponse>();
        var skipped = new List<SkippedCompanyResponse>();
        var unknownContracts = new SortedSet<string>(StringComparer.Ordinal);
        var membersWithoutCompany = new List<MemberWithoutCompanyResponse>();

        foreach (var membersOfCompany in GroupBillableMembers(
            members, companiesByTaxId, unknownContracts, membersWithoutCompany))
        {
            var company = companiesByTaxId[membersOfCompany.Key];
            var memberCount = membersOfCompany.Count();

            var refusal = DescribeRefusal(company, companiesAlreadyDrafted);
            if (refusal is not null)
            {
                skipped.Add(new SkippedCompanyResponse(
                    company.TaxId, company.DisplayName, memberCount, refusal));
                continue;
            }

            var amountPerMember = company.AmountPerMember!.Value;
            var billingDraft = new BillingDraft(
                billingPeriod.Id,
                company.TaxId,
                company.DisplayName,
                company.TaxId,
                null,
                membersOfCompany
                    .Select(member => new BillingDraftItem(
                        member.MemberName,
                        1,
                        amountPerMember,
                        member.EvoMemberId.ToString()))
                    .ToArray(),
                DateTimeOffset.UtcNow);

            await billingDraftRepository.AddAsync(billingDraft, cancellationToken);
            await auditLogRepository.AddAsync(
                AuditLog.Create(
                    "billing-draft.generated-from-catalog",
                    operatorId,
                    DateTimeOffset.UtcNow,
                    billingPeriod.Id,
                    billingDraft.Id,
                    $"{memberCount} colaborador(es) x {amountPerMember:N2} para {company.DisplayName}."),
                cancellationToken);

            created.Add(new GeneratedBillingDraftResponse(
                billingDraft.Id,
                company.TaxId,
                company.DisplayName,
                memberCount,
                amountPerMember,
                billingDraft.TotalAmount));
        }

        return new GenerateCorporateBillingDraftsResponse(
            created, skipped, unknownContracts, membersWithoutCompany);
    }

    /// <summary>
    /// Agrupa por empresa apenas quem é cobrável, e recolhe pelo caminho o que
    /// ficou de fora: contrato desconhecido e colaborador sem empresa no
    /// catálogo.
    /// </summary>
    private static IEnumerable<IGrouping<string, CorporateMember>> GroupBillableMembers(
        IReadOnlyCollection<CorporateMember> members,
        IReadOnlyDictionary<string, Company> companiesByTaxId,
        SortedSet<string> unknownContracts,
        List<MemberWithoutCompanyResponse> membersWithoutCompany)
    {
        var billable = new List<CorporateMember>();
        foreach (var member in members.Where(member => member.IsActive))
        {
            var contractNames = member.Contracts
                .Select(contract => contract.ContractName)
                .ToArray();

            if (!contractNames.Any(BillableCorporateContracts.Includes))
            {
                foreach (var contractName in contractNames)
                {
                    // Só interessa avisar sobre o que parece corporativo. Um
                    // EVOPASS fora da lista é o caso normal, não uma pendência.
                    if (!string.IsNullOrWhiteSpace(contractName)
                        && contractName.Contains("CORPORATIVO", StringComparison.OrdinalIgnoreCase))
                    {
                        unknownContracts.Add(contractName.Trim());
                    }
                }

                continue;
            }

            if (!companiesByTaxId.ContainsKey(member.CompanyTaxId))
            {
                membersWithoutCompany.Add(
                    new MemberWithoutCompanyResponse(member.EvoMemberId, member.MemberName));
                continue;
            }

            billable.Add(member);
        }

        return billable.GroupBy(member => member.CompanyTaxId, StringComparer.Ordinal);
    }

    private static string? DescribeRefusal(Company company, HashSet<string> companiesAlreadyDrafted)
    {
        if (companiesAlreadyDrafted.Contains(company.TaxId))
        {
            return "Já existe uma prévia desta empresa nesta competência.";
        }

        if (!company.IsActive)
        {
            return "Empresa inativa no catálogo.";
        }

        if (company.AmountPerMember is not > 0m)
        {
            return "Empresa sem valor por colaborador cadastrado.";
        }

        return null;
    }
}
```

- [ ] **Step 5: Rodar e confirmar que passa**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Esperado: PASS, com os 9 casos novos. O primeiro é o que mais importa: se
`ReproducesTheClosingsDoneByHand` falhar, o cálculo divergiu do que a operação já
conferiu à mão.

- [ ] **Step 6: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Services/CorporateBillingDraftService.cs Evoque.Billing.Api/Contracts/CorporateBillingDraftContracts.cs Evoque.Billing.Api.Tests/CorporateBillingDraftServiceTests.cs
git commit -F - <<'EOF'
Generate the previews from the roster and the agreed price

No spreadsheet is read. What this produces is the composition the EVO
import already showed the operator, which is why the checking happens
there and not here.

The first test is the one that matters: Open Sports at 24 x 89.90 and
Web Prado at 4 x 109.90 have to land on 2157.60 and 439.60, the figures
Geovanna reached by hand. If it ever fails, the calculation has drifted
from work the operation already verified.

The result names what was skipped as prominently as what was created. A
company with no price, a member who pays for themselves, a contract we
do not recognise: each is reported rather than silently dropped, because
silence here is revenue nobody goes looking for.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 6: O endpoint

**Files:**
- Modify: `Evoque.Billing.Api/Controllers/BillingDraftsController.cs`
- Modify: `Evoque.Billing.Api/Program.cs`

- [ ] **Step 1: Registrar o serviço**

Em `Program.cs`, junto dos demais:

```csharp
builder.Services.AddScoped<CorporateBillingDraftService>();
```

- [ ] **Step 2: Acrescentar o endpoint**

Em `BillingDraftsController.cs`, acrescente `CorporateBillingDraftService` ao
construtor primário e o método:

```csharp
    /// <summary>
    /// Gera as prévias da competência a partir da base de colaboradores e do
    /// valor de cada empresa. Não recebe planilha.
    /// </summary>
    [HttpPost("~/api/billing-periods/{year:int}/{month:int}/corporate-drafts")]
    [ProducesResponseType<GenerateCorporateBillingDraftsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GenerateCorporateBillingDraftsResponse>> GenerateCorporateDraftsAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var resultado = await corporateBillingDraftService.GenerateAsync(
            new BillingPeriodReference(year, month),
            User.GetOperatorId(),
            cancellationToken);
        return Ok(resultado);
    }
```

`User.GetOperatorId()` vem de `Evoque.Billing.Api.Authentication` — o operador
sai da sessão, não do corpo. Acrescente o `using` se faltar.

- [ ] **Step 3: Compilar, testar e exercitar**

```bash
cd C:\prog\evoque\api
dotnet build Evoque.Billing.slnx --no-restore -warnaserror
dotnet test Evoque.Billing.slnx --no-restore
```

Suba a aplicação com um operador configurado, entre com `POST /api/session`, e
chame o endpoint com o cookie. Esperado: `200` com listas vazias, porque o
ambiente local usa repositórios em memória sem dados.

- [ ] **Step 4: Commit**

```bash
cd C:\prog\evoque\api
git add Evoque.Billing.Api/Controllers/BillingDraftsController.cs Evoque.Billing.Api/Program.cs
git commit -F - <<'EOF'
Expose preview generation over HTTP

The operator comes from the session, not the request body, like every
other endpoint since the login landed.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 7: O valor na tela de empresa

**Files:**
- Modify: `C:\prog\evoque\web\client\src\lib\api.ts`
- Modify: `C:\prog\evoque\web\client\src\app\page.tsx`

- [ ] **Step 1: O campo no tipo**

Em `src/lib/api.ts`, acrescente a `Company`:

```ts
  amountPerMember: number | null;
```

E ao tipo do corpo de atualização de empresa, o mesmo campo opcional. Leia como
`updateCompany` monta o corpo hoje e siga o formato.

- [ ] **Step 2: O campo no formulário**

Em `CompanyDetailPage` (ou no formulário de edição que existir), acrescente um
campo de valor ao lado do dia de fechamento:

```tsx
      <label className="mt-4 block text-sm font-bold" htmlFor="amountPerMember">
        Valor por colaborador
      </label>
      <input
        className="field mt-1.5 w-full"
        id="amountPerMember"
        inputMode="decimal"
        onChange={(event) => setAmountPerMember(event.target.value)}
        placeholder="Ex.: 89,90"
        value={amountPerMember}
      />
      <p className="mt-1 text-xs text-slate-500">
        O EVO não informa esse valor: os contratos corporativos vêm zerados porque
        o desconto é em folha. Sem ele, a empresa não entra na geração de prévias.
      </p>
```

Converta vírgula para ponto antes de enviar, e mande `null` quando vazio.

- [ ] **Step 3: Mostrar quem está sem valor na lista**

Na tabela de empresas, acrescente uma coluna "Valor" mostrando
`money(company.amountPerMember)` quando houver, e um aviso discreto quando não:

```tsx
                  {company.amountPerMember === null
                    ? <span className="badge bg-amber-50 text-amber-700">Sem valor</span>
                    : money(company.amountPerMember)}
```

Uma empresa sem valor precisa ser encontrável antes do fechamento, não no
momento em que a geração a recusa.

- [ ] **Step 4: Compilar e commitar**

```bash
cd C:\prog\evoque\web\client
npm run build
git add src/lib/api.ts src/app/page.tsx
git commit -F - <<'EOF'
Let the catalogue hold what each company pays per member

The list marks a company with no price, so it can be found before the
closing rather than at the moment generation refuses it.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

### Task 8: A tela de geração

**Files:**
- Modify: `C:\prog\evoque\web\client\src\lib\api.ts`
- Modify: `C:\prog\evoque\web\client\src\app\page.tsx`

- [ ] **Step 1: A chamada**

Em `src/lib/api.ts`:

```ts
export interface GeneratedBillingDraft {
  billingDraftId: string;
  companyTaxId: string;
  companyName: string;
  memberCount: number;
  amountPerMember: number;
  totalAmount: number;
}

export interface SkippedCompany {
  companyTaxId: string;
  companyName: string;
  memberCount: number;
  reason: string;
}

export interface MemberWithoutCompany {
  evoMemberId: number;
  memberName: string;
}

export interface GenerateCorporateDraftsResult {
  created: GeneratedBillingDraft[];
  skipped: SkippedCompany[];
  unknownContracts: string[];
  membersWithoutCompany: MemberWithoutCompany[];
}
```

E o método:

```ts
  generateCorporateDrafts: (year: number, month: number) =>
    request<GenerateCorporateDraftsResult>(
      `/api/billing-periods/${year}/${month}/corporate-drafts`,
      { method: "POST" },
    ),
```

- [ ] **Step 2: O botão e o resultado**

Na tela de faturamento da competência, acrescente o botão e o resultado:

```tsx
function CorporateDraftResult({ result }: { result: GenerateCorporateDraftsResult }) {
  const totalGerado = result.created.reduce((soma, previa) => soma + previa.totalAmount, 0);

  return <div className="mt-5 space-y-5">
    <div className="panel p-5">
      <p className="font-extrabold">{result.created.length} prévia(s) criada(s) · {money(totalGerado)}</p>
      <ul className="mt-3 space-y-1 text-sm">
        {result.created.map((previa) => (
          <li key={previa.billingDraftId} className="flex justify-between gap-4">
            <span>{previa.companyName}</span>
            <span className="text-slate-500">
              {previa.memberCount} × {money(previa.amountPerMember)} = <strong>{money(previa.totalAmount)}</strong>
            </span>
          </li>
        ))}
        {result.created.length === 0 && <li className="text-slate-400">nenhuma</li>}
      </ul>
    </div>

    {/* Os três blocos abaixo aparecem mesmo vazios. Escondê-los quando não há
        nada ensinaria o operador a não procurá-los, e é justamente no dia em que
        têm conteúdo que eles importam. */}
    <ListaDoQueFicouDeFora titulo="Empresas não faturadas">
      {result.skipped.map((empresa) => (
        <li key={empresa.companyTaxId} className="flex justify-between gap-4">
          <span>{empresa.companyName} · {empresa.memberCount} pessoa(s)</span>
          <span className="text-amber-700">{empresa.reason}</span>
        </li>
      ))}
    </ListaDoQueFicouDeFora>

    <ListaDoQueFicouDeFora titulo="Colaboradores sem empresa no cadastro do EVO">
      {result.membersWithoutCompany.map((colaborador) => (
        <li key={colaborador.evoMemberId}>
          {colaborador.memberName} <span className="text-slate-400">#{colaborador.evoMemberId}</span>
        </li>
      ))}
    </ListaDoQueFicouDeFora>

    <ListaDoQueFicouDeFora titulo="Contratos não reconhecidos">
      {result.unknownContracts.map((contrato) => <li key={contrato}>{contrato}</li>)}
    </ListaDoQueFicouDeFora>
  </div>;
}

function ListaDoQueFicouDeFora({ children, titulo }: { children: React.ReactNode[]; titulo: string }) {
  return <div className="panel p-5">
    <p className="font-extrabold">{titulo}</p>
    <ul className="mt-3 space-y-1 text-sm">
      {children.length > 0 ? children : <li className="text-slate-400">nenhum</li>}
    </ul>
  </div>;
}
```

E o botão, junto dos demais controles da competência:

```tsx
        <button
          className="button-secondary"
          disabled={isGeneratingCorporateDrafts}
          onClick={() => void generateCorporateDrafts()}
        >
          {isGeneratingCorporateDrafts ? "Gerando..." : "Gerar prévias a partir do catálogo"}
        </button>
```

Com o estado e a função no componente principal:

```tsx
  const [corporateDraftResult, setCorporateDraftResult] = useState<GenerateCorporateDraftsResult | null>(null);
  const [isGeneratingCorporateDrafts, setIsGeneratingCorporateDrafts] = useState(false);

  async function generateCorporateDrafts() {
    setIsGeneratingCorporateDrafts(true);
    try {
      setCorporateDraftResult(await api.generateCorporateDrafts(selectedYear, selectedMonth));
      await refreshBillingData(selectedYear, selectedMonth);
    } catch (error) {
      showError(error instanceof Error ? error.message : "Não foi possível gerar as prévias.");
    } finally {
      setIsGeneratingCorporateDrafts(false);
    }
  }
```

`selectedYear`, `selectedMonth`, `refreshBillingData`, `showError` e `money` já
existem em `page.tsx`. Renderize `<CorporateDraftResult />` quando
`corporateDraftResult` não for nulo.

- [ ] **Step 3: Compilar e commitar**

```bash
cd C:\prog\evoque\web\client
npm run build
git add src/lib/api.ts src/app/page.tsx
git commit -F - <<'EOF'
Show what generation skipped as plainly as what it created

The skipped, orphaned and unrecognised blocks render even when empty.
Hiding them when there is nothing to show would teach the operator not
to look, and they matter precisely on the day they are not empty.

Claude-Session: https://claude.ai/code/session_01NJLspFgTbgJoemNRZRDwqX
EOF
```

---

## Depois do plano

Conferir em produção, com dados reais, que a geração de 09/2026 produz **28
prévias** e **R$ 12.334,60**, e que Open Sports dá 2.157,60 e Web Prado 439,60.
É o mesmo número que a Geovanna chegava à mão.

Atualizar `.agents/specs/BUSINESS_RULES.md`: a prévia passa a ter duas origens, e
a do catálogo é a normal.

Registrar em `PENDENCIAS.md`, se ainda não estiverem lá:

- **Plastpel e Ciasul sem dia de fechamento.** Têm valor agora, mas sem dia não
  entram em lote agendado. São 15 colaboradores e R$ 898,50 por mês.
- **Os 14 colaboradores sem CNPJ no EVO.** Treze com Profissão vazia e um com
  CNPJ truncado na NEW LIMP. Cerca de R$ 1.100 por mês que não são cobrados de
  ninguém, e a correção é no EVO, não aqui.
- **O dia 18.** `BUSINESS_RULES.md` lista `02, 18, 20, 25`; o controle
  operacional só tem 2, 20 e 25.
