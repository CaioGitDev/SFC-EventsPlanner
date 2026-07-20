# Design — API pública read-only (Prompt 05, Parte A)

Data: 2026-07-20 · Estado: decisões tomadas em sessão autónoma — a rever pelo Caio no PR ·
Âmbito: base do portal público (docs/01-ambito-fase1.md); Parte B (portal Next.js) fica
para a sessão seguinte, como o prompt sugere.

## Objetivo

Rotas `/api/public/...` anónimas e read-only com DTOs explícitos, que o portal Next.js
consome via SSG/ISR. Conformidade RGPD (ADR-004) testada primeiro: nunca expor
DateOfBirth (só idade), contactos, IDs internos, eventos Draft, nem pesos de pesagem
não aprovados; atletas sem `PublicProfileConsent` aparecem apenas com o nome.

## Decisões desta sessão

| Decisão | Escolha |
|---|---|
| Forma | Minimal APIs num `MapPublicApi()` (`src/Sfc.Web/Api/`), queries num `PublicContentService` — sem controllers/MVC extra (ADR-001/-005). `[AllowAnonymous]` explícito nas rotas públicas |
| Identificadores | **Slugs como única chave pública**; nenhum GUID em nenhum DTO. Combates identificados pela ordem no card. Respostas 404 para slug inexistente ou Draft |
| Visibilidade de eventos | `Published`, `Completed` e `Cancelled` são públicos (o público precisa de saber que um evento foi cancelado); `Draft` nunca. "Próximo evento" = Published com data mais próxima >= hoje (Europa/Lisboa) |
| Consentimento | Sem `PublicProfileConsent`: no fight card/resultados/pesagem aparece **só o nome** (sem foto, slug, record, idade, nacionalidade, clube — leitura estrita do ADR-004); `/fighters/{slug}` devolve 404 |
| Perfil de atleta | Só atletas ativos com consentimento; inclui idade calculada (nunca DoB), record, últimos 5 combates concluídos (evento, adversário, resultado) e próximo combate agendado em evento Published futuro |
| Resultados | `/events/{slug}/results`: por combate (ordem, atletas, resumo do resultado — vencedor/método/round/tempo, ou estado Cancelado/No contest). Disponível para eventos públicos; combates sem resultado aparecem sem resultado |
| Weight results | `/events/{slug}/weigh-ins`: **apenas pesagens aprovadas** (aprovado = publicado, ADR-004); inclui peso oficial e flag "não fez peso" |
| Revalidação | O monólito **chama** o portal (Next.js on-demand ISR): `PortalRevalidator` (`HttpClient`) POSTa para `Portal:RevalidateUrl` com header secreto `Portal:RevalidateSecret` quando um evento é publicado/despublicado/concluído/cancelado, quando um resultado é gravado/corrigido/apagado e quando uma pesagem é gravada. No-op se não configurado; falhas só logadas (nunca quebram a operação do backoffice). O endpoint Next.js nasce na Parte B |
| Serialização | camelCase (default), datas ISO; sem cache HTTP no monólito (o ISR do portal é a cache) |

## Rotas e DTOs (todas GET, anónimas)

- `GET /api/public/events/next` → `PublicEventSummary?` (204 quando não há próximo)
- `GET /api/public/events` → `{ upcoming: PublicEventSummary[], past: PublicEventSummary[] }` (past = data < hoje, desc)
- `GET /api/public/events/{slug}` → `PublicEventDetail` (summary + descrição, poster, stream/tickets URLs, fight card)
- `GET /api/public/fighters/{slug}` → `PublicFighterProfile`
- `GET /api/public/events/{slug}/results` → `PublicFightResultRow[]`
- `GET /api/public/events/{slug}/weigh-ins` → `PublicWeighInRow[]`

```
PublicEventSummary: name, slug, date, venue, city, status, bannerUrl, posterUrl, ticketsUrl, streamUrl, fightCount
PublicEventDetail: summary + description + fights: PublicFightCardEntry[]
PublicFightCardEntry: order, billing, discipline, rounds, roundDurationMinutes, weightClass, catchweightKg, isTitleFight, isAmateur, status, red: PublicCardAthlete, blue: PublicCardAthlete
PublicCardAthlete: name (sempre); com consentimento: nickname, slug, photoUrl, nationality, age, record ("W-L-D"), clubName
PublicFighterProfile: name, nickname, slug, photoUrl, nationality, age, discipline, status(Amador/Profissional em EN enum name), clubName, record, winsByKo, lastFights: [{ eventName, eventSlug, eventDate, opponentName, opponentSlug?, summary }], nextFight: { eventName, eventSlug, eventDate, opponentName, opponentSlug? }?
PublicFightResultRow: order, billing, red/blue: PublicCardAthlete, status, result?: { winnerCorner ("red"/"blue")?, method, round?, time? }
PublicWeighInRow: order, athleteName, athleteSlug?, corner, weightClass/catchweightKg, officialWeightKg, weighedAt, missedWeight
```

## Testes (TDD primeiro — integração, WebApplicationFactory)

- RGPD: payloads de card/perfil/pesagem **nunca** contêm `dateOfBirth`, emails/телефones, nem GUIDs (verificação por regex ao JSON cru); atleta sem consentimento → só nome (todos os outros campos null) e `/fighters/{slug}` → 404
- Draft: fora da lista, detalhe/resultados/pesagens → 404; Cancelled visível com status
- Próximo evento: escolhe o Published futuro mais próximo; 204 sem candidatos
- Pesagens: só aprovadas; flag `missedWeight` correta
- Perfil: últimos combates e próximo combate corretos; idade calculada
- Revalidator: publicar/gravar resultado dispara POST com o secret (handler fake); sem config → sem chamadas; falha do portal não afeta a operação

## Fora desta sessão

Portal Next.js (Parte B), notícias, rankings, estatísticas, pesquisa.
Antes do PR: `guardiao-ambito`, `revisor-dominio` e `/security-review`.
