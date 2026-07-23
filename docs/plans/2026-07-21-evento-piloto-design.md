# Design — Evento piloto / ensaio geral (Prompt 06)

Data: 2026-07-21 · Estado: desenhado com o Caio em sessão · Âmbito: nenhuma funcionalidade
nova — preparação, ensaio e correção de fricções (docs/01-ambito-fase1.md)

## Objetivo

Provar o critério de saída da Fase 1: **um evento SFC gerido de ponta a ponta, sem Excel e
sem intervenção técnica de emergência.** Esta sessão não entrega funcionalidades; entrega
um importador, um ensaio geral medido, backup/restore testado, um runbook e uma lista de
fricções classificada.

## Decisões desta sessão

| Decisão | Escolha |
|---|---|
| Origem dos dados | Não há Excel da SFC disponível → **dados de mock** em CSV, produzidos por nós |
| Forma da carga | Comando de importação (`-- import`) sobre CSV commitado, via `ClubService`/`AthleteService` — não um seeder hardcoded nem SQL direto. Quando o Excel real aparecer, exporta-se para os mesmos cabeçalhos e corre-se o mesmo comando |
| Dimensão | ~10 clubes, ~45 atletas, 2 eventos históricos concluídos com resultados, 1 evento piloto |
| Fronteira do automático | O importador cria o **pano de fundo** (clubes, atletas, eventos históricos). O **evento piloto é criado à mão no backoffice pelo Caio** — é esse fluxo que está a ser testado |
| Medição dos 30s | Claude mede o **custo estrutural** (page loads, campos, cliques, tempo de resposta); o cronómetro humano é do Caio. Não se inventa uma medição de velocidade humana a partir de automação |
| Produção | Backup/restore reais contra o Postgres local + runbook. **Sem provisionar cloud** — a escolha de VPS/Vercel fica em aberto, registada no runbook |
| Correções | Só o que o âmbito já promete. O que exigir editar `01-ambito-fase1.md` vai para `ideias-parqueadas.md` |

## Sequência

A ordem não é arbitrária — cada passo depende do anterior ter deixado dados na base:

1. Importador + CSV de mock (§1) → pano de fundo na base
2. Ensaio geral (§2): evento piloto à mão, com os três desvios, medido
3. Backup/restore (§3) → só agora tem dados reais para verificar
4. Lista de fricções (§4) → sai do que doeu em 2 e 3
5. `guardiao-ambito` classifica; só depois se corrige

## 1. Importador (`-- import`)

Verbo de linha de comandos em `Program.cs`, que corre e sai sem levantar o servidor:

```
dotnet run --project src/Sfc.Web -- import ./data/seed [--dry-run]
```

- Lê, por ordem: `clubs.csv` → `athletes.csv` → `events.csv` → `fights.csv` → `results.csv`
- Passa pelos **services** (`ClubService`, `AthleteService`, `EventService`), nunca pelo
  `DbContext` — para exercitar geração e colisão de slugs, validações de `Athlete` e a
  agregação de records por `ApplyResultDelta`
- `--dry-run` valida e relata sem escrever
- **Idempotente:** clube existente (por nome) e atleta existente (por nome + data de
  nascimento) são saltados e contados; reimportar não duplica. A chave do atleta não é o
  slug porque o serviço resolve colisões acrescentando sufixos (`joao-silva-2`)
- Erros reportam **número de linha e ficheiro**; `club_name` desconhecido é erro, não um
  atleta órfão criado em silêncio
- Relatório final: criados / saltados / erros, por ficheiro

### Contrato de colunas (`snake_case`)

- `clubs.csv` — `name`, `city`, `country`, `contact_email`, `contact_phone`, `coaches` (`|`)
- `athletes.csv` — `first_name`, `last_name`, `nickname`, `date_of_birth`, `nationality`,
  `club_name`, `coach_name`, `discipline`, `weight_class`, `weight_kg`, `height_cm`,
  `status`, `public_profile_consent`, `baseline_wins`, `baseline_losses`, `baseline_draws`,
  `baseline_kos`
- `events.csv` — `name`, `slug`, `date`, `venue`, `city`, `status`
- `fights.csv` — `event_slug`, `order`, `discipline`, `rounds`,
  `round_duration_minutes`, `weight_class`, `catchweight_kg`, `is_title_fight`,
  `is_amateur`, `red_athlete_slug`, `blue_athlete_slug`
  (**sem coluna de billing**: `Event.RecalculateBilling()` deriva main/co-main da ordem)
- `results.csv` — `event_slug`, `fight_order`, `winner_slug`, `method`, `round`, `time`

Ligações por **nome/slug**, não por GUID — é o que um humano consegue produzir a partir de
um Excel.

### Composição do dataset (deliberadamente desconfortável)

O mock não pode ser bonito demais, ou o ensaio não prova nada:

- Maioria amadores, minoria profissionais (realidade de uma gala SFC)
- **3 a 4 menores com `public_profile_consent = false`** e nota de consentimento de tutor
  em `Notes` — é o que faz o ensaio tocar no caminho RGPD (ADR-004)
- Alguns sem nickname, alguns sem `weight_class`, alguns com baseline a zeros (estreantes)
- **Nenhuma foto** — o portal é inspirado na ONE Championship (fotos frente a frente); um
  card real sem fotos é uma fricção verdadeira da SFC e tem de aparecer na lista
