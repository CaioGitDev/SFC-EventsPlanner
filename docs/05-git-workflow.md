# Workflow Git e gates de qualidade

Repositório: `https://github.com/CaioGitDev/SFC-EventsPlanner` (privado). Branch principal: `master`, protegida — **nunca se faz push direto; todo o trabalho entra por PR.**

## Gates (por ordem, em cada funcionalidade)

| # | Gate | Onde | Bloqueia |
|---|---|---|---|
| 1 | TDD (red-green-refactor) | superpowers, durante implementação | código sem teste |
| 2 | Code review superpowers | fim de cada tarefa do plano | issues Critical |
| 3 | Agent `guardiao-ambito` | antes de abrir PR | scope creep |
| 4 | Agent `revisor-dominio` | antes de abrir PR (features de domínio) | regras erradas |
| 5 | `/security-review` do Claude Code | antes de abrir PR | vulnerabilidades |
| 6 | CI (build + testes + gitleaks) | no PR, obrigatório | build/testes vermelhos, segredos |
| 7 | Review humana do Caio + merge | GitHub | tudo o resto |

O checklist destes gates está em `.github/PULL_REQUEST_TEMPLATE.md` — um PR sem checklist preenchido não é merged.

## Configuração do GitHub (fazer uma vez, na tua máquina)

```bash
# 1. Criar o repo privado e fazer o primeiro push
cd SFC-EventsPlanner
gh repo create CaioGitDev/SFC-EventsPlanner --private --source=. --push

# 2. Proteger a master (ruleset: PR obrigatório + CI verde + sem force push)
gh api repos/CaioGitDev/SFC-EventsPlanner/rulesets \
  --method POST \
  --input - <<'EOF'
{
  "name": "protect-master",
  "target": "branch",
  "enforcement": "active",
  "conditions": { "ref_name": { "include": ["refs/heads/master"], "exclude": [] } },
  "rules": [
    { "type": "deletion" },
    { "type": "non_fast_forward" },
    { "type": "pull_request", "parameters": {
        "required_approving_review_count": 0,
        "dismiss_stale_reviews_on_push": true,
        "require_code_owner_review": false,
        "require_last_push_approval": false,
        "required_review_thread_resolution": true
    }},
    { "type": "required_status_checks", "parameters": {
        "strict_required_status_checks_policy": true,
        "required_status_checks": [
          { "context": "backend" },
          { "context": "portal" },
          { "context": "secrets-scan" }
        ]
    }}
  ]
}
EOF

# 3. Ativar secret scanning + push protection do GitHub
gh api repos/CaioGitDev/SFC-EventsPlanner \
  --method PATCH \
  --field security_and_analysis[secret_scanning][status]=enabled \
  --field security_and_analysis[secret_scanning_push_protection][status]=enabled
```

Nota: `required_approving_review_count` está a 0 porque és o único developer — o GitHub não permite aprovares o teu próprio PR. O gate humano é a tua própria review do diff no PR antes do merge. Se entrar mais alguém na equipa, subir para 1.

### Dependência de plano (rulesets e secret scanning)

Os passos 2 e 3 acima dependem do plano do repositório:

- **Repositório privado:** rulesets (passo 2) e secret scanning + push protection (passo 3) exigem **GitHub Pro** (ou superior). No plano gratuito devolvem `403 Upgrade to GitHub Pro` e `422 Secret scanning is not available`, respetivamente.
- **Repositório público:** ambos são **gratuitos**.

O `SFC-EventsPlanner` está **público** para ter estes gates sem custo. Isto é aceitável porque o repositório contém apenas **código, documentação e configuração** — nunca dados de eventos, atletas, documentos médicos ou dados de menores (esses vivem exclusivamente na base de dados; ver ADR-004). Se algum dia for necessário privado com estes gates impostos, a via é subscrever GitHub Pro.

Independentemente do plano, o gate de segredos do **CI (gitleaks, job `secrets-scan`)** corre sempre em cada PR — o secret scanning nativo do GitHub é uma camada adicional, não o único mecanismo.

## Fluxo por funcionalidade

```bash
git checkout -b feature/nome-curto   # ou worktree do superpowers
# ... desenvolvimento com superpowers (TDD) ...
# gates 3-5 (agents + security-review)
git push -u origin feature/nome-curto
gh pr create --fill                  # template preenche o checklist
# CI verde → review do diff → merge (squash) → apagar branch
```

## Regras

- Commits pequenos, mensagem em inglês no imperativo
- Merge por squash; branch apagada após merge
- Nunca commitar: `.env`, `appsettings.Development.json`, segredos, dumps de BD (ver `.gitignore`)
- Segredos de produção: variáveis de ambiente / secrets do GitHub Actions, nunca no código
