# SFC EventsPlanner

Plataforma de gestão de eventos de desportos de combate. Backoffice para a associação + portal público inspirado na ONE Championship.

- **Estado:** Fase 1 — setup da solução concluído
- **Documentação:** `docs/`
- **Como desenvolver:** ver `CLAUDE.md` e `prompts/README.md`

## Estrutura

```
src/Sfc.Domain/          # Entidades e regras de domínio (sem dependências)
src/Sfc.Infrastructure/  # EF Core, PostgreSQL, Identity, migrations
src/Sfc.Web/             # Backoffice (Razor Pages) + futura API pública
tests/                   # xUnit (domínio + integração com Testcontainers)
portal/                  # Portal público Next.js (App Router)
```

## Desenvolvimento local

Pré-requisitos: .NET SDK 10, Node 22+, Docker.

```bash
# 1. Infraestrutura local (PostgreSQL + MinIO) — --wait espera pelo healthcheck
docker compose up -d --wait

# 2. Seed do admin de dev (ficheiro gitignored — criar uma vez)
#    src/Sfc.Web/appsettings.Development.json:
#    { "SeedAdmin": { "Email": "admin@sfc.local", "Password": "<escolhe-uma>" } }

# 3. Backoffice (aplica migrations e faz seed no arranque)
dotnet run --project src/Sfc.Web

# 4. Portal público
cd portal && npm install && npm run dev

# 5. Testes (integração usa Testcontainers — Docker tem de estar a correr)
dotnet test
```

Sem `SeedAdmin` configurado a app arranca na mesma — apenas não cria o utilizador admin.
