# Prompt 04 — Pesagem simples

Lê CLAUDE.md, docs/02-modelo-dominio.md e as skills sfc-contexto e sfc-convencoes.

Quero implementar a pesagem simples:

- Entidade `WeighIn` conforme o modelo (peso esperado, peso oficial, hora, aprovado, notas).
- Vista de pesagem por evento: lista de todos os atletas do card, introdução rápida do peso oficial + aprovar, pensada para telemóvel no local.
- Regras a testar primeiro: peso oficial acima do limite da categoria assinala visualmente falha de peso (mas não bloqueia — a decisão é humana: catchweight ou cancelamento); um WeighIn por atleta por combate.
- Preparar os dados de "Weight Results" para consumo futuro pelo portal (sem construir o portal ainda).

Fora de âmbito: agendamento de pesagens, check-in, notificações, portal público. No fim, correr guardiao-ambito e revisor-dominio.
