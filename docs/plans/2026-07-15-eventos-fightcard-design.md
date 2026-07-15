# Design — Eventos e Fight Card (Prompt 02)

Data: 2026-07-15 · Estado: aprovado pelo Caio · Âmbito: itens 3 e 4 do backoffice (docs/01-ambito-fase1.md)

## Objetivo

CRUD de Eventos com banner/poster e gestão do fight card dentro da página do evento
(adicionar/remover/reordenar combates, escolher atletas por pesquisa, substituição de
atleta), com as regras de domínio testadas primeiro (TDD).

## Decisões desta sessão

| Decisão | Escolha |
|---|---|
| Base | PR #3 merged na master antes de começar; branch `feature/eventos-fightcard` a partir de master |
| Billing | Derivado da posição: último Order = Main, penúltimo = CoMain (se ≥2), resto Card; recalculado a cada alteração do card — nunca editado à mão |
| Estrutura do card | `Event` é aggregate root dos `Fights`; operações e invariantes como métodos de domínio |
| Data do evento | `DateTime` UTC com hora (o countdown do portal precisa da hora) |
| Slug de evento | Gerado do nome, editável enquanto `PublishedAt == null`; imutável após a primeira publicação (regra 7) |
| Peso do combate | `WeightClass` (string) XOR `CatchweightKg` (decimal) — exatamente um |
| Substituição | `ReplaceAthlete(fightId, corner, novoAtletaId)` apenas em combates `Scheduled` |
| Reordenação UI | Botões ↑/↓ (server-side, mobile-friendly, sem libs JS) |
| Seleção de atletas | Dois `<select>` nativos de atletas ativos + filtro server-side (nome/clube/disciplina) por GET |

## 1. Domínio (`Sfc.Domain/Events/`)

### Event

- `Id`, `OrganizationId` (`IOrganizationScoped`), `CreatedAt`, `UpdatedAt`
- `Name` (obrig.), `Slug` (único por org), `Description?`, `Date` (DateTime UTC), `Venue?`,
  `City?`, `BannerUrl?`, `PosterUrl?`, `TicketsUrl?`, `StreamUrl?`
- `Status` (`EventStatus { Draft, Published, Completed, Cancelled }`), default Draft
- `PublishedAt?` — definido na primeira publicação; ancora a imutabilidade do slug
- Transições: `Publish()` Draft→Published (define `PublishedAt` na primeira vez);
  `Unpublish()` Published→Draft; `Complete()` Published→Completed;
  `Cancel()` Draft/Published→Cancelled. Transições inválidas lançam `InvalidOperationException`.
- `Update(...)` para campos editáveis (não toca em slug/status); `UpdateSlug` só com
  `PublishedAt == null`; `SetBanner(url)` / `SetPoster(url)`
- **Fight card (aggregate):** `IReadOnlyList<Fight> Fights` (ordenado por `Order`);
  `AddFight(...)`, `RemoveFight(fightId)`, `MoveFight(fightId, MoveDirection.Up/Down)`,
  `ReplaceAthlete(fightId, Corner.Red/Blue, novoAtletaId)`
- Invariantes do card (todas no domínio):
  1. Atleta não pode estar nos dois corners do mesmo combate (regra 1 do modelo)
  2. Atleta não pode ter dois combates no mesmo evento (regra 2) — verificado em Add e Replace
  3. `Order` contíguo 1..N (Add no fim; Remove fecha o buraco; Move troca com o vizinho)
  4. `RecalculateBilling()` após cada alteração: último = Main, penúltimo = CoMain (se ≥2), resto Card
  5. `ReplaceAthlete` exige `Fight.Status == Scheduled`

### Fight

- `Id`, `OrganizationId`, `EventId`, `Order`, `Billing` (`FightBilling { Main, CoMain, Card }`,
  setter interno ao aggregate), `Discipline`, `Rounds` (1–12), `RoundDurationMinutes` (1–10),
  `WeightClass?` XOR `CatchweightKg?` (exatamente um — validado), `IsTitleFight`, `IsAmateur`,
  `RedCornerAthleteId` + `RedCornerAthlete?`, `BlueCornerAthleteId` + `BlueCornerAthlete?`,
  `Status` (`FightStatus { Scheduled, Completed, Cancelled, NoContest }`, default Scheduled —
  os outros estados só ganham uso no prompt 03), `CreatedAt`, `UpdatedAt`
- Criado apenas via `Event.AddFight`; corners alterados apenas via `Event.ReplaceAthlete`

