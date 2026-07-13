# ADR-001 — Monólito modular, sem CQRS/MediatR

- **Estado:** Aceite (2026-07-13)
- **Contexto:** Um developer part-time. A proposta inicial (Clean Architecture + CQRS + MediatR + Redis + SignalR + Hangfire) é adequada a equipas, não a este contexto — cada camada extra é custo de manutenção sem valor entregue.
- **Decisão:** Monólito .NET com 3 projetos (Domain, Infrastructure, Web). Services diretos + EF Core. Sem CQRS, MediatR, Redis, SignalR ou Hangfire até existir um problema concreto medido que os justifique.
- **Consequências:** Menos cerimónia, iteração rápida. Se o projeto crescer para equipa/SaaS, refatoração será necessária — mitigada por cobertura de testes (TDD) e fronteiras claras entre módulos de domínio.
