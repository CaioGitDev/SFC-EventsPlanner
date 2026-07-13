# ADR-002 — OrganizationId desde a primeira migration

- **Estado:** Aceite (2026-07-13)
- **Contexto:** A visão a longo prazo é SaaS multi-tenant, mas construir tenancy real agora é desperdício (YAGNI). Ignorar o tema por completo tornaria a migração futura cara.
- **Decisão:** Todas as entidades de domínio têm `OrganizationId` (FK para `Organization`). EF Core global query filter por organização. Fase 1 tem exatamente 1 organização (SFC), criada por seed. Sem UI, sem resolução de tenant por domínio, sem billing.
- **Consequências:** Custo quase nulo agora; caminho para multi-tenant preservado. Risco: falso sentido de "já é multi-tenant" — não é; isolamento real, auth por tenant e infra ficam para a Fase 4.
