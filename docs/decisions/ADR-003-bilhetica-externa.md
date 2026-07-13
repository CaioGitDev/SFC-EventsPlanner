# ADR-003 — Bilhética sempre externa

- **Estado:** Aceite (2026-07-13)
- **Contexto:** Bilhética implica pagamentos, faturação, reembolsos, fraude e compliance — um produto inteiro. Existem soluções maduras (Stripe Payment Links, TicketTailor, Shotgun).
- **Decisão:** A plataforma nunca processa pagamentos. Cada evento tem um `TicketsUrl` externo; o portal mostra o CTA. Tipologias de bilhete (VIP, Ringside, etc.) geridas na plataforma externa.
- **Consequências:** Zero risco de pagamentos; perde-se analytics de vendas integrado (aceitável — dados disponíveis na plataforma externa). Revisitar apenas se um dia o modelo de negócio do SaaS depender de % de bilhética.
