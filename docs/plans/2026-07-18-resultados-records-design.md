# Design — Resultados e Records (Prompt 03)

Data: 2026-07-18 · Estado: decisões tomadas em sessão autónoma — a rever pelo Caio no PR ·
Âmbito: registo de resultados + atualização automática de records (docs/01-ambito-fase1.md)

## Objetivo

Registar resultados de combates e manter os records dos atletas automaticamente
(record apresentado = baseline + agregação de resultados na plataforma), com correção
e remoção de resultados a reverter/reaplicar efeitos numa transação. UI de registo
otimizada para o dia do evento (telemóvel, poucos toques, confirmação antes de gravar).

## Decisões desta sessão

| Decisão | Escolha |
|---|---|
| Base | Branch `feature/resultados-records` a partir de `feature/eventos-fightcard` (PR #4 pendente de merge — bloqueado nesta sessão; ao fazer merge o PR novo passa a comparar só com o delta) |
| Record armazenado vs calculado | **Contadores agregados armazenados no Athlete** (`ResultWins/ResultLosses/ResultDraws/ResultKos`), atualizados transacionalmente ao gravar/corrigir/apagar resultado. `Wins = BaselineWins + ResultWins` etc. Leituras baratas (listas, picker de atletas já mostra cartel) e corresponde à linguagem do modelo ("ao gravar atualiza; ao corrigir/apagar reverte") |
| Efeitos por método | Tabela da skill sfc-contexto: KO/TKO → vencedor +1W +1KO, derrotado +1L; Decisões (unânime/dividida/maioria) → +1W/+1L; Draw → +1D ambos; No Contest → sem alteração; DQ → +1W/+1L; Forfeit → +1W/+1L |
| Cálculo dos deltas | `FightResult.GetDeltas(redId, blueId)` devolve `RecordDelta` por atleta; reverter = aplicar delta negado. Matriz de efeitos testada método a método |
| Round/Time por método | KO/TKO: `Round` obrigatório (1..Rounds do combate), `Time` opcional (`m:ss`, seg < 60); DQ: round/time opcionais; Decisões, Draw, No Contest, Forfeit: **sem** round/time (decisões vão à distância; forfeit é desistência/walkover sem momento no relógio) |
| Vencedor por método | KO/TKO/Decisões/DQ/Forfeit exigem `WinnerAthleteId` ∈ {canto vermelho, canto azul}; Draw e No Contest exigem `WinnerAthleteId == null` |
| Onde vive a operação | `Event` continua aggregate root: `RecordResult`, `ChangeResult`, `DeleteResult`, `CancelFight`, `MarkFightNoContest`, `ReinstateFight`. O `EventService` carrega os dois atletas e aplica os deltas na mesma `SaveChanges` (transação única) |
| Regra "data <= hoje" | Verificada no domínio com `today` injetado (`DateOnly`); o service calcula hoje em Europa/Lisboa. Aplica-se só ao **registo** — corrigir/apagar não re-verifica (o evento já aconteceu) |
| Estado do evento | Resultados permitidos em qualquer estado exceto `Cancelled` (permite backfill de eventos históricos em Draft e correções em Completed). O guard `EnsureCardEditable` (Completed/Cancelled) continua a aplicar-se só às operações de card |
| FightStatus | `RecordResult` → `Completed` (ou `NoContest` se método NC); `DeleteResult` → volta a `Scheduled`; `CancelFight`/`MarkFightNoContest` só em `Scheduled` e sem resultado; `ReinstateFight` reverte Cancelled/NoContest-sem-resultado para `Scheduled` (recuperação de toque errado no dia do evento) |
| Apagar fight com resultado | Bloqueado com mensagem amigável — apagar primeiro o resultado (regra 6 do modelo, tornada explícita em vez de reversão automática escondida) |
| Apagar evento com resultados | `EventDeleteResult.HasResults` — bloqueado até apagar os resultados (o cascade delete iria deixar records inconsistentes) |
| Time em BD | String `m:ss` validada no domínio (varchar(5)); sem aritmética de tempo na Fase 1 — YAGNI |
| UI | Página `Admin/Events/Fights/Result/{fightId}`: botões grandes (canto vermelho / canto azul / empate / no contest), método, round/tempo condicionais; **passo de confirmação server-side** com resumo antes de gravar; mesma página mostra/corrige/apaga resultado existente. Ações Cancelar/No Contest/Reativar como POST handlers no Edit do evento com `confirm()` |

## 1. Domínio (`Sfc.Domain/Events/` e `Sfc.Domain/Athletes/`)

### FightResultMethod

`enum FightResultMethod { Ko, Tko, UnanimousDecision, SplitDecision, MajorityDecision, Draw, NoContest, Disqualification, Forfeit }`

### FightResult (1:1 com Fight)

- `Id`, `OrganizationId`, `FightId`, `WinnerAthleteId?`, `Method`, `Round?` (int), `Time?` (`m:ss`), `CreatedAt`, `UpdatedAt`
- Criado/alterado apenas através do aggregate `Event` (ctor e mutators `internal`), que valida
  com o contexto do `Fight` (corners, nº de rounds)
- Validações: coerência vencedor↔método; round/time conforme a tabela de decisões acima;
  `Round` ∈ 1..`fight.Rounds`; `Time` no formato `m:ss` com segundos < 60
- `GetDeltas(redId, blueId)` → `(RecordDelta red, RecordDelta blue)` segundo a matriz de efeitos

### RecordDelta (value object, `Sfc.Domain.Athletes`)

- `record RecordDelta(int Wins, int Losses, int Draws, int Kos)` + `Negate()`
- Aplicado pelo Athlete; NC → delta zero para ambos

### Athlete

- Novos contadores privados persistidos: `ResultWins`, `ResultLosses`, `ResultDraws`, `ResultKos`
- `ApplyResultDelta(RecordDelta)` — soma; lança se algum contador ficasse negativo
- `Wins => BaselineWins + ResultWins` (idem Losses/Draws/WinsByKo); `RecordDisplay` inalterado
- Regra 3 do modelo mantém-se: nunca editável diretamente

### Event (aggregate)

- `FightResult RecordResult(Guid fightId, Guid? winnerAthleteId, FightResultMethod method, int? round, string? time, DateOnly today)`
  — exige evento não-Cancelled, `Date.Date <= today`, fight `Scheduled`; define `FightStatus`
- `FightResult ChangeResult(fightId, winner?, method, round?, time?)` — exige resultado existente; re-deriva `FightStatus` (Completed↔NoContest)
- `void DeleteResult(fightId)` — remove o resultado, fight volta a `Scheduled`
- `void CancelFight(fightId)` / `void MarkFightNoContest(fightId)` — só `Scheduled`, sem resultado
- `void ReinstateFight(fightId)` — `Cancelled`/`NoContest` sem resultado → `Scheduled`
- Nenhuma destas operações passa por `EnsureCardEditable` (são operações de resultado, não de card)

## 2. Persistência (`Sfc.Infrastructure`)

- `DbSet<FightResult>`; índice único `FightId`; FK Fight→FightResult cascade (o guard de
  domínio/service impede apagar fight com resultado sem reverter primeiro); FK
  `WinnerAthleteId` → Restrict
- `Fight.Result` navegação 1:1 (backing field não necessário — propriedade com setter privado)
- 4 colunas int novas em Athletes (default 0)
- Migration única: `AddFightResults`

## 3. Service e UI (`Sfc.Web`)

### EventService

- `RecordResultAsync(eventId, fightId, ResultInput)` / `ChangeResultAsync(...)`:
  carrega evento+fights+resultado e os dois atletas do combate; reverte deltas antigos
  (correção), aplica novos, tudo numa única `SaveChanges` (transação); mapeia exceções de
  domínio para mensagens pt-PT
- `DeleteResultAsync(eventId, fightId)` — reverte deltas e remove
- `CancelFightAsync` / `MarkFightNoContestAsync` / `ReinstateFightAsync`
- "Hoje" calculado em Europa/Lisboa (`TimeZoneInfo`), na fronteira do service
- `RemoveFightAsync` passa a devolver `HasResult` quando o combate tem resultado
- `DeleteAsync` de evento passa a devolver `HasResults` quando algum combate tem resultado

### Páginas

- `Fights/Result/{fightId}` — fluxo em dois passos no mesmo handler POST:
  1. Escolha: quem venceu (dois cartões grandes com nome/alcunha, ou Empate / No Contest),
     método (rádios), round + tempo quando aplicável (mostrados por método via HTML `data-*`
     + JS mínimo inline, sem libs)
  2. Confirmação: resumo legível ("Vitória de X por KO, round 2, 1:34") com campos hidden +
     botões «Confirmar» / «Voltar»
  - Com resultado existente: mostra o resumo atual, permite corrigir (mesmo fluxo) e
    «Apagar resultado» (confirmação)
- `Edit` do evento: cada linha do card mostra o resumo do resultado (ou estado
  Cancelado/No Contest); botões «Resultado» (Scheduled/Completed/NoContest), «Cancelar
  combate» / «No contest» (Scheduled), «Reativar» (Cancelled/NoContest sem resultado)
- Vocabulário pt-PT: Vitória, Empate, No contest (aceite), KO/TKO, Decisão unânime/dividida/por maioria, Desqualificação, Desistência

## 4. Testes (TDD primeiro)

- **Domínio:** matriz de efeitos por método (os dois atletas, deltas certos, NC zero);
  validações de FightResult (vencedor obrigatório/proibido, vencedor tem de ser um dos
  corners, round/time por método, round dentro do limite, formato de tempo);
  `ApplyResultDelta` (soma, reversão, guarda de negativos); `RecordResult` (data futura
  rejeitada, evento cancelado rejeitado, fight não-Scheduled rejeitado, status derivado);
  `ChangeResult`/`DeleteResult` (status re-derivado, volta a Scheduled);
  Cancel/NoContest/Reinstate (transições válidas e inválidas);
  `Wins/Losses/Draws/WinsByKo` = baseline + agregação
- **Integração (service):** gravar resultado atualiza os dois atletas; corrigir reverte o
  antigo e aplica o novo atomicamente (verificar contadores finais); apagar reverte; NC não
  altera; correção KO→Draw e Draw→KO (casos de reversão cruzada); `RemoveFightAsync` →
  `HasResult`; delete de evento → `HasResults`; data futura → erro amigável
- Smoke test manual no fim (browser)

## Fora desta sessão

Estatísticas derivadas (KO%, streaks), rankings, portal público, pesagens.
Antes do PR: `guardiao-ambito`, `revisor-dominio` (atenção especial às correções de
resultados) e `/security-review`.
