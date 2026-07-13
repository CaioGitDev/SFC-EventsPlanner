# Prompt 05 — API pública e Portal

Lê CLAUDE.md, docs/01-ambito-fase1.md, docs/03-arquitetura.md, ADR-004 e as skills sfc-contexto e sfc-convencoes. Referência visual: ONE Championship.

## Parte A — API pública read-only

- Rotas `/api/public/...`: próximo evento, lista de eventos, detalhe de evento com fight card, perfil de atleta por slug, resultados de evento, weight results.
- DTOs explícitos. Testar primeiro: nunca expor DateOfBirth (só idade), contactos, IDs internos, eventos Draft, nem pesos de pesagem não aprovados. Atletas sem `PublicProfileConsent` aparecem só com nome.
- Endpoint de revalidação on-demand para o portal quando algo é publicado.

## Parte B — Portal (Next.js)

1. Home: hero do próximo evento (banner, countdown, CTA bilhetes se TicketsUrl, CTA "Ver em direto" se StreamUrl e data = hoje), próximos eventos, últimos resultados.
2. `/events/{slug}`: poster, info, fight card visual (atletas frente a frente: foto, record, bandeira).
3. `/fighters/{slug}`: foto, record, últimos combates, próximo combate.
4. `/events/{slug}/results`: resultados + weight results.
5. SSG/ISR com revalidação on-demand; metadata SEO e Open Graph por página; pt-PT.

Fora de âmbito: notícias, rankings, estatísticas, pesquisa, dark mode configurável. Sugiro dividir em 2 sessões (A e B). No fim de B, correr guardiao-ambito e revisor-dominio.
