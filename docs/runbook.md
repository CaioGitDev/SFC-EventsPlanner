# Runbook operacional — SFC EventsPlanner

Procedimentos para operar a plataforma: arranque local, carga de dados, backup,
restauro, variáveis de ambiente e o dia do evento. Todos os comandos aqui foram
executados e verificados; onde algo está por decidir, está marcado como tal.

Shell assumido: **PowerShell** (o `.ps1`). As queries de verificação com identificadores
entre aspas duplas (`"Athletes"`) correm melhor num shell POSIX / Git Bash — o PowerShell
consome as aspas duplas nos argumentos passados a `docker exec psql`.

---

## 1. Arranque local

```powershell
# Infraestrutura (PostgreSQL + MinIO). --wait espera pelo healthcheck;
# sem ele, o primeiro boot do Postgres ainda está em initdb quando a app liga.
docker compose up -d --wait
```

Seed do administrador de desenvolvimento — ficheiro **gitignored**, criar uma vez em
`src/Sfc.Web/appsettings.Development.json`:

```json
{ "SeedAdmin": { "Email": "admin@sfc.local", "Password": "<escolhe-uma>" } }
```

Sem `SeedAdmin` a app arranca na mesma — apenas não cria o utilizador admin.

```powershell
dotnet run --project src/Sfc.Web      # backoffice: aplica migrations e faz seed no arranque
cd portal; npm install; npm run dev   # portal público
```

**MinIO (imagens).** O bucket `sfc-media` é criado no primeiro upload mas é privado por
omissão. Para as imagens renderizarem, permitir leitura anónima uma vez:

```powershell
docker compose exec minio mc alias set local http://localhost:9000 minioadmin minioadmin
docker compose exec minio mc anonymous set download local/sfc-media
```

---

## 2. Importar dados (carga inicial / migração do Excel)

Comando one-off que corre e sai sem levantar o servidor web:

```powershell
dotnet run --project src/Sfc.Web -- import <pasta> [--dry-run]
```

- `--dry-run` valida e relata **sem gravar nada** — é o modo a usar quando o Excel real
  da SFC aparecer, para ver os erros antes de escrever.
- **O caminho é relativo à pasta onde corres o comando, não ao projeto.** Corre a partir
  da raiz do repositório (`dotnet run --project src/Sfc.Web -- import ./data/seed`) ou passa
  um caminho absoluto. Fora da raiz, `./data/seed` não é encontrado.
- A importação é idempotente: clubes (por nome) e atletas (por nome + data de nascimento)
  já existentes são saltados, não duplicados. Re-correr é seguro.
- Erros indicam **ficheiro, linha e coluna**. Uma linha má é registada e a importação
  continua — não aborta o lote.

**Contrato de colunas** (cabeçalhos exatos; um cabeçalho desconhecido é rejeitado pelo nome):

| Ficheiro | Colunas |
|---|---|
| `clubs.csv` | `name`, `city`, `country`, `contact_email`, `contact_phone`, `coaches` (separados por `\|`) |
| `athletes.csv` | `first_name`, `last_name`, `nickname`, `date_of_birth` (`yyyy-MM-dd`), `nationality`, `club_name`, `coach_name`, `discipline`, `weight_class`, `weight_kg`, `height_cm`, `status`, `public_profile_consent`, `baseline_wins`, `baseline_losses`, `baseline_draws`, `baseline_kos` |
| `events.csv` | `name`, `slug`, `date`, `venue`, `city`, `status` |
| `fights.csv` | `event_slug`, `order`, `discipline`, `rounds`, `round_duration_minutes`, `weight_class`, `catchweight_kg`, `is_title_fight`, `is_amateur`, `red_athlete_slug`, `blue_athlete_slug` |
| `results.csv` | `event_slug`, `fight_order`, `winner_slug`, `method`, `round`, `time` |

Notas de formato que, se violadas, aparecem como erro:
- `events.csv` `date` é **hora local de Lisboa, sem fuso**, formato `yyyy-MM-ddTHH:mm`. Um
  sufixo `Z` ou um offset numérico é **rejeitado** (evita um desvio silencioso de uma hora
  no horário de verão).
- `fights.csv` **não tem coluna de billing** — main/co-main são derivados da ordem do card.
- `baseline_kos` nunca pode exceder `baseline_wins`.
- Ligações por nome/slug: `club_name` liga ao clube; os slugs em `fights`/`results` são os
  gerados de `nome apelido` (minúsculas, sem acentos, hífen).

O dataset de mock em `data/seed/` (10 clubes, 45 atletas, 2 eventos históricos) serve de
exemplo do contrato e de pano de fundo para ensaios.

---

## 3. Backup

```powershell
.\scripts\backup.ps1                        # grava em backups\sfc_events_<timestamp>.dump
.\scripts\backup.ps1 -OutputDirectory D:\algures
```

