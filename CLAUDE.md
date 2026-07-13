# SFC EventsPlanner

Plataforma de gestão de eventos de desportos de combate (Muay Thai, Kickboxing, K1, Boxe, MMA).
Primeiro cliente: SFC. Visão a longo prazo: SaaS multi-tenant para associações e promotores.

## Contexto essencial

- **Um developer, part-time.** Simplicidade é requisito, não preferência. YAGNI agressivo.
- **Critério de sucesso da Fase 1:** um evento SFC gerido de ponta a ponta na plataforma, sem Excel.
- Documentação de referência em `docs/` — ler antes de qualquer trabalho de design ou implementação:
  - `docs/00-visao.md` — visão e posicionamento
  - `docs/01-ambito-fase1.md` — o que está DENTRO e FORA da Fase 1 (vinculativo)
  - `docs/02-modelo-dominio.md` — entidades e regras de domínio
  - `docs/03-arquitetura.md` — stack e estrutura da solução
  - `docs/04-roadmap.md` — fases 1–4
  - `docs/05-git-workflow.md` — gates de qualidade/segurança e regras de PR
  - `docs/decisions/` — ADRs (decisões fechadas; não reabrir sem novo ADR)

## Regras invioláveis

1. **Âmbito:** nenhuma funcionalidade fora de `docs/01-ambito-fase1.md` é implementada sem atualização explícita desse documento pelo Caio. Em caso de dúvida, perguntar — não implementar.
2. **`OrganizationId` em todas as entidades de domínio** desde a primeira migration (ver ADR-002). Sem infraestrutura multi-tenant por agora — apenas a coluna e filtros.
3. **RGPD:** documentos médicos e dados de menores são dados sensíveis. Nunca expor no portal público; acesso apenas por role autorizado (ver ADR-004).
4. **Código em inglês, UI em português de Portugal.** Entidades, propriedades e testes em inglês; textos visíveis ao utilizador em pt-PT.
5. **Testes primeiro.** O workflow superpowers de TDD aplica-se a toda a lógica de domínio.
6. **Nunca push direto na `master`.** Todo o trabalho entra por PR com os gates de `docs/05-git-workflow.md`: guardiao-ambito + revisor-dominio + `/security-review` antes de abrir o PR; CI verde antes do merge.
7. **Segredos nunca no código.** `.env`/user-secrets em dev; GitHub Secrets em CI. O gitleaks corre em todos os PRs.

## Stack (resumo — detalhe em docs/03-arquitetura.md)

- Backend: ASP.NET Core (.NET 9), monólito modular, EF Core, PostgreSQL
- Frontend público: Next.js (App Router) + TypeScript + Tailwind + shadcn/ui
- Backoffice: Razor Pages/MVC dentro do monólito (simples primeiro)
- Sem Redis, SignalR, Hangfire, CQRS ou MediatR até haver problema concreto que os justifique

## Workflow de desenvolvimento

Este projeto usa o plugin **superpowers**. Fluxo por funcionalidade:

1. `prompts/` contém a sequência ordenada de prompts — usar como ponto de partida de cada sessão
2. Brainstorm/design → guardar em `docs/plans/`
3. Plano de implementação → execução com TDD → code review
4. Antes de dar por concluído: correr o agent `guardiao-ambito` para validar que nada saiu do âmbito

## Agents e skills locais

- `.claude/agents/guardiao-ambito.md` — valida trabalho contra o âmbito da Fase 1
- `.claude/agents/revisor-dominio.md` — valida regras de desportos de combate
- `.claude/skills/sfc-contexto/` — vocabulário e regras do domínio
- `.claude/skills/sfc-convencoes/` — convenções de código do projeto
