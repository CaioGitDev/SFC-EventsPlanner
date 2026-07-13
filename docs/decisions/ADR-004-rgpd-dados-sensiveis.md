# ADR-004 — RGPD e dados sensíveis

- **Estado:** Aceite (2026-07-13)
- **Contexto:** Exames médicos são dados de saúde (art. 9.º RGPD — categoria especial). Muitos atletas amadores são menores. O portal público expõe perfis de atletas.
- **Decisão:**
  1. Portal público nunca expõe: data de nascimento (mostrar idade calculada), contactos, peso oficial de pesagem antes da publicação dos weight results, ou qualquer documento.
  2. Documentos médicos/licenças (Fase 2) serão armazenados encriptados, com acesso restrito por role e auditado.
  3. Consentimento explícito para publicação de perfil/foto no portal — flag `PublicProfileConsent` no atleta; sem consentimento, o atleta aparece no fight card apenas com nome.
  4. Menores: perfil público exige consentimento do encarregado de educação (processo manual na Fase 1, registado em Notes).
- **Consequências:** Fricção na carga de dados (necessário consentimento), mas conformidade desde o dia 1. A flag de consentimento entra no modelo da Fase 1.
