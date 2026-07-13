# Prompt 00 — Setup da solução

Lê CLAUDE.md, docs/03-arquitetura.md e as skills sfc-convencoes antes de começar.

Quero fazer o setup inicial do projeto, sem nenhuma funcionalidade de negócio ainda:

1. Solução .NET 9 com os projetos Sfc.Domain, Sfc.Infrastructure, Sfc.Web e os projetos de teste (xUnit), conforme docs/03-arquitetura.md.
2. EF Core + PostgreSQL configurado; entidade `Organization` com seed da SFC; global query filter por OrganizationId preparado (ADR-002).
3. ASP.NET Identity com roles Admin e Editor; seed de um utilizador admin para dev.
4. `portal/` com Next.js (App Router, TypeScript, Tailwind, shadcn/ui) — apenas uma home placeholder.
5. `docker-compose.yml` para dev: PostgreSQL + MinIO.
6. GitHub Actions mínimo: build + testes em cada push.
7. Um teste de integração a passar (arranque da app + ligação à BD via Testcontainers).

Critério de aceitação: `dotnet test` verde, `docker compose up` funcional, portal a arrancar com `npm run dev`. Nada mais — sem entidades de negócio, sem UI além de placeholders.
