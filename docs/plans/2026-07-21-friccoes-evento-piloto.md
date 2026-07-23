# Lista de fricções — evento piloto

Estado: **preliminar.** Contém a passagem estrutural (medida pelo Claude no backoffice a
375×812) e as pendências herdadas confirmadas contra o código. **Falta a passagem humana**
do Caio — cronometragem real dos 12 combates e os três desvios (falha de peso, substituição,
resultado corrigido) — que vai acrescentar linhas a esta tabela.

**Regra de corte:** é *correção* se `docs/01-ambito-fase1.md` já promete a capacidade e ela
está lenta, confusa ou partida. É *funcionalidade nova* se for preciso **editar** esse
documento para a justificar — e aí vai para `docs/ideias-parqueadas.md`, não se implementa
nesta fase.

O `guardiao-ambito` valida a classificação **quando a lista estiver completa** (depois do
ensaio do Caio). Não corre ainda sobre uma lista parcial, porque os três desvios são
precisamente onde podem nascer tentações de funcionalidade nova.

## Passagem estrutural (medida)

Medido contra um evento publicado no backoffice, viewport de telemóvel (375×812), com os
tempos de resposta lidos dos logs do servidor.

| # | Fricção | Onde dói | Evidência | Custo | Classificação |
|---|---|---|---|---|---|
| E1 | **Fight card no fundo do formulário de edição do evento.** Para chegar às ações de um combate (Resultado, Substituir, Pesagem) abre-se «Editar» e percorre-se todo o formulário do evento — Nome, Data, Descrição, Local, Cidade, 2 links, Slug, Banner, Poster, Guardar — antes de ver o card. | No telemóvel, entre combates, cada regresso ao evento aterra no topo do formulário e obriga a rolar até ao fim. Repete-se 12 vezes numa noite. | `Pages/Admin/Events/Edit.cshtml` — o card é a última secção; medido a 375×812. | Médio (rolagem repetida, sem cliques extra) | Correção (o card já existe; é ordenação/atalho) |
| E2 | **Registo de resultado tem passo de confirmação.** Preencher → «Rever resultado» → página de revisão → «Confirmar». | Um page load e um toque extra por combate. Em 12 combates, 12 recargas extra em rede fraca. **Mas é uma salvaguarda deliberada** contra registar o vencedor errado à pressa. | Fluxo `Result.cshtml` com handlers `?handler=Review` e `?handler=Confirm` (confirmado na rede). | Baixo, mas multiplicado por 12 | A decidir com o Caio: manter (segurança) vs. gravação direta com desfazer |
| E3 | **Pesagem grava um atleta de cada vez, com page load por atleta.** A grelha tem um mini-formulário «Gravar» por canto; não há gravação em lote. | 12 combates = 24 atletas = 24 submissões de página inteira, cada uma uma espera em rede fraca de pavilhão. | `WeighIns.cshtml` — um `<form>` por atleta, POST completo (sem AJAX). | Médio (24 recargas) | A decidir com o Caio: aceitável para Fase 1 vs. gravação em lote (roça funcionalidade nova) |
| E4 | Tempo de resposta do servidor **não é fricção.** | — | POSTs de resultado e queries de card: operações de BD entre 2 e 48ms nos logs. | Nenhum | Sem ação |

## Pendências herdadas (confirmadas contra o código)

| # | Fricção | Evidência | Classificação |
|---|---|---|---|
| P1 | **RGPD: pesagem pública expõe nome + peso corporal de atleta sem consentimento.** A linha de pesagem pública usa `card.Name` (único campo que sobrevive à redação sem consentimento) e junta-lhe `OfficialWeightKg`. Como o dataset tem menores sem consentimento, o portal publicaria nome completo + peso corporal de um menor. | `PublicContentService.cs:212` | **Correção — tratada como bloqueador.** O âmbito proíbe dados sensíveis no portal (ADR-004). |
| P2 | **Sem `Fights/Edit` para renegociar peso combinado.** Após falha de peso, o card só permite cancelar ou apagar-e-recriar o combate; não há edição para acordar um catchweight. | `Pages/Admin/Events/Fights/` tem `Add`, `Replace`, `Result` — não `Edit`. | A decidir: o âmbito prevê cancelar (`01-ambito-fase1.md:14`), mas a realidade do domínio renegoceia. Pode ser funcionalidade nova. |
| P3 | **Bandeiras vs `Nationality` texto livre.** O portal promete bandeiras mas `Nationality` é texto livre; `flags.ts` cobre ~30 países e degrada em silêncio. | `portal/lib/flags.ts` | Correção menor (qualidade de dados), ou nada. |
| P4 | Eventos cancelados continuam públicos se alguma vez publicados. | design de eventos | A decidir com o ensaio. |
| P5 | A linha de pesagem pública (`PublicWeighInRow`) não indica o estado do combate. | `PublicApi.cs:56` | A decidir com o ensaio. |
| P6 | `ReinstateFight` mantido sem estar literal no prompt 03. | `EventService.cs:414` | A decidir com o ensaio. |

## Achado de dados (do review da Task 6)

| # | Item | Estado |
|---|---|---|
| D1 | **Superfights cross-disciplina uniformes:** todos os 20 combates do dataset têm exatamente um canto cuja disciplina pessoal difere da disciplina do combate. Parece deliberado mas 20/20 é suspeito. | **Confirmar com o Caio** se é intencional. Não é mislead perigoso — o portal mostra a disciplina do combate. |

## A preencher no ensaio do Caio

- **Cronometragem real** por combate (meta < 30s) — o número humano, não a medição estrutural.
- **Desvio 1 — falha de peso:** dar peso oficial acima do limite e tentar resolver com peso
  combinado. Esperado: bater na ausência de `Fights/Edit` (P2).
- **Desvio 2 — substituição de última hora:** trocar um atleta num combate já com pesagem
  gravada; confirmar o que acontece à pesagem do substituído.
- **Desvio 3 — resultado errado:** gravar um vencedor errado, confirmar o record no perfil,
  corrigir, confirmar que o record reverteu.
- **RGPD ao vivo (P1):** publicar o evento com pesagens aprovadas e ler
  `/events/<slug>/results` no portal, procurando os menores sem consentimento.
- **Lighthouse** mobile em `/`, `/events/<slug>`, `/fighters/<slug>`, `/events/<slug>/results`.
