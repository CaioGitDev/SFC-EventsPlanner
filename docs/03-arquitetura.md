# Arquitetura — Fase 1

Princípio: **arquitetura de uma pessoa part-time.** Cada peça extra tem de pagar a sua manutenção.

## Estrutura da solução

```
src/
  Sfc.Domain/          # Entidades, regras, sem dependências externas
  Sfc.Infrastructure/  # EF Core, PostgreSQL, storage de ficheiros
  Sfc.Web/             # Backoffice (Razor Pages) + API pública read-only
tests/
  Sfc.Domain.Tests/
  Sfc.Web.Tests/       # Testes de integração (WebApplicationFactory)
portal/                # Next.js (App Router) — portal público
```

Monólito modular: 3 projetos .NET chegam. Sem Clean Architecture com 6 camadas, sem CQRS, sem MediatR — services diretos e DbContext. Se a complexidade o exigir mais tarde, refatora-se com testes como rede.

## Decisões (detalhe em decisions/)

| Tema | Decisão | ADR |
|---|---|---|
| Arquitetura backend | Monólito modular, sem CQRS/MediatR | ADR-001 |
| Multi-tenant | Só `OrganizationId` + query filters; sem infra de tenancy | ADR-002 |
| Bilhética | Sempre externa (link) | ADR-003 |
| Dados sensíveis / RGPD | DoB e dados médicos nunca públicos; docs médicos só na Fase 2 com encriptação | ADR-004 |
| Backoffice | Razor Pages no monólito (não SPA) | ADR-005 |

## Frontend público (portal/)

- Next.js App Router + TypeScript + Tailwind + shadcn/ui
- Consome a API pública read-only do monólito
- SSG/ISR para SEO: `/fighters/{slug}`, `/events/{slug}`, `/events/{slug}/results`
- Revalidação on-demand quando o backoffice publica alterações

## Infraestrutura

- PostgreSQL
- Imagens: Cloudflare R2 (S3-compatible); WebP; redimensionamento no upload
- Docker Compose para dev local (Postgres + MinIO local como stand-in do R2)
- Deploy: 1 VPS ou serviço gerido barato; portal na Vercel (free tier)
- Sem Redis, SignalR ou Hangfire. Jobs agendados (se surgirem): `IHostedService`

## Requisitos não funcionais realistas (Fase 1)

- Portal: páginas estáticas/ISR — rápido por construção
- Backoffice: responsivo, utilizável em telemóvel no dia do evento
- Backups automáticos diários da BD com restore testado 1x antes do primeiro evento
- Auth: ASP.NET Identity, cookies no backoffice. MFA e OAuth2 ficam para quando houver mais utilizadores
