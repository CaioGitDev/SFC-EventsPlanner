# Visão

## Problema

O espaço dos desportos de combate está fragmentado: software de ginásio, software de torneios, bilhética, Excel para matchmaking e WhatsApp para comunicação. Nenhuma solução integra a operação completa de um evento para associações de Muay Thai, Kickboxing, K1, Boxe e MMA em Portugal.

## Visão a longo prazo

Um "Combat Sports Operating System": plataforma modular com CRM de atletas/clubes, gestão de eventos, fight management (matchmaking, pesagens, rankings), media hub e portal público de qualidade televisiva (referência: ONE Championship). Multi-tenant, servindo associações, promotores e federações.

## Estratégia

A visão NÃO é o plano. A estratégia é:

1. **SFC como primeiro cliente e laboratório.** Construir apenas o necessário para gerir eventos reais da SFC.
2. **Validar com eventos reais** antes de qualquer investimento em funcionalidades "premium".
3. **Manter a porta do SaaS aberta com custo mínimo:** `OrganizationId` em todas as entidades, mais nada.
4. **Um evento de cada vez.** Cada fase do roadmap só avança quando a anterior foi usada em produção.

## Concorrência de referência

Blue6, ArenaFlow, FightFlow, MartialMatch, TatamiTek, Kihapp — convergem em gestão de atletas, matchmaking, pesagens, bilhetes, resultados e transmissão. O diferencial pretendido: experiência pública ao nível da ONE + operação integrada adaptada à realidade das associações portuguesas.

## O que este projeto NÃO é (por agora)

- Não é um produto de broadcast (OBS/vMix/overlays) — Fase 4+
- Não é uma bilheteira — integra soluções externas
- Não é uma rede social de atletas
- Não compete com software de gestão de ginásios
