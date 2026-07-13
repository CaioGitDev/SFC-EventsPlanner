---
name: sfc-contexto
description: Contexto de domínio do SFC EventsPlanner — vocabulário, regras e realidade operacional de eventos de desportos de combate. Usar sempre que se modela, implementa ou desenha UI relacionada com atletas, clubes, eventos, combates, resultados, records ou pesagens.
---

# Contexto de domínio SFC

## Vocabulário (código EN ↔ UI pt-PT)

| Código | UI pt-PT | Nota |
|---|---|---|
| Fight | Combate | não "luta" em contexto formal |
| Fight card | Fight card / Cartaz | termo aceite em pt |
| Record | Cartel / Record | formato W-L-D, ex.: 18-3-0 |
| Weigh-in | Pesagem | |
| Red/Blue corner | Canto vermelho / Canto azul | |
| Main event | Combate principal / Main event | main event fecha o card |
| Weight class | Categoria de peso | |
| Catchweight | Peso combinado | quando fora das categorias |
| Bout order | Ordem do card | 1 = primeiro combate da noite |

## Disciplinas e formatos

- Disciplinas: Muay Thai, Kickboxing, K1, Boxe, MMA
- Amador: tipicamente 3 rounds de 2 min (varia); proteções obrigatórias; muitos atletas menores de idade
- Profissional: 3 ou 5 rounds de 3 min (MMA: 5 min); main events e títulos frequentemente 5 rounds

## Métodos de resultado e efeito no record

| Método | Vencedor | Derrotado | Nota |
|---|---|---|---|
| KO / TKO | +1 W (+1 KO) | +1 L | |
| Decisão (unânime/dividida/maioria) | +1 W | +1 L | sem round/tempo |
| Draw | +1 D ambos | — | sem vencedor |
| No Contest | sem alteração | — | combate anulado |
| Desqualificação | +1 W | +1 L | |
| Desistência/Forfeit | +1 W | +1 L | |

## Realidade operacional (informa toda a UX)

- Pesagens na véspera ou manhã do evento; falhas de peso são comuns → renegociação (catchweight) ou cancelamento
- Substituições até horas antes do evento; cards mudam sempre
- No dia do evento: pressa, rede fraca no pavilhão, uso em telemóvel
- Quem introduz resultados fá-lo ao vivo entre combates — o fluxo tem de demorar segundos
- Atletas amadores frequentemente sem nickname, sem foto profissional e menores (RGPD — ver ADR-004)

## Referência visual

Portal público inspirado na ONE Championship: hero grande do próximo evento com countdown, fight cards visuais (foto + record + bandeira de cada atleta frente a frente), cada atleta tratado como marca, CTAs claros para bilhetes e streaming, resultados publicados logo após o evento.
