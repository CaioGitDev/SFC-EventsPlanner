# Design — Pesagem simples (Prompt 04)

Data: 2026-07-20 · Estado: decisões tomadas em sessão autónoma — a rever pelo Caio no PR ·
Âmbito: item 6 do backoffice (docs/01-ambito-fase1.md)

## Objetivo

Pesagem por atleta do card: peso esperado, peso oficial, hora, aprovado, observações.
Vista por evento pensada para telemóvel no local (véspera/manhã do evento): introdução
rápida do peso oficial + aprovar. Falha de peso é **assinalada visualmente mas não
bloqueia** — a decisão (catchweight ou cancelamento) é humana e já tem ferramentas
próprias no card (peso combinado, cancelar combate).

## Decisões desta sessão

| Decisão | Escolha |
|---|---|
| Estrutura | `WeighIn` standalone (FightId + AthleteId), criado/atualizado via `EventService` — não mexe no estado de fights/eventos/records, por isso não entra no aggregate `Event`. Invariantes com contexto do fight validadas no service |
| Um por atleta por combate | Índice único `(FightId, AthleteId)` + semântica **upsert** no service (`SaveWeighInAsync` cria ou atualiza) — a vista é uma grelha de introdução rápida, não um CRUD |
| Limite de peso | `Fight.WeightLimitKg` (decimal?, domínio, testado): catchweight → `CatchweightKg`; senão parse do primeiro número de `WeightClass` ("-72kg" → 72, aceita vírgula decimal); string sem número → null (sem deteção de falha) |
| Falha de peso | `OfficialWeightKg > WeightLimitKg` → badge "Falhou o peso" na vista; **nunca bloqueia** gravar nem aprovar (regra do prompt) |
| Peso esperado | Default = `Fight.WeightLimitKg` na primeira gravação; editável (acordos de bout agreement podem diferir) |
| Aprovação | `IsApproved` exige `OfficialWeightKg` preenchido (não se aprova uma pesagem sem peso). Aprovar um atleta acima do limite é permitido (decisão humana) |
| WeighedAt | Definido/atualizado automaticamente (UTC) quando o peso oficial é gravado |
| Estado do evento | Pesagens permitidas em qualquer estado exceto `Cancelled` (coerente com resultados; backfill em Draft/Completed possível) |
| Atleta ∈ combate | Validado no service contra os corners do fight; substituições de atleta mantêm o WeighIn antigo órfão? Não — FK Restrict a Athlete; ao substituir atleta no card, o WeighIn do atleta substituído nesse fight é removido pelo service (a pesagem era desse atleta nesse combate) |
| Weight Results (portal futuro) | `GetWeighInSummaryAsync(eventId)` devolve DTOs (`WeighInRow`: fight order, atleta, categoria/limite, esperado, oficial, hora, aprovado, falhou) — usados já pela vista do backoffice; o portal consumirá o mesmo service no prompt 05. Sem API pública agora |
| UI | Página `Admin/Events/WeighIns/{eventId}`: lista agrupada por combate (ordem do card), uma linha por atleta com input numérico (step 0.05) + botão «Gravar», toggle aprovar, notas; badges Falhou o peso / Aprovado; link «Pesagem» no Edit do evento |

## 1. Domínio

### Fight

- `decimal? WeightLimitKg` — computed: `CatchweightKg` quando definido; senão parse de
  `WeightClass` (primeiro número, aceita `,` ou `.`); null se não parseável

### WeighIn (`Sfc.Domain/Events/WeighIn.cs`)

- `Id`, `OrganizationId`, `FightId`, `AthleteId`, `ExpectedWeightKg?`, `OfficialWeightKg?`,
  `WeighedAt?`, `IsApproved`, `Notes?`, `CreatedAt`, `UpdatedAt`
- Ctor público (organizationId, fightId, athleteId, expectedWeightKg) — pesos positivos
- `RecordOfficialWeight(decimal officialWeightKg, DateTime weighedAtUtc)` — positivo; define `WeighedAt`
- `Approve()` — exige `OfficialWeightKg != null`; `Unapprove()`
- `SetExpectedWeight(decimal?)`, `SetNotes(string?)`
- `bool IsOverweight(decimal? limitKg)` — `OfficialWeightKg > limitKg` (false se algum null)

## 2. Persistência

- `DbSet<WeighIn>`; índice único `(FightId, AthleteId)`; FK Fight cascade, FK Athlete restrict;
  `ExpectedWeightKg`/`OfficialWeightKg` precision (5,2); `Notes` max 1000
- Migration única: `AddWeighIns`

## 3. Service e UI

### EventService

- `record WeighInInput(decimal? OfficialWeightKg, decimal? ExpectedWeightKg, bool IsApproved, string? Notes)`
- `SaveWeighInAsync(eventId, fightId, athleteId, WeighInInput)` → upsert; enum
  `WeighInOperationResult { Success, EventNotFound, FightNotFound, AthleteNotInFight, EventCancelled, ApprovalRequiresWeight, InvalidInput }`
- `GetWeighInSummaryAsync(eventId)` → `List<WeighInRow>` ordenada por fight order + corner
- `ReplaceAthleteAsync` passa a remover o WeighIn do atleta substituído nesse fight (mesma SaveChanges)

### Página `Admin/Events/WeighIns/{eventId}`

- GET: grelha agrupada por combate; POST handler por linha (`OnPostSaveAsync`) com
  fightId/athleteId + campos; TempData de sucesso; erros pt-PT
- Badges: «Falhou o peso» (danger) quando overweight; «Aprovado» (success); «—» sem pesagem
- Link no Edit do evento, secção fight card

## 4. Testes (TDD primeiro)

- **Domínio:** `WeightLimitKg` (catchweight, "-72kg", "72,5 kg", string sem número → null);
  `RecordOfficialWeight` define WeighedAt e rejeita ≤ 0; `Approve` sem peso → throws;
  aprovar acima do limite é permitido; `IsOverweight` matriz (acima/igual/abaixo/nulls)
- **Integração:** upsert (2ª gravação atualiza a mesma linha — índice único respeitado);
  atleta fora do combate → `AthleteNotInFight`; evento cancelado → `EventCancelled`;
  aprovação sem peso → `ApprovalRequiresWeight`; summary devolve linhas ordenadas com
  flag de falha; substituição de atleta remove o WeighIn do substituído
- Smoke test manual no browser (telemóvel viewport)

## Fora desta sessão

Agendamento de pesagens, check-in, notificações, portal público (Weight Results ficam
prontos no service). Antes do PR: `guardiao-ambito`, `revisor-dominio` e `/security-review`.
