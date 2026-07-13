# ADR-005 — Backoffice em Razor Pages, portal em Next.js

- **Estado:** Aceite (2026-07-13)
- **Contexto:** Dois frontends com necessidades opostas: o backoffice precisa de produtividade CRUD; o portal precisa de SEO e qualidade visual (referência ONE Championship).
- **Decisão:** Backoffice em Razor Pages dentro do monólito (sem SPA, sem API interna duplicada). Portal público em Next.js consumindo uma API read-only mínima.
- **Consequências:** Um só stack de auth/deploy para o backoffice; Next.js apenas onde o SEO paga o custo. Se o backoffice vier a precisar de interatividade pesada (ex.: matchmaking drag-and-drop na Fase 2), avaliar ilhas de React ou htmx antes de migrar para SPA.
