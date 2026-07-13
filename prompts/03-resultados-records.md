# Prompt 03 — Resultados e Records (núcleo do domínio)

Lê CLAUDE.md, docs/02-modelo-dominio.md e as skills sfc-contexto e sfc-convencoes. Esta é a lógica mais crítica do sistema — TDD rigoroso, sem exceções.

Quero implementar o registo de resultados e a atualização automática de records:

- Entidade `FightResult` conforme o modelo (métodos, round, tempo).
- Tabela de efeitos no record por método: ver skill sfc-contexto (KO/TKO/Decisão/Draw/NC/DQ/Forfeit).
- Regras a testar primeiro: cada método atualiza os dois atletas corretamente; corrigir um resultado reverte o anterior e aplica o novo numa transação; apagar resultado reverte; NC não altera records; resultado só em eventos com data <= hoje; record apresentado = baseline + agregação.
- UI de registo otimizada para o dia do evento: poucos toques, confirmação antes de gravar, utilizável em telemóvel.
- Marcar combates como Cancelled/NoContest sem resultado.

Fora de âmbito: estatísticas derivadas (KO%, streaks), rankings, portal público. No fim, correr guardiao-ambito e revisor-dominio — pedir ao revisor-dominio atenção especial aos casos de correção de resultados.
