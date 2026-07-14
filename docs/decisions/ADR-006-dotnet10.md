# ADR-006 — Migrar de .NET 9 para .NET 10 LTS

- **Estado:** Aceite (2026-07-14)
- **Contexto:** O setup inicial (prompts/00-setup-solucao.md) foi especificado em .NET 9, mas o .NET 9 é STS e atingiu o fim de suporte em maio de 2026. A máquina de dev já só tem SDK/runtime 10, o que obrigava a `RollForward=LatestMajor` como paliativo. .NET 10 é LTS (suporte até novembro de 2028).
- **Decisão:** Todos os projetos target `net10.0`; pacotes EF Core/Identity/Npgsql na linha 10.0.x; `dotnet-ef` 10.x; CI com SDK `10.0.x`. O `RollForward` deixa de ser necessário e foi removido.
- **Consequências:** Stack suportada até 2028 sem custo de migração relevante (a solução tinha apenas o scaffold). A migration `InitialCreate` gerada com EF 9 mantém-se — migrations aplicadas não se regeneram; EF 10 executa-as sem alterações. Referências a ".NET 9" em prompts históricos ficam como registo; a stack vinculativa é a deste ADR e do CLAUDE.md.
