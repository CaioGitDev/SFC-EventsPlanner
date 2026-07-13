# Âmbito da Fase 1 (MVP) — vinculativo

Critério de sucesso: **um evento SFC gerido de ponta a ponta na plataforma, sem Excel.**
Nada fora da lista "DENTRO" é implementado sem o Caio atualizar este documento.

## DENTRO

### Backoffice

1. **Atletas** — CRUD: foto, nome, nickname, nacionalidade, clube, treinador, disciplina, categoria de peso, peso, altura, data de nascimento, record (W-L-D, KOs), estatuto (amador/profissional), ativo/inativo
2. **Clubes** — CRUD: nome, logo, cidade, país, contactos, treinadores, lista de atletas
3. **Eventos** — CRUD: nome, descrição, data, local, banner, poster, estado (rascunho/publicado/concluído/cancelado), link externo de bilhetes, link de streaming (YouTube embed)
4. **Fight card** — combates por evento: ordem, main/co-main, disciplina, rounds, categoria de peso, título (s/n), amador/profissional, red corner, blue corner
5. **Resultados** — vencedor, método (KO/TKO/Decision/Split/Draw/NC), round, tempo; **atualiza automaticamente o record do atleta**
6. **Pesagem simples** — por atleta do card: peso esperado, peso oficial, aprovado (s/n), observações
7. **Utilizadores/roles mínimos** — Administrador e Editor. Mais nada.

### Portal público (Next.js, read-only)

1. Home: hero com próximo evento + countdown + CTA bilhetes (link externo) + CTA streaming
2. Página de evento: fight card completo, info, poster
3. Perfil de atleta: foto, record, últimos combates, próximo combate — URL SEO `/fighters/{slug}`
4. Resultados de evento (inclui weight results da pesagem)
5. Lista de eventos (próximos + passados)

## FORA (com fase prevista)

| Funcionalidade | Fase |
|---|---|
| Matchmaking assistido (filtros/sugestões) | 2 |
| Gestão documental (licenças, seguros, exames, validades, alertas) | 2 |
| Rankings automáticos por categoria | 2 |
| Pesagem avançada (agendamento, check-in) | 2 |
| Geração de PDFs (fight card, credenciais, diplomas) | 3 |
| Comunicação por email | 3 |
| Estatísticas (KO%, streaks) e analytics | 3 |
| Notícias/CMS | 3 |
| SMS/WhatsApp/Push | 4 |
| Broadcast (OBS/vMix/overlays), bastidores, timeline do evento | 4 |
| Media center, Hall of Fame | 4 |
| Multi-tenant real / SaaS | 4 |
| Bilhética própria | nunca (integrar externo) |

## Regras transversais da Fase 1

- `OrganizationId` em todas as entidades (ADR-002)
- Sem dados sensíveis no portal público; data de nascimento nunca exposta publicamente (mostrar idade)
- Responsivo (o backoffice será usado no telemóvel no dia do evento)