## 2. Persistência (`Sfc.Infrastructure`)

- `DbSet<Event>`, `DbSet<Fight>`; índice único `(OrganizationId, Slug)` em Events;
  índice `EventId` em Fights (não único em Order — a contiguidade é garantida pelo domínio;
  índice único criaria conflitos transitórios na reordenação)
- `Event.Fights` com backing field, cascade delete do evento para os fights
- FKs `Fight.RedCornerAthleteId`/`BlueCornerAthleteId` → `DeleteBehavior.Restrict` —
  passa a proteger atletas com combates (resolve o aviso do revisor-dominio do prompt 01)
- `AthleteService.DeleteAsync` passa a devolver `AthleteDeleteResult { Deleted, NotFound, HasFights }`
  com pré-verificação amigável (padrão do clube); página Delete de atleta mostra a mensagem
- Delete de evento: permitido só em Draft/Cancelled (`EventDeleteResult { Deleted, NotFound, NotDeletable }`);
  um Published tem de ser cancelado primeiro. Banner/poster apagados do storage no delete.
- Migration única: `AddEventsAndFights`

## 3. Services e UI (`Sfc.Web`)

### EventService

- `SearchAsync(name?, EventStatus?)` — ILIKE no nome, ordenado por Date desc
- `GetWithCardAsync(id)` — evento + fights (ordenados) + atletas dos corners
- `CreateAsync`/`UpdateAsync` com uploads: banner max 1920px, poster max 1200px, WebP q80,
  keys `events/{id}-banner.webp` / `events/{id}-poster.webp` (determinísticas, substituição reutiliza key)
- Transições: `PublishAsync`/`UnpublishAsync`/`CompleteAsync`/`CancelAsync` (mapeiam exceções
  de transição inválida para mensagens pt-PT nas páginas)
- Card: `AddFightAsync(eventId, FightInput)`, `RemoveFightAsync`, `MoveFightAsync`,
  `ReplaceAthleteAsync` — carregam o aggregate, delegam no domínio, SaveChanges
- `EventDeleteResult` como acima

### Páginas (`Pages/Admin/Events/`)

- `Index` — pesquisa por nome + filtro por estado; badges pt-PT (Rascunho/Publicado/Concluído/Cancelado)
- `Create` — dados base + uploads
- `Edit` — página central: dados/uploads, botões de transição de estado, e secção
  **Fight card**: lista ordenada (1 abre a noite; último = Combate principal) com badge de
  billing, ações ↑/↓ (POST handlers `OnPostMoveUp/DownAsync`), "Substituir" (só Scheduled),
  "Remover" (confirmação), link "Adicionar combate"
- `Delete` — confirmação; bloqueado com mensagem quando não Draft/Cancelled
- `Fights/Add` (`Admin/Events/Fights/Add/{eventId}`) — formulário do combate: disciplina,
  rounds/duração (defaults por estatuto: amador 3×2, profissional 3×3), categoria de peso XOR
  peso combinado, título s/n, amador s/n, e **dois selects nativos** de atletas ativos
  ("Apelido, Nome 'Alcunha' — cartel, clube") com filtro GET (nome/clube/disciplina) acima
- `Fights/Replace/{fightId}` — escolher canto (vermelho/azul) + novo atleta (mesmo seletor)
- Vocabulário pt-PT: Combate, Canto vermelho/azul, Combate principal, Peso combinado, Cartaz

## 4. Testes (TDD primeiro)

- **Domínio (xUnit, sem mocks):** transições de estado (válidas e inválidas); slug imutável
  após primeira publicação; XOR WeightClass/Catchweight; AddFight rejeita mesmo atleta nos
  dois corners e atleta já no evento; MoveFight mantém contiguidade e rederiva billing
  (casos com 1, 2 e N combates); RemoveFight fecha a ordem; ReplaceAthlete valida Scheduled
  e unicidade no evento
- **Integração (WebApplicationFactory + Testcontainers):** CRUD de evento com uploads (fake
  storage); fluxo do card add→move→replace→remove via service; unicidade de slug de evento;
  `AthleteService.DeleteAsync` → `HasFights`; delete de evento Published bloqueado
- Smoke test manual no fim (browser + MinIO real), como no prompt 01

## Fora desta sessão

Resultados, pesagens, portal público, PDFs, matchmaking, rankings.
Antes do PR: `guardiao-ambito`, `revisor-dominio` e `/security-review`.
