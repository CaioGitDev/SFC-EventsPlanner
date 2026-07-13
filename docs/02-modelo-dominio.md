# Modelo de domínio — Fase 1

Nomes em inglês (código). Todas as entidades têm `Id`, `OrganizationId`, `CreatedAt`, `UpdatedAt`.

## Entidades

### Organization
- Name, Slug
- Fase 1: existe apenas 1 registo (SFC). Sem UI de gestão.

### Club
- Name, LogoUrl, City, Country, ContactEmail, ContactPhone
- Coaches: lista simples (nome + contacto) — não são utilizadores na Fase 1

### Athlete
- FirstName, LastName, Nickname, Slug (SEO, único)
- PhotoUrl, Nationality, DateOfBirth (nunca exposta publicamente — expor idade calculada)
- ClubId (FK, opcional), CoachName
- Discipline (MuayThai, Kickboxing, K1, Boxing, MMA), WeightClass, WeightKg, HeightCm
- Status (Amateur, Professional), IsActive
- PublicProfileConsent (bool) — sem consentimento, o portal mostra apenas o nome no fight card (ADR-004)
- Record: Wins, Losses, Draws, WinsByKo — **derivado dos Fights concluídos + ajuste manual inicial** (record histórico anterior à plataforma: BaselineWins, BaselineLosses, BaselineDraws, BaselineKos)

### Event
- Name, Slug, Description, Date, Venue, City
- BannerUrl, PosterUrl
- Status (Draft, Published, Completed, Cancelled)
- TicketsUrl (link externo), StreamUrl (YouTube embed)

### Fight
- EventId, Order (int — 1 = abre o evento; o maior é o main event)
- Billing (Main, CoMain, Card)
- Discipline, Rounds, RoundDurationMinutes, WeightClass ou CatchweightKg
- IsTitleFight, IsAmateur
- RedCornerAthleteId, BlueCornerAthleteId
- Status (Scheduled, Completed, Cancelled, NoContest)

### FightResult (1:1 com Fight)
- WinnerAthleteId (null se Draw/NC)
- Method (KO, TKO, UnanimousDecision, SplitDecision, MajorityDecision, Draw, NoContest, Disqualification, Forfeit)
- Round (int, null para decisões), Time (mm:ss, null para decisões)
- **Efeito:** ao gravar, atualiza o record dos dois atletas. Ao corrigir/apagar, reverte.

### WeighIn (por Fight + Athlete)
- FightId, AthleteId, ExpectedWeightKg, OfficialWeightKg, WeighedAt, IsApproved, Notes

### User
- Email, PasswordHash, Role (Admin, Editor), IsActive

## Regras de domínio (testar todas)

1. Um atleta não pode estar nos dois corners do mesmo combate.
2. Um atleta não pode ter dois combates no mesmo evento (V1 — pode mudar em torneios; ADR futuro).
3. Record = baseline + agregação de FightResults concluídos. Nunca editável diretamente após criação.
4. Evento `Published` é visível no portal; `Draft` nunca.
5. Resultado só pode ser registado em evento com data <= hoje.
6. Apagar um Fight com resultado exige reverter o record primeiro (transação).
7. Slugs únicos por entidade; gerados do nome, editáveis, imutáveis depois de publicados.

## Fora da Fase 1 (não modelar ainda)

Rankings, títulos/cinturões, documentos do atleta, matchmaking, notícias, estatísticas derivadas, multi-tenancy real. Não criar tabelas "para o futuro" — YAGNI.
