# Arquitetura da API

## Componentes

```text
Next.js (repositório web)
        ↓ HTTPS /api
Nginx da stack
        ├── API ASP.NET Core :8080
        └── Next.js :3000
API → Evo (GET) / Asaas (Sandbox ou Produção autorizada)
API → MySQL próprio do faturamento
```

O Nginx, Compose e arquivos de ambiente de exemplo estão em `infra/`. A VPS
mantém segredos em `/opt/evoque/production.env`, fora do Git.

## Camadas

```text
Controllers → Services → Repositories → MySQL
                         ↘ Gateways Evo/Asaas
```

- DTOs em `Contracts/` definem contratos HTTP.
- Domínio em `Domain/` representa competências, prévias, agendas e lotes.
- Gateways encapsulam HTTP externo e não conhecem controllers.
- Migrations próprias são aplicadas pelo inicializador do schema; nunca aplicar
  alterações ao banco legado da Evoque.

## Integrações

Evo usa Basic Auth com usuário e token do servidor. Endpoints confirmados para
leitura incluem membros, colaboradores, grupos/filiais e consultas de
partnership. O consumo de `v3/membership` e `v3/membermembership` é um próximo
incremento para melhorar a composição de contratos corporativos.

Asaas é acessado apenas pelo backend. O client recebe estado de integração,
prévia e resultado, nunca tokens ou URLs autenticadas.

