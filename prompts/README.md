# Prompts orquestrados — Fase 1

Sequência ordenada de prompts para desenvolver o MVP com o Claude Code + plugin superpowers. Cada ficheiro é um prompt pronto a colar no início de uma sessão.

## Como usar

1. Seguir a ordem numérica — cada prompt assume que os anteriores estão concluídos e merged.
2. Colar o conteúdo do ficheiro no Claude Code, dentro da pasta do projeto.
3. O superpowers ativa automaticamente: brainstorming → design em `docs/plans/` → plano → execução com TDD → code review.
4. No fim de cada funcionalidade, correr o agent `guardiao-ambito` antes do merge.
5. Se durante a sessão surgir uma ideia fora do âmbito: anotar em `docs/ideias-parqueadas.md` e continuar — não implementar.

## Sequência

| # | Prompt | Entrega |
|---|---|---|
| 00 | `00-setup-solucao.md` | Solução .NET + portal Next.js + Docker + testes a verde |
| 01 | `01-atletas-clubes.md` | CRUD de clubes e atletas no backoffice |
| 02 | `02-eventos-fightcard.md` | CRUD de eventos + fight card |
| 03 | `03-resultados-records.md` | Resultados + records automáticos (núcleo do domínio) |
| 04 | `04-pesagem.md` | Pesagem simples |
| 05 | `05-api-publica-portal.md` | API read-only + portal público completo |
| 06 | `06-evento-piloto.md` | Checklist de ensaio geral com dados reais |

## Regra de sessão

Uma funcionalidade por sessão/branch. Sessões part-time curtas terminam sempre num estado merged ou com plano guardado em `docs/plans/` — nunca com trabalho a meio sem registo.
