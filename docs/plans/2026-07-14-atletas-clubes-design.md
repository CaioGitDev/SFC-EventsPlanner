# Design — Gestão de Clubes e Atletas (Prompt 01)

Data: 2026-07-14 · Estado: aprovado pelo Caio · Âmbito: itens 1, 2 e parte do 7 do backoffice (docs/01-ambito-fase1.md)

## Objetivo

CRUD completo de Clubes e Atletas no backoffice (Razor Pages), com upload de foto/logo,
pesquisa, autenticação mínima e as regras de domínio testadas primeiro (TDD).

## Decisões desta sessão

| Decisão | Escolha |
|---|---|
| Autenticação | Login mínimo já nesta sessão: cookie auth + página de login + `[Authorize]` na área Admin |
| Slug de atleta | Editável durante toda a Fase 1; imutabilidade só será aplicada quando o portal for lançado (prompt 05) |
| Estrutura | Domínio rico + services de aplicação em `Sfc.Web`; Razor Pages finas (sem repositórios — ADR-001) |
| WeightClass | String livre (categorias variam por disciplina/federação; enum seria prematuro) |
| Coaches do clube | Value objects `Coach { Name, Contact? }` em coluna JSON (`OwnsMany().ToJson()`) |
| CSS do backoffice | Bootstrap 5 servido localmente (sem build step); Tailwind fica só no portal |

## 1. Domínio (`Sfc.Domain`)

### Club (`Clubs/Club.cs`)

- `Id`, `OrganizationId` (`IOrganizationScoped`), `CreatedAt`, `UpdatedAt`
- `Name` (obrigatório), `LogoUrl?`, `City?`, `Country?`, `ContactEmail?`, `ContactPhone?`
- `Coaches`: lista de `Coach { Name, Contact? }` — não são utilizadores (Fase 1)
- Construtor valida invariantes; `Update(...)` para edição; `SetLogo(url)`

### Athlete (`Athletes/Athlete.cs`)

- `Id`, `OrganizationId`, `CreatedAt`, `UpdatedAt`
- Obrigatórios: `FirstName`, `LastName`, `DateOfBirth`, `Nationality`, `Discipline`, `Status`
- Opcionais: `Nickname`, `PhotoUrl`, `Slug` (gerado se vazio), `ClubId` (FK), `CoachName`,
  `WeightClass` (string), `WeightKg`, `HeightCm`
- `IsActive` (default `true`), `PublicProfileConsent` (default `false` — ADR-004)
- Baseline do record: `BaselineWins/Losses/Draws/Kos` — definido **apenas no construtor**;
  nenhum método de update lhe toca (regra 3 do modelo de domínio)
- Record exposto (`Wins`, `Losses`, `Draws`, `WinsByKo`): baseline + agregação de fights
  concluídos; nesta sessão (sem fights) record == baseline. Sem setters públicos.

### SlugGenerator (`Common/SlugGenerator.cs`)

Função pura: minúsculas, remoção de diacríticos, não-alfanuméricos → hífen, colapso e trim
de hífens. Unicidade é responsabilidade do service (sufixo `-2`, `-3`, … em colisão).

### Enums

- `Discipline { MuayThai, Kickboxing, K1, Boxing, Mma }`
- `AthleteStatus { Amateur, Professional }`

## 2. Persistência (`Sfc.Infrastructure`)

- `DbSet<Club>`, `DbSet<Athlete>`; query filter por `OrganizationId` aplica-se por convenção
- Índice único `(OrganizationId, Slug)` em `Athletes`
- `Coaches` como coluna JSON
- FK `Athlete.ClubId` → `DeleteBehavior.Restrict`; o service bloqueia delete de clube com
  atletas e devolve mensagem amigável
- Atleta: desativar (`IsActive = false`) é o caminho normal; hard delete permitido com
  confirmação (RGPD — ainda não existem fights que o impeçam)
- Migration única: `AddClubsAndAthletes`

## 3. Storage de imagens (`Sfc.Infrastructure`)

- `IImageStorage`: `SaveAsync(stream, key) → url`, `DeleteAsync(key)`
- Implementação S3-compatible (`AWSSDK.S3`): MinIO em dev (docker-compose existente),
  Cloudflare R2 em prod; config em `Storage:*` (endpoint, bucket, credenciais via
  user-secrets/env — nunca no código)
- `ImageProcessor` (SixLabors.ImageSharp): valida imagem real; redimensiona
  (atleta max 800px, logo max 400px, proporção mantida); converte para WebP q~80;
  limite de upload 10 MB
- Keys determinísticas: `athletes/{id}.webp`, `clubs/{id}.webp` no bucket `sfc-media`;
  substituição reutiliza a key (sem órfãos)

## 4. Backoffice (`Sfc.Web`)

- Auth: `SignInManager` + cookie; `Pages/Account/Login.cshtml` (+ logout);
  `AuthorizeFolder("/Admin")`. Admin e Editor acedem ambos a Clubes/Atletas.
- Páginas: `Pages/Admin/Clubs/{Index,Create,Edit,Delete}` e
  `Pages/Admin/Athletes/{Index,Create,Edit,Delete}`
- Index de atletas: pesquisa combinável por nome (`ILIKE`), clube e disciplina; paginação simples
- `ClubService` / `AthleteService` injetados; PageModels finos
- UI em pt-PT, responsiva (telemóvel); Bootstrap 5 local; validação server-side + HTML5 nativa (scripts jQuery de validação adiados até haver necessidade real)

## 5. Testes (TDD primeiro)

- **Domínio (xUnit, sem mocks):** `SlugGenerator` (diacríticos, espaços, pontuação, vazio);
  `Athlete` (record inicial = baseline; baseline imutável após criação; obrigatórios);
  `Club` (obrigatórios; coaches)
- **Integração (WebApplicationFactory + Testcontainers PostgreSQL):** unicidade de slug com
  sufixo; CRUD de clube/atleta; bloqueio de delete de clube com atletas; redirect para login.
  `IImageStorage` fake em memória nos testes.

## Fora desta sessão

Eventos, combates, documentos do atleta, rankings, portal público, revalidação on-demand.
Antes do PR: `guardiao-ambito`, `revisor-dominio` e `/security-review`.