- Nacionalidades maioritariamente `Portugal`, com 2–3 estrangeiros

## 2. Ensaio geral

### Duas passagens

**Estrutural (Claude).** Por cada registo de resultado: page loads, campos obrigatórios,
cliques desde a lista do card até «guardado», e tempo de resposta de cada POST (logs do
servidor). Viewport 375×812 nas páginas do dia do evento — `Admin/Events/WeighIns.cshtml` e
`Admin/Events/Fights/Result.cshtml`, usadas de pé, num pavilhão, com rede fraca.

**Humana (Caio).** Evento piloto do zero: criar, montar 12 combates, publicar, registar
pesagens, registar os 12 resultados em sequência com cronómetro. Meta: < 30s por combate.

Nota de máquina: os screenshots do painel de browser rebentam por timeout nesta máquina —
a verificação do Claude é por leitura de texto/DOM, não por imagem.

### Guião: os três desvios obrigatórios

O ensaio **não** segue o caminho feliz. A meio força-se o que acontece sempre numa gala:

1. **Falha de peso** num atleta → obriga a decidir entre catchweight e cancelamento
2. **Substituição de última hora** num combate
3. **Resultado registado errado** e corrigido → o record do atleta tem de reverter

### Lighthouse

`npm run build && npm start` no portal (parar o dev server antes — o `.next` fica bloqueado
se ambos correrem), depois `npx lighthouse` em modo mobile contra `/`, `/events/{slug}`,
`/fighters/{slug}` e `/events/{slug}/results`. Chrome confirmado nesta máquina.

## 3. Backup, restore e runbook

`scripts/backup.ps1` — `docker compose exec postgres pg_dump -Fc` →
`backups/sfc_events_<timestamp>.dump`
`scripts/restore.ps1` — recebe o dump, faz `DROP DATABASE` / `CREATE DATABASE` /
`pg_restore`

**O teste corre depois do ensaio**, com o evento piloto já registado: dump → destruir a
base → restaurar → confirmar por query que os 12 resultados, os records agregados e as
pesagens sobreviveram. Um restore não verificado contra dados reais não é um restore
testado (requisito de `docs/03-arquitetura.md:49`).

`DROP DATABASE` em vez de apagar `docker-volumes/postgres/`: reversível e não arrisca o
volume. As queries de verificação correm pela ferramenta **Bash** — aspas duplas em `psql`
via `docker exec` são consumidas pelo PowerShell nesta máquina.

`docs/runbook.md`: arranque local, backup, restore, variáveis de ambiente do backoffice
(`Portal:RevalidateUrl`, `Portal:RevalidateSecret`) e do portal (`SFC_API_BASE`,
`PORTAL_REVALIDATE_SECRET`, `NEXT_PUBLIC_IMAGE_HOST`, `NEXT_PUBLIC_SITE_URL`), e o alvo de
deploy **explicitamente em aberto** — decisão do Caio; escrever deploy para um servidor que
não existe seria ficção.

## 4. Lista de fricções

`docs/plans/2026-07-21-friccoes-evento-piloto.md` — tabela: fricção, onde dói, custo
estimado, classificação **correção** / **funcionalidade nova**.

**Regra de corte:** é *correção* se o âmbito já promete a capacidade e ela está lenta,
confusa ou partida. É *funcionalidade nova* se for preciso **editar**
`docs/01-ambito-fase1.md` para a justificar — e aí não entra nesta fase.

O `guardiao-ambito` valida a lista inteira antes de se escrever código de correção.

## 5. Pendências herdadas (verificadas contra o código)

| # | Pendência | Estado verificado |
|---|---|---|
| 1 | Pesagem pública expõe dados de quem não consentiu | **Confirmado, bloqueador.** `PublicContentService.cs:212` usa `card.Name` (único campo que sobrevive à redação sem consentimento) e junta-lhe `OfficialWeightKg` → nome completo + peso corporal de menor sem consentimento, público. Nomear num card é defensável; publicar o peso não é a mesma coisa |
| 2 | Sem renegociação de peso combinado | **Confirmado.** `Pages/Admin/Events/Fights/` tem `Add`, `Replace`, `Result` — não tem `Edit`. Falha de peso só permite cancelar ou apagar/recriar o combate |
| 3 | Bandeiras vs `Nationality` texto livre | **Menos grave do que registado.** `portal/lib/flags.ts` é best-effort (~30 países) e degrada em silêncio. Qualidade de dados, não bug |
| 4 | Eventos cancelados continuam públicos se alguma vez publicados | A decidir com o ensaio à frente |
| 5 | Linha de pesagem não indica o estado do combate | A decidir com o ensaio à frente |
| 6 | `ReinstateFight` mantido sem estar literal no prompt 03 | A decidir com o ensaio à frente |

As três últimas não se decidem bem no abstrato — ficam para o ensaio.

## Fora desta sessão

Qualquer funcionalidade nova. Provisionamento de cloud, contas e segredos (o Claude não
cria contas nem introduz credenciais — é trabalho do Caio). Escolha final de alvo de
deploy. Antes do PR: `guardiao-ambito` e `/security-review`.
