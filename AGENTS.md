# Evoque Billing API

Leia antes de alterar código:

1. `.agents/specs/PROJECT_CONTEXT.md`
2. `.agents/specs/BUSINESS_RULES.md`
3. `.agents/specs/ARCHITECTURE.md`
4. `.agents/specs/DEPLOYMENT.md` quando a alteração afetar containers, GitHub
   Actions ou variáveis de ambiente.

## Backend

Mantenha a arquitetura explícita:

```text
Controller → Service → Repository → Banco de dados
```

- Controllers validam DTOs e devolvem HTTP; não implementam regra de negócio
  nem acessam banco.
- Services orquestram regras de faturamento e gateways externos.
- Repositories persistem somente dados do processo de faturamento.
- Use nomes completos e DTOs explícitos. Não introduza CQRS, MediatR, Result
  monads ou repositórios genéricos sem aprovação.

## Segurança financeira

- Evo é leitura somente.
- Sandbox é o único Asaas mutável autorizado durante o MVP.
- Produção exige ambiente configurado, prévia aprovada, lote aprovado,
  confirmação `CONFIRMAR`, auditoria e idempotência.
- Nunca versionar tokens, connection strings reais, chaves SSH ou arquivos
  `production.env`.

## Verificação

Execute `dotnet test Evoque.Billing.slnx --no-restore` após alterações de
backend. Para CI/CD e KVM2, use a skill local `deploy-cicd`; não execute deploy
por SSH manualmente.