- Formato custom do PostgreSQL (`pg_dump -Fc`), restaurável com `pg_restore`.
- Corre `pg_dump` **dentro do contentor** e transfere com `docker compose cp` (stream tar) —
  não passa os bytes pela pipeline do PowerShell, que os corromperia (PowerShell 5.1
  reencoda saída de comandos nativos como texto).
- Os dumps **nunca são versionados**: `backups/` e `*.dump` estão no `.gitignore`. Contêm
  dados pessoais (nomes, datas de nascimento de atletas, alguns menores) — tratar como
  dados sensíveis (RGPD, ADR-004).

---

## 4. Restauro (DESTRUTIVO)

```powershell
.\scripts\restore.ps1 -DumpFile backups\sfc_events_<timestamp>.dump -Force
```

- Sem `-Force`, o script avisa e não apaga nada. O `-Force` é a confirmação explícita —
  não corre por engano no dia do evento.
- Faz `DROP DATABASE` / `CREATE DATABASE` / `pg_restore`. **Não** apaga o volume Docker
  (`docker-volumes/postgres/`), por isso é reversível.
- Se o `DROP DATABASE` falhar por haver ligações abertas, **parar o backoffice primeiro**
  (o pool de ligações reconecta): fechar o `dotnet run` e voltar a correr o restauro.

**Verificar o restauro** (Git Bash, não PowerShell — aspas duplas nos identificadores):

```bash
docker compose exec -T postgres psql -U sfc -d sfc_events -t -c \
  'select (select count(*) from "Athletes"), (select count(*) from "Events"), (select count(*) from "FightResults");'
```

Contar **antes** do backup e **depois** do restauro; os números têm de bater. Um restauro
que nunca foi verificado contra dados reais não é um restauro testado
(requisito de `docs/03-arquitetura.md:49`).

---

## 5. Variáveis de ambiente

**Backoffice** (`appsettings.json` / `appsettings.Development.json` / user-secrets / ambiente):

| Chave | Papel |
|---|---|
| `ConnectionStrings:Default` | Ligação PostgreSQL |
| `Storage:Endpoint` / `Bucket` / `AccessKey` / `SecretKey` / `PublicBaseUrl` | Armazenamento de imagens (MinIO em dev; R2 em produção) |
| `SeedAdmin:Email` / `Password` | Cria o admin no arranque (omitir em produção) |
| `Portal:RevalidateUrl` | Endpoint de revalidação do portal; **não definido = revalidação desligada** (default de dev) |
| `Portal:RevalidateSecret` | Segredo enviado no header `x-revalidate-secret` |

**Portal** (`portal/.env.local` / ambiente):

| Variável | Papel |
|---|---|
| `SFC_API_BASE` | Base da API pública read-only do backoffice |
| `PORTAL_REVALIDATE_SECRET` | Tem de coincidir com `Portal:RevalidateSecret` do backoffice |
| `NEXT_PUBLIC_IMAGE_HOST` | Host das imagens (corresponde a `Storage:PublicBaseUrl`) |
| `NEXT_PUBLIC_SITE_URL` | URL público do portal (SEO, links canónicos) |

Segredos nunca no código: `.env`/user-secrets em dev, GitHub Secrets em CI (regra 7 do
`CLAUDE.md`). O `gitleaks` corre em todos os PRs.

---

## 6. Deploy — Em aberto (decisão pendente)

**Ainda não decidido.** `docs/03-arquitetura.md:42` prevê "1 VPS ou serviço gerido barato;
portal na Vercel (free tier)", mas nada foi provisionado e o alvo concreto não está
escolhido. Escrever um procedimento de deploy passo-a-passo para infraestrutura que não
existe seria ficção.

O que falta decidir e fazer, quando houver alvo:
- Escolher e provisionar o host do backoffice (VPS ou serviço gerido) e a base PostgreSQL.
- Provisionar o armazenamento de imagens (Cloudflare R2, S3-compatible) e preencher
  `Storage:*` com credenciais reais.
- Ligar o portal à Vercel e preencher as suas variáveis de ambiente.
- Definir os segredos em GitHub Secrets / no painel do host, nunca no código.
- Agendar backups automáticos diários da BD (o `backup.ps1` é o ponto de partida) e repetir
  o teste de restauro no ambiente de produção antes do primeiro evento.

Estas decisões são do Caio. Este runbook cobre tudo o resto até esse ponto.

---

## 7. No dia do evento

- [ ] **Backup antes de começar.** `.\scripts\backup.ps1`, guardar o ficheiro num sítio
      seguro (não só na máquina do evento).
- [ ] Confirmar login no backoffice **no telemóvel** que vai ser usado no pavilhão — a rede
      lá é fraca e o backoffice é usado de pé.
- [ ] Confirmar que o portal **revalida** depois de publicar um resultado (com
      `Portal:RevalidateUrl` definido); se a revalidação estiver desligada, as páginas
      públicas não atualizam sozinhas.
- [ ] Ter o comando de restauro à mão (secção 4) caso a base fique num estado mau — mas
      lembrar que é destrutivo e precisa de `-Force`.
