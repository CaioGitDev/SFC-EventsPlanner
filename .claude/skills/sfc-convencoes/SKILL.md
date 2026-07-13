---
name: sfc-convencoes
description: Convenções de código e estrutura do SFC EventsPlanner. Usar ao escrever ou rever qualquer código .NET ou Next.js deste projeto — naming, testes, migrations, estrutura de pastas e regras invioláveis.
---

# Convenções de código

## Regras invioláveis (ver também CLAUDE.md)

1. `OrganizationId` em toda a entidade de domínio + global query filter no DbContext
2. Código, entidades e testes em inglês; strings de UI em pt-PT
3. TDD em toda a lógica de domínio (records, resultados, regras de fight card)
4. Nunca expor DateOfBirth, contactos ou dados de pesagem não publicados na API pública

## .NET

- Estrutura: `Sfc.Domain` (sem dependências), `Sfc.Infrastructure` (EF Core, storage), `Sfc.Web` (Razor Pages + API pública)
- Services simples injetados; sem MediatR/CQRS (ADR-001)
- Entidades com construtores/métodos que protegem invariantes; sem setters públicos em campos derivados (Record)
- Migrations: nome descritivo em inglês (`AddWeighIn`), nunca editar migrations aplicadas
- Testes: xUnit; domínio sem mocks; integração com WebApplicationFactory + Testcontainers (PostgreSQL)
- API pública: read-only, DTOs explícitos (nunca serializar entidades), rotas `/api/public/...`

## Next.js (portal/)

- App Router, TypeScript strict, Tailwind + shadcn/ui
- Páginas de conteúdo: SSG/ISR com revalidação on-demand; nunca client-fetch para conteúdo indexável
- Slugs como única chave pública (`/fighters/{slug}`, `/events/{slug}`); nunca expor IDs internos
- Imagens via `next/image` com URLs do R2

## Git

- Branch por funcionalidade (workflow superpowers com worktrees)
- Commits pequenos, mensagem em inglês, imperativo (`Add fight result reversal`)
- Nunca fazer merge com testes a falhar
