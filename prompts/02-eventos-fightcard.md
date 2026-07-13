# Prompt 02 — Eventos e Fight Card

Lê CLAUDE.md, docs/01-ambito-fase1.md, docs/02-modelo-dominio.md e as skills sfc-contexto e sfc-convencoes.

Quero implementar Eventos e o Fight Card no backoffice:

- Entidades `Event` e `Fight` conforme docs/02-modelo-dominio.md (estados do evento, billing, corners, catchweight).
- CRUD de eventos com upload de banner/poster e campos TicketsUrl/StreamUrl.
- Gestão do fight card dentro da página do evento: adicionar/remover/reordenar combates, escolher atletas por pesquisa.
- Regras a testar primeiro (TDD): atleta não pode estar nos dois corners; atleta não pode ter dois combates no mesmo evento; só eventos Published são públicos; reordenação mantém consistência do billing.
- Suportar substituição de atleta num combate agendado (realidade operacional — ver skill sfc-contexto).

Fora de âmbito: resultados, pesagens, portal público, PDFs, matchmaking. No fim, correr guardiao-ambito e revisor-dominio.
