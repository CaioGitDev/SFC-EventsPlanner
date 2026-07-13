# Roadmap

Regra: **cada fase só avança quando a anterior foi usada num evento real.** Sem datas fixas (part-time); cada fase tem uma "deadline natural": o próximo evento SFC exequível.

## Fase 1 — MVP (substituir o Excel)

Ver `01-ambito-fase1.md`. Marco de saída: 1 evento SFC gerido de ponta a ponta na plataforma.

Ordem de construção (espelhada em `prompts/`):

1. Setup da solução + Docker + CI mínima
2. Atletas + Clubes (backoffice)
3. Eventos + Fight card (backoffice)
4. Resultados + atualização de records
5. Pesagem simples
6. API pública + Portal (home, evento, atleta, resultados)
7. Evento piloto (carga de dados reais, ensaio geral)

## Fase 2 — Operação a sério

- Matchmaking assistido (filtros: peso ±, idade, record, excluir mesmo clube, disponibilidade)
- Gestão documental: licenças, seguros, exames, validades + alertas (RGPD: encriptação, acesso restrito)
- Rankings automáticos por categoria (top 10)
- Pesagem completa (agendamento, check-in no local)
- Acesso de treinadores/clubes (role Coach) — os clubes mantêm os seus atletas

Marco de saída: matchmaker e clubes a usar a plataforma sem intervenção do Caio.

## Fase 3 — Profissionalizar

- Geração de PDFs: fight card oficial, pesagens, credenciais, diplomas
- Comunicação por email (pesagem amanhã, combate confirmado, mudança de horário)
- Estatísticas de atleta (KO%, finish%, streaks) e de evento
- Notícias / CMS simples no portal

## Fase 4 — Diferenciação e SaaS

- Multi-tenant real (onboarding de outras organizações, isolamento, billing)
- Broadcast: overlays, integração OBS/vMix
- Gestão de bastidores (estados do combate em tempo real) + timeline do evento
- Media center (fotos, highlights, replays), Hall of Fame
- SMS/WhatsApp/Push

## Pré-requisito da Fase 4 (decidir antes de investir)

Modelo de negócio: por evento? mensalidade? % bilhética? Sem resposta, a Fase 4 não começa.
