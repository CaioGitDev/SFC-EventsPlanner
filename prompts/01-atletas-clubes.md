# Prompt 01 — Atletas e Clubes

Lê CLAUDE.md, docs/01-ambito-fase1.md, docs/02-modelo-dominio.md e as skills sfc-contexto e sfc-convencoes.

Quero implementar a gestão de Clubes e Atletas no backoffice (Razor Pages), conforme o modelo de domínio:

- Entidades `Club` e `Athlete` exatamente como definidas em docs/02-modelo-dominio.md — incluindo baseline do record, slug único e `PublicProfileConsent` (ADR-004).
- CRUD completo no backoffice com listagem, pesquisa por nome/clube/disciplina e upload de foto/logo (storage S3-compatible, WebP).
- Regras a testar primeiro (TDD): geração e unicidade de slug; record inicial = baseline; validações de campos obrigatórios.
- UI em pt-PT, responsiva (uso em telemóvel).

Fora de âmbito nesta sessão: eventos, combates, documentos do atleta, rankings, qualquer coisa do portal público. No fim, correr o agent guardiao-ambito e o revisor-dominio antes do merge.
