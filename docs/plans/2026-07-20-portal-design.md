# Design — Portal público Next.js (Prompt 05, Parte B)

Data: 2026-07-20 · Estado: decisões tomadas em sessão autónoma — a rever pelo Caio no PR ·
Âmbito: "Portal público" (docs/01-ambito-fase1.md, linhas 18-24). Consome a API pública
da Parte A (PR #9), já na master.

## Objetivo

Portal read-only em `portal/` (Next.js 16 App Router, TypeScript, Tailwind v4, shadcn),
inspirado na ONE Championship: cada atleta tratado como marca, fight cards visuais frente
a frente, hero do próximo evento com countdown. SSG/ISR com revalidação on-demand,
metadata SEO + Open Graph por página, tudo em pt-PT.

## Decisões desta sessão

| Decisão | Escolha |
|---|---|
| Caching | Modelo clássico do App Router: `fetch(url, { next: { revalidate: 3600, tags } })` (ISR 1h + revalidação on-demand por tag). **Não** usar Cache Components (`use cache`) — o modelo clássico chega e é mais simples (YAGNI) |
| Camada de dados | `lib/api.ts` — fetchers tipados que espelham os DTOs da Parte A; tipos em `lib/types.ts`. Base da API por env `SFC_API_BASE` (server-only; default `http://localhost:5000` em dev). Erros HTTP → `null` (páginas fazem `notFound()`) |
| Revalidação | Route handler `app/api/revalidate/route.ts` (POST): valida header `x-revalidate-secret` contra `PORTAL_REVALIDATE_SECRET`; lê `{ reason, eventSlug }` (formato do `PortalRevalidator` da Parte A) e chama `revalidateTag`. Sem secret configurado no portal → 401 (o backoffice tolera a falha). Tags: `events` (home/lista/próximo), `event:{slug}` (detalhe/resultados/pesagens), `fighters` (perfis; resultados/pesagens também revalidam esta) |
| Tema | **Dark fixo** inspirado ONE Championship (fundo quase-preto, primário vermelho "canto vermelho", tipografia forte). Sem toggle claro/escuro — "dark mode configurável" está FORA (docs/01-ambito-fase1.md). Tokens shadcn em `globals.css` reescritos para o look dark |
| Estrutura de páginas | `/` (home), `/events` (lista próximos+passados), `/events/[slug]` (detalhe + card visual), `/events/[slug]/results` (resultados + weight results), `/fighters/[slug]` (perfil). `generateStaticParams` nos slugs conhecidos; `dynamicParams = true` para slugs novos (ISR on-demand) |
| Countdown | Client component `<Countdown targetIso>`; render só depois de montar (evita mismatch de hidratação). CTA "Ver em direto" aparece quando há `streamUrl` **e** a data do evento é hoje (Europa/Lisboa) |
| Fight card visual | Componente `<FightCard>` — atletas frente a frente: foto (ou placeholder de iniciais), record, bandeira (best-effort de `nationality`), nome/alcunha; badges de billing/título/amador; VS central. Atleta sem consentimento (slug null) → sem link, sem foto/record (a API já redige; o portal reflete) |
| Bandeiras | `lib/flags.ts` — mapeia nomes pt-PT/EN e códigos ISO comuns para emoji de bandeira; fallback sem bandeira. Resolve o aviso do revisor da Parte A do lado do portal sem alterar o backoffice (a decisão de campo ISO fica para o Caio) |
| Imagens | `next/image` com `remotePatterns` do host R2 (env `NEXT_PUBLIC_IMAGE_HOST`) + localhost (MinIO dev); wrapper `<Poster>`/`<Avatar>` com placeholder quando a URL é null (dados de smoke não têm imagens) |
| SEO | `generateMetadata` por página dinâmica (title, description, Open Graph com poster/banner quando existe); `lang="pt-PT"` já no layout; `metadataBase` por env |
| Estados vazios | Sem próximo evento (204 da API) → secção "Sem eventos agendados"; listas vazias e combates sem resultado tratados com mensagens pt-PT |

## Ficheiros (portal/)

```
app/
  layout.tsx           # já existe — ajustar metadata base, header/footer
  page.tsx             # home
  globals.css          # tema dark
  events/page.tsx                     # lista
  events/[slug]/page.tsx              # detalhe
  events/[slug]/results/page.tsx      # resultados + pesagens
  fighters/[slug]/page.tsx            # perfil
  api/revalidate/route.ts             # webhook de revalidação
  not-found.tsx        # 404 pt-PT
components/
  site-header.tsx, site-footer.tsx
  countdown.tsx (client), event-hero.tsx
  fight-card.tsx, athlete-corner.tsx, result-line.tsx, weigh-in-table.tsx
  event-card.tsx (resumo na lista), record-badge.tsx, poster.tsx, avatar.tsx
lib/
  types.ts             # espelho dos DTOs da Parte A
  api.ts               # fetchers
  flags.ts             # nacionalidade → emoji
  format.ts            # datas pt-PT, "hoje em Lisboa"
```

## Verificação

Frontend — sem TDD unitário (YAGNI para páginas de conteúdo estáticas); a rede de
segurança é:
- `next build` verde (type-check + lint estritos; `tsconfig` strict já ativo)
- Smoke test no browser contra o dev server .NET (dados do smoke das sessões anteriores):
  home, lista, detalhe com card, perfil, resultados/pesagens; verificar redação
  (atleta sem consentimento só com nome), countdown, e revalidação (POST ao webhook)
- Screenshots falham nesta máquina ([[browser-pane-screenshot-timeout]]) — verificação
  por `get_page_text`/`read_page`/JS e por `next build`

## Fora desta sessão

Notícias, rankings, estatísticas, pesquisa, dark mode configurável (todos FORA, prompt 05).
Antes do PR: `guardiao-ambito` e `revisor-dominio` (o prompt não pede `/security-review`
para a Parte B, mas o portal é read-only e não introduz segredos além do env do webhook).
