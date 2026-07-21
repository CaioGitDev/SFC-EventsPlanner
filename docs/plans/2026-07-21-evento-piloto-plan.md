# Evento piloto / ensaio geral — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preparar e executar o ensaio geral de um evento SFC de ponta a ponta — importador de dados, simulação medida, backup/restore testado, runbook e lista de fricções — sem implementar uma única funcionalidade nova.

**Architecture:** Um comando de linha de comandos (`dotnet run --project src/Sfc.Web -- import <dir>`) lê CSVs e cria dados através dos serviços existentes (`ClubService`, `AthleteService`, `EventService`), nunca do `DbContext` — para exercitar slugs, validações e agregação de records. A lógica vive em `Sfc.Web/Import/`, testável sem lançar processo. Scripts PowerShell tratam de backup/restore contra o Postgres do docker-compose.

**Tech Stack:** .NET 10, EF Core/Npgsql, xUnit + Testcontainers, CsvHelper (nova dependência), PowerShell, Docker Compose, Next.js (portal), Lighthouse CLI.

**Design de referência:** `docs/plans/2026-07-21-evento-piloto-design.md`

## Global Constraints

- **Zero funcionalidades novas.** Nada que exija editar `docs/01-ambito-fase1.md` entra nesta sessão (regra 1 do `CLAUDE.md`).
- **Código em inglês, UI e mensagens de consola em português de Portugal** (regra 4).
- **TDD obrigatório** em toda a lógica do importador (regra 5).
- `TreatWarningsAsErrors=true` no `Directory.Build.props` — código tem de compilar sem avisos, nullable incluído.
- Todos os TFMs são `net10.0`. Não regenerar migrations já aplicadas.
- **Nunca commitar dumps de BD** (`docs/05-git-workflow.md:92`) — `backups/` entra no `.gitignore` antes do primeiro backup correr.
- **Nunca push direto na `master`** (regra 6). Branch atual: `feature/evento-piloto`.
- Commits pequenos, mensagem **em inglês no imperativo**.
- `OrganizationId` resolve-se sozinho: `SfcDbContext.CurrentOrganizationId` tem default `SeedData.SfcOrganizationId`. O importador não passa organização.

## Factos do código que condicionam este plano

Verificados antes de escrever o plano — não voltar a assumir o contrário:

- **`FightBilling` é derivado, não é input.** `Event.RecalculateBilling()` (`Event.cs:267`) define main = último combate, co-main = penúltimo. `FightInput` **não tem** campo de billing nem de ordem. Logo `fights.csv` **não leva coluna `billing`** — a ordem das linhas é a ordem do card.
- **Um atleta só pode ter um combate por evento** (`Event.cs:264`, lança `InvalidOperationException`). 12 combates = 24 atletas distintos.
- **`ClubService.ParseCoaches`** é `public static` e espera **um treinador por linha**, formato `Nome; contacto`. O CSV usa `|` como separador e o importador converte para `\n`.
- **`PortalRevalidator.TriggerAsync`** é best-effort e sai imediatamente se `Portal:RevalidateUrl` não estiver definido. O import não precisa do portal a correr.
- **`AthleteService.CreateAsync(AthleteInput input, (int Wins, int Losses, int Draws, int Kos) baseline, Stream? photo, ct)`** — o baseline é um tuplo separado do input.
- **`Athlete` valida `baselineKos <= baselineWins`** no construtor. O dataset tem de respeitar isto.
- Testes de integração usam `SfcWebApplicationFactory` (`IClassFixture`), um contentor Postgres por classe, **base partilhada entre testes da mesma classe** — cada teste usa nomes únicos.

## File Structure

**Criar:**
- `src/Sfc.Web/Import/CsvTable.cs` — leitura de um CSV para linhas com acesso por nome de coluna e número de linha. Uma responsabilidade: ficheiro → linhas tipadas.
- `src/Sfc.Web/Import/ImportReport.cs` — contadores (criados/saltados) e erros com ficheiro+linha.
- `src/Sfc.Web/Import/SeedImporter.cs` — orquestra os cinco ficheiros pela ordem correta.
- `src/Sfc.Web/Import/ImportCommand.cs` — parsing de argumentos e escrita na consola (pt-PT).
- `data/seed/{clubs,athletes,events,fights,results}.csv` — o dataset de mock.
- `scripts/backup.ps1`, `scripts/restore.ps1`
- `docs/runbook.md`
- `docs/plans/2026-07-21-friccoes-evento-piloto.md`
- `tests/Sfc.Web.Tests/Import/{CsvTableTests,SeedImporterTests,SeedDatasetTests}.cs`

**Modificar:**
- `src/Sfc.Web/Program.cs:11-66` — ramo de comando antes de `app.Run()`
- `src/Sfc.Web/Sfc.Web.csproj:5-7` — pacote CsvHelper
- `.gitignore` — `backups/`

---

### Task 1: Leitura de CSV

**Files:**
- Create: `src/Sfc.Web/Import/CsvTable.cs`
- Modify: `src/Sfc.Web/Sfc.Web.csproj`
- Test: `tests/Sfc.Web.Tests/Import/CsvTableTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `CsvTable.Read(string path) → List<CsvRow>`; `CsvRow.Line → int`; `CsvRow.File → string`; `CsvRow.Text(string column) → string?` (vazio → `null`, sempre `Trim()`); `CsvRow.Required(string column) → string` (lança `ImportException` se ausente/vazio); `CsvRow.Int/Decimal/Bool/Date/Enum<T>` (todos nullable, cultura invariante); `ImportException(string message)`.

- [ ] **Step 1: Adicionar a dependência**

```bash
cd "D:/Users/160173003/Desktop/SFC-EventsPlanner"
dotnet add src/Sfc.Web/Sfc.Web.csproj package CsvHelper
```

Anotar a versão resolvida no `.csproj` — é o que vai ao PR para revisão.

- [ ] **Step 2: Escrever o teste que falha**

```csharp
using Sfc.Web.Import;
using Xunit;

namespace Sfc.Web.Tests.Import;

public class CsvTableTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sfc-csv").FullName;

    private string WriteCsv(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Read_ParsesQuotedFieldsAndTracksLineNumbers()
    {
        var path = WriteCsv("clubs.csv",
            "name,city\n\"Silva, Costa & Filhos\",Lisboa\n  Team Scorpion  ,Porto\n");

        var rows = CsvTable.Read(path);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Silva, Costa & Filhos", rows[0].Required("name"));
        Assert.Equal("clubs.csv", rows[0].File);
        Assert.Equal(2, rows[0].Line);           // linha 1 é o cabeçalho
        Assert.Equal("Team Scorpion", rows[1].Required("name"));
        Assert.Equal(3, rows[1].Line);
    }

    [Fact]
    public void Text_TreatsBlankAsNull()
    {
        var path = WriteCsv("a.csv", "name,nickname\nJoao,\nAna,   \n");

        var rows = CsvTable.Read(path);

        Assert.Null(rows[0].Text("nickname"));
        Assert.Null(rows[1].Text("nickname"));
    }

    [Fact]
    public void Required_WhenBlank_ThrowsWithFileAndLine()
    {
        var path = WriteCsv("athletes.csv", "first_name\n\n");

        var rows = CsvTable.Read(path);
        var ex = Assert.Throws<ImportException>(() => rows[0].Required("first_name"));

        Assert.Contains("athletes.csv", ex.Message);
        Assert.Contains("2", ex.Message);
        Assert.Contains("first_name", ex.Message);
    }

    [Fact]
    public void Decimal_UsesInvariantCultureAndAcceptsComma()
    {
        var path = WriteCsv("a.csv", "weight_kg\n72.5\n");

        Assert.Equal(72.5m, CsvTable.Read(path)[0].Decimal("weight_kg"));
    }

    [Fact]
    public void Enum_WhenUnknownValue_ThrowsWithColumnName()
    {
        var path = WriteCsv("athletes.csv", "discipline\nSumo\n");

        var ex = Assert.Throws<ImportException>(
            () => CsvTable.Read(path)[0].Enum<Sfc.Domain.Athletes.Discipline>("discipline"));

        Assert.Contains("discipline", ex.Message);
        Assert.Contains("Sumo", ex.Message);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
```

- [ ] **Step 3: Correr o teste e confirmar que falha**

Run: `dotnet test tests/Sfc.Web.Tests --filter "FullyQualifiedName~CsvTableTests"`
Expected: FAIL na compilação — `CsvTable` e `ImportException` não existem.

- [ ] **Step 4: Implementar**

```csharp
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace Sfc.Web.Import;

/// <summary>Import failure with enough context for a human to fix the source file.</summary>
public class ImportException(string message) : Exception(message);

/// <summary>One data row, aware of where it came from so errors can point at it.</summary>
public class CsvRow(string file, int line, IReadOnlyDictionary<string, string> fields)
{
    public string File { get; } = file;
    public int Line { get; } = line;

    public string? Text(string column)
    {
        if (!fields.TryGetValue(column, out var value))
            throw Fail(column, "coluna em falta no ficheiro");

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    public string Required(string column)
        => Text(column) ?? throw Fail(column, "valor obrigatório em falta");

    public int? Int(string column)
    {
        var text = Text(column);
        if (text is null)
            return null;

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Fail(column, $"'{text}' não é um número inteiro");
    }

    public decimal? Decimal(string column)
    {
        var text = Text(column)?.Replace(',', '.');
        if (text is null)
            return null;

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Fail(column, $"'{text}' não é um número decimal");
    }

    public bool Bool(string column)
    {
        var text = Text(column)?.ToLowerInvariant();
        return text switch
        {
            null or "0" or "false" or "nao" or "não" or "n" => false,
            "1" or "true" or "sim" or "s" => true,
            _ => throw Fail(column, $"'{text}' não é sim/não"),
        };
    }

    public DateOnly? Date(string column)
    {
        var text = Text(column);
        if (text is null)
            return null;

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value)
            ? value
            : throw Fail(column, $"'{text}' não está no formato yyyy-MM-dd");
    }

    public T? Enum<T>(string column) where T : struct, Enum
    {
        var text = Text(column);
        if (text is null)
            return null;

        return System.Enum.TryParse<T>(text, ignoreCase: true, out var value)
            && System.Enum.IsDefined(value)
            ? value
            : throw Fail(column, $"'{text}' não é um valor válido de {typeof(T).Name}");
    }

    public ImportException Fail(string column, string problem)
        => new($"{File}, linha {Line}, coluna '{column}': {problem}.");

    public ImportException Fail(string problem)
        => new($"{File}, linha {Line}: {problem}.");
}

public static class CsvTable
{
    public static List<CsvRow> Read(string path)
    {
        var file = Path.GetFileName(path);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
        };

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();
        var header = csv.HeaderRecord
            ?? throw new ImportException($"{file}: ficheiro sem linha de cabeçalho.");

        var rows = new List<CsvRow>();
        while (csv.Read())
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Length; i++)
                fields[header[i]] = csv.TryGetField<string>(i, out var value) ? value ?? "" : "";

            rows.Add(new CsvRow(file, csv.Parser.Row, fields));
        }

        return rows;
    }
}
```

- [ ] **Step 5: Correr os testes e confirmar que passam**

Run: `dotnet test tests/Sfc.Web.Tests --filter "FullyQualifiedName~CsvTableTests"`
Expected: PASS, 5 testes.

- [ ] **Step 6: Commit**

```bash
git add src/Sfc.Web/Import/CsvTable.cs src/Sfc.Web/Sfc.Web.csproj tests/Sfc.Web.Tests/Import/CsvTableTests.cs
git commit -m "Add CSV reading with file and line aware errors"
```

---

### Task 2: Relatório de importação e importação de clubes

**Files:**
- Create: `src/Sfc.Web/Import/ImportReport.cs`, `src/Sfc.Web/Import/SeedImporter.cs`
- Test: `tests/Sfc.Web.Tests/Import/SeedImporterTests.cs`

**Interfaces:**
- Consumes: `CsvTable.Read`, `CsvRow`, `ImportException` (Task 1); `ClubService.CreateAsync(ClubInput, Stream?, ct)`, `ClubService.SearchAsync(string?, ct)`, `ClubInput(Name, City, Country, ContactEmail, ContactPhone, CoachesText)`.
- Produces: `ImportReport` com `Created(string file)`, `Skipped(string file)`, `Error(string message)`, `IReadOnlyList<string> Errors`, `int CountCreated(string file)`, `int CountSkipped(string file)`, `bool HasErrors`, `string Summary()`. `SeedImporter(ClubService, AthleteService, EventService, SfcDbContext)` com `Task<ImportReport> ImportAsync(string directory, bool dryRun, CancellationToken ct = default)`.

- [ ] **Step 1: Escrever o teste que falha**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Sfc.Web.Import;
using Sfc.Web.Services;
using Xunit;

namespace Sfc.Web.Tests.Import;

public class SeedImporterTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>, IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sfc-import").FullName;

    private void Write(string name, string content)
        => File.WriteAllText(Path.Combine(_dir, name), content);

    private async Task<ImportReport> RunAsync(bool dryRun = false)
    {
        using var scope = factory.Services.CreateScope();
        var importer = ActivatorUtilities.CreateInstance<SeedImporter>(scope.ServiceProvider);
        return await importer.ImportAsync(_dir, dryRun);
    }

    [Fact]
    public async Task ImportAsync_CreatesClubsWithCoaches()
    {
        Write("clubs.csv",
            "name,city,country,contact_email,contact_phone,coaches\n" +
            "Task2 Scorpion Gym,Lisboa,Portugal,geral@scorpion.pt,912345678,Mestre Rui; rui@scorpion.pt|Kru Ana\n");

        var report = await RunAsync();

        Assert.False(report.HasErrors);
        Assert.Equal(1, report.CountCreated("clubs.csv"));

        using var scope = factory.Services.CreateScope();
        var clubs = scope.ServiceProvider.GetRequiredService<ClubService>();
        var club = Assert.Single(await clubs.SearchAsync("Task2 Scorpion Gym"));
        Assert.Equal("Lisboa", club.City);
        Assert.Equal(2, club.Coaches.Count);
        Assert.Equal("Mestre Rui", club.Coaches[0].Name);
        Assert.Equal("rui@scorpion.pt", club.Coaches[0].Contact);
        Assert.Equal("Kru Ana", club.Coaches[1].Name);
    }

    [Fact]
    public async Task ImportAsync_IsIdempotentByClubName()
    {
        Write("clubs.csv", "name,city\nTask2 Repeat Gym,Porto\n");

        await RunAsync();
        var second = await RunAsync();

        Assert.Equal(0, second.CountCreated("clubs.csv"));
        Assert.Equal(1, second.CountSkipped("clubs.csv"));

        using var scope = factory.Services.CreateScope();
        var clubs = scope.ServiceProvider.GetRequiredService<ClubService>();
        Assert.Single(await clubs.SearchAsync("Task2 Repeat Gym"));
    }

    [Fact]
    public async Task ImportAsync_WithDryRun_WritesNothing()
    {
        Write("clubs.csv", "name,city\nTask2 Ghost Gym,Faro\n");

        var report = await RunAsync(dryRun: true);

        Assert.Equal(1, report.CountCreated("clubs.csv"));

        using var scope = factory.Services.CreateScope();
        var clubs = scope.ServiceProvider.GetRequiredService<ClubService>();
        Assert.Empty(await clubs.SearchAsync("Task2 Ghost Gym"));
    }

    [Fact]
    public async Task ImportAsync_WithBlankName_ReportsErrorAndContinues()
    {
        Write("clubs.csv", "name,city\n,Braga\nTask2 Valid Gym,Braga\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e => e.Contains("clubs.csv") && e.Contains("linha 2"));
        Assert.Equal(1, report.CountCreated("clubs.csv"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
```

- [ ] **Step 2: Correr e confirmar que falha**

Run: `dotnet test tests/Sfc.Web.Tests --filter "FullyQualifiedName~SeedImporterTests"`
Expected: FAIL na compilação — `SeedImporter` e `ImportReport` não existem.

- [ ] **Step 3: Implementar o relatório**

```csharp
namespace Sfc.Web.Import;

/// <summary>Per-file tallies plus human-readable errors. Import never throws on bad
/// rows: it records them and carries on, so one typo does not hide the other twenty.</summary>
public class ImportReport
{
    private readonly Dictionary<string, int> _created = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _skipped = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors => _errors;
    public bool HasErrors => _errors.Count > 0;

    public void Created(string file) => Bump(_created, file);
    public void Skipped(string file) => Bump(_skipped, file);
    public void Error(string message) => _errors.Add(message);

    public int CountCreated(string file) => _created.GetValueOrDefault(file);
    public int CountSkipped(string file) => _skipped.GetValueOrDefault(file);

    public string Summary()
    {
        var files = _created.Keys.Union(_skipped.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        var lines = files.Select(f =>
            $"  {f}: {CountCreated(f)} criados, {CountSkipped(f)} saltados");

        return string.Join(Environment.NewLine, lines);
    }

    private static void Bump(Dictionary<string, int> counter, string file)
        => counter[file] = counter.GetValueOrDefault(file) + 1;
}
```

- [ ] **Step 4: Implementar o importador (só clubes por agora)**

```csharp
using Microsoft.EntityFrameworkCore;
using Sfc.Infrastructure.Persistence;
using Sfc.Web.Services;

namespace Sfc.Web.Import;

/// <summary>
/// One-off seed import from CSV. Goes through the services (not the DbContext) so slug
/// generation, domain validation and record aggregation are exercised for real — the
/// point is to prove the platform, not to fill a database.
/// </summary>
public class SeedImporter(ClubService clubs, AthleteService athletes, EventService events,
    SfcDbContext db)
{
    public async Task<ImportReport> ImportAsync(string directory, bool dryRun,
        CancellationToken ct = default)
    {
        var report = new ImportReport();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await ImportClubsAsync(directory, report, ct);

        if (dryRun)
            await transaction.RollbackAsync(ct);
        else
            await transaction.CommitAsync(ct);

        return report;
    }

    private async Task ImportClubsAsync(string directory, ImportReport report,
        CancellationToken ct)
    {
        const string file = "clubs.csv";
        var path = Path.Combine(directory, file);
        if (!File.Exists(path))
            return;

        var existing = (await db.Clubs.AsNoTracking().Select(c => c.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in CsvTable.Read(path))
        {
            try
            {
                var name = row.Required("name");
                if (!existing.Add(name))
                {
                    report.Skipped(file);
                    continue;
                }

                await clubs.CreateAsync(new ClubInput(name, row.Text("city"), row.Text("country"),
                    row.Text("contact_email"), row.Text("contact_phone"),
                    row.Text("coaches")?.Replace("|", "\n")), logo: null, ct);

                report.Created(file);
            }
            catch (ImportException ex)
            {
                report.Error(ex.Message);
            }
            catch (ArgumentException ex)
            {
                report.Error(row.Fail(ex.Message).Message);
            }
        }
    }
}
```

- [ ] **Step 5: Correr e confirmar que passam**

Run: `dotnet test tests/Sfc.Web.Tests --filter "FullyQualifiedName~SeedImporterTests"`
Expected: PASS, 4 testes.

- [ ] **Step 6: Commit**

```bash
git add src/Sfc.Web/Import/ImportReport.cs src/Sfc.Web/Import/SeedImporter.cs tests/Sfc.Web.Tests/Import/SeedImporterTests.cs
git commit -m "Import clubs from CSV through ClubService"
```

---

### Task 3: Importação de atletas

**Files:**
- Modify: `src/Sfc.Web/Import/SeedImporter.cs`
- Test: `tests/Sfc.Web.Tests/Import/SeedImporterTests.cs`

**Interfaces:**
- Consumes: `AthleteService.CreateAsync(AthleteInput, (int Wins, int Losses, int Draws, int Kos), Stream?, ct)`; `AthleteInput(FirstName, LastName, Nickname, DateOfBirth, Nationality, Discipline, Status, ClubId, CoachName, WeightClass, WeightKg, HeightCm, PublicProfileConsent, Slug, Notes)`.
- Produces: `SeedImporter.ImportAsync` passa a preencher também `athletes.csv`. Chave natural de idempotência: `first_name` + `last_name` + `date_of_birth`.

**Nota de desenho:** a idempotência de atletas usa a chave natural (nome + data de nascimento), **não o slug**. O slug é gerado pelo serviço com resolução de colisões (dois "João Silva" geram `joao-silva` e `joao-silva-2`), por isso não serve para reconhecer quem já existe.

- [ ] **Step 1: Escrever os testes que falham**

Acrescentar a `SeedImporterTests.cs`:

```csharp
    private const string ClubsHeader =
        "name,city,country,contact_email,contact_phone,coaches\n";

    private const string AthletesHeader =
        "first_name,last_name,nickname,date_of_birth,nationality,club_name,coach_name," +
        "discipline,weight_class,weight_kg,height_cm,status,public_profile_consent," +
        "baseline_wins,baseline_losses,baseline_draws,baseline_kos\n";

    [Fact]
    public async Task ImportAsync_CreatesAthleteLinkedToClubWithBaselineRecord()
    {
        Write("clubs.csv", ClubsHeader + "Task3 Home Gym,Lisboa,Portugal,,,\n");
        Write("athletes.csv", AthletesHeader +
            "Rui,Task3Marques,O Falcão,1998-03-11,Portugal,Task3 Home Gym,Mestre Rui," +
            "MuayThai,-72kg,71.4,178,Professional,1,12,3,1,7\n");

        var report = await RunAsync();

        Assert.False(report.HasErrors);
        Assert.Equal(1, report.CountCreated("athletes.csv"));

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AthleteService>();
        var found = await service.SearchAsync("Task3Marques", null, null);
        var item = Assert.Single(found.Items);
        Assert.Equal("12-3-1", item.Record);
        Assert.Equal("Task3 Home Gym", item.ClubName);
    }

    [Fact]
    public async Task ImportAsync_PreservesMinorWithoutConsent()
    {
        Write("athletes.csv", AthletesHeader +
            "Ana,Task3Menor,,2010-06-02,Portugal,,,Kickboxing,-52kg,51.2,160,Amateur,0,0,0,0,0\n");

        await RunAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var athlete = await db.Athletes.SingleAsync(a => a.LastName == "Task3Menor");
        Assert.False(athlete.PublicProfileConsent);
        Assert.Null(athlete.ClubId);
    }

    [Fact]
    public async Task ImportAsync_WithUnknownClub_ReportsErrorAndSkipsAthlete()
    {
        Write("athletes.csv", AthletesHeader +
            "Zeca,Task3Orfao,,1995-01-01,Portugal,Ginásio Que Não Existe,,Boxing,,,,Amateur,1,0,0,0,0\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e => e.Contains("Ginásio Que Não Existe"));
        Assert.Equal(0, report.CountCreated("athletes.csv"));
    }

    [Fact]
    public async Task ImportAsync_IsIdempotentByNameAndDateOfBirth()
    {
        Write("athletes.csv", AthletesHeader +
            "Beto,Task3Repete,,1993-09-09,Portugal,,,MMA,,,,Professional,1,4,4,0,2\n");

        await RunAsync();
        var second = await RunAsync();

        Assert.Equal(0, second.CountCreated("athletes.csv"));
        Assert.Equal(1, second.CountSkipped("athletes.csv"));
    }

    [Fact]
    public async Task ImportAsync_WithKosAboveWins_ReportsErrorWithLineNumber()
    {
        Write("athletes.csv", AthletesHeader +
            "Nuno,Task3Invalido,,1990-01-01,Portugal,,,Boxing,,,,Professional,1,2,0,0,5\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e => e.Contains("athletes.csv") && e.Contains("linha 2"));
    }
```

Acrescentar os `using` em falta ao topo do ficheiro: `Microsoft.EntityFrameworkCore;` e `Sfc.Infrastructure.Persistence;`.

- [ ] **Step 2: Correr e confirmar que falha**

Run: `dotnet test tests/Sfc.Web.Tests --filter "FullyQualifiedName~SeedImporterTests"`
Expected: FAIL — os 5 testes novos falham (nenhum atleta é criado).

- [ ] **Step 3: Implementar**

Em `SeedImporter.ImportAsync`, a seguir a `ImportClubsAsync`:

```csharp
        await ImportAthletesAsync(directory, report, ct);
```

E o método novo:

```csharp
    private async Task ImportAthletesAsync(string directory, ImportReport report,
        CancellationToken ct)
    {
        const string file = "athletes.csv";
        var path = Path.Combine(directory, file);
        if (!File.Exists(path))
            return;

        var clubIds = await db.Clubs.AsNoTracking()
            .ToDictionaryAsync(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase, ct);

        var existing = (await db.Athletes.AsNoTracking()
                .Select(a => new { a.FirstName, a.LastName, a.DateOfBirth })
                .ToListAsync(ct))
            .Select(a => NaturalKey(a.FirstName, a.LastName, a.DateOfBirth))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in CsvTable.Read(path))
        {
            try
            {
                var firstName = row.Required("first_name");
                var lastName = row.Required("last_name");
                var dateOfBirth = row.Date("date_of_birth")
                    ?? throw row.Fail("date_of_birth", "valor obrigatório em falta");

                if (!existing.Add(NaturalKey(firstName, lastName, dateOfBirth)))
                {
                    report.Skipped(file);
                    continue;
                }

                Guid? clubId = null;
                if (row.Text("club_name") is { } clubName)
                {
                    if (!clubIds.TryGetValue(clubName, out var id))
                        throw row.Fail("club_name", $"clube '{clubName}' não existe em clubs.csv");

                    clubId = id;
                }

                var input = new AthleteInput(firstName, lastName, row.Text("nickname"),
                    dateOfBirth, row.Required("nationality"),
                    row.Enum<Discipline>("discipline")
                        ?? throw row.Fail("discipline", "valor obrigatório em falta"),
                    row.Enum<AthleteStatus>("status")
                        ?? throw row.Fail("status", "valor obrigatório em falta"),
                    clubId, row.Text("coach_name"), row.Text("weight_class"),
                    row.Decimal("weight_kg"), row.Int("height_cm"),
                    row.Bool("public_profile_consent"), Slug: null, row.Text("notes"));

                var baseline = (
                    Wins: row.Int("baseline_wins") ?? 0,
                    Losses: row.Int("baseline_losses") ?? 0,
                    Draws: row.Int("baseline_draws") ?? 0,
                    Kos: row.Int("baseline_kos") ?? 0);

                await athletes.CreateAsync(input, baseline, photo: null, ct);
                report.Created(file);
            }
            catch (ImportException ex)
            {
                report.Error(ex.Message);
            }
            catch (ArgumentException ex)
            {
                report.Error(row.Fail(ex.Message).Message);
            }
        }
    }

    private static string NaturalKey(string firstName, string lastName, DateOnly dateOfBirth)
        => $"{firstName.Trim()}|{lastName.Trim()}|{dateOfBirth:yyyy-MM-dd}";
```

Acrescentar ao topo do ficheiro: `using Sfc.Domain.Athletes;`.

- [ ] **Step 4: Correr e confirmar que passam**

Run: `dotnet test tests/Sfc.Web.Tests --filter "FullyQualifiedName~SeedImporterTests"`
Expected: PASS, 9 testes.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Web/Import/SeedImporter.cs tests/Sfc.Web.Tests/Import/SeedImporterTests.cs
git commit -m "Import athletes from CSV with natural-key idempotency"
```

---

### Task 4: Importação de eventos, combates e resultados

**Files:**
- Modify: `src/Sfc.Web/Import/SeedImporter.cs`
- Test: `tests/Sfc.Web.Tests/Import/SeedImporterTests.cs`

**Interfaces:**
- Consumes: `EventService.CreateAsync(EventInput, Stream? banner, Stream? poster, ct)`, `AddFightAsync(Guid eventId, FightInput, ct)`, `SaveResultAsync(Guid eventId, Guid fightId, ResultInput, ct)`, `PublishAsync/CompleteAsync(Guid id, ct)`, `GetWithCardAsync(Guid id, ct)`; `EventInput(Name, Description, Date, Venue, City, TicketsUrl, StreamUrl, Slug)`; `FightInput(RedCornerAthleteId, BlueCornerAthleteId, Discipline, Rounds, RoundDurationMinutes, WeightClass, CatchweightKg, IsTitleFight, IsAmateur)`; `ResultInput(WinnerAthleteId, Method, Round, Time)`.
- Produces: `SeedImporter.ImportAsync` completo (5 ficheiros).

**Ciclo de vida obrigatório:** criar o evento em `Draft` → adicionar os combates pela ordem das linhas → `PublishAsync` → gravar os resultados → `CompleteAsync` se `status` do CSV for `Completed`. `FightBilling` é recalculado sozinho; não há coluna para isso.

- [ ] **Step 1: Escrever o teste que falha**

```csharp
    [Fact]
    public async Task ImportAsync_BuildsCompletedEventAndAggregatesRecords()
    {
        Write("athletes.csv", AthletesHeader +
            "Ivo,Task4Vencedor,,1996-05-05,Portugal,,,MuayThai,-72kg,71.0,177,Professional,1,5,1,0,3\n" +
            "Hugo,Task4Derrotado,,1997-07-07,Portugal,,,MuayThai,-72kg,71.5,175,Professional,1,4,2,0,1\n");
        Write("events.csv",
            "name,slug,date,venue,city,status\n" +
            "Task4 Fight Night,task4-fight-night,2026-05-30T20:00:00Z,Pavilhão,Almada,Completed\n");
        Write("fights.csv",
            "event_slug,order,discipline,rounds,round_duration_minutes,weight_class," +
            "catchweight_kg,is_title_fight,is_amateur,red_athlete_slug,blue_athlete_slug\n" +
            "task4-fight-night,1,MuayThai,3,3,-72kg,,0,0,ivo-task4vencedor,hugo-task4derrotado\n");
        Write("results.csv",
            "event_slug,fight_order,winner_slug,method,round,time\n" +
            "task4-fight-night,1,ivo-task4vencedor,Ko,2,1:45\n");

        var report = await RunAsync();

        Assert.False(report.HasErrors);
        Assert.Equal(1, report.CountCreated("events.csv"));
        Assert.Equal(1, report.CountCreated("fights.csv"));
        Assert.Equal(1, report.CountCreated("results.csv"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();

        var winner = await db.Athletes.SingleAsync(a => a.LastName == "Task4Vencedor");
        Assert.Equal("6-1-0", winner.RecordDisplay);   // baseline 5-1-0 + 1 vitória
        Assert.Equal(4, winner.WinsByKo);              // baseline 3 + 1 KO

        var loser = await db.Athletes.SingleAsync(a => a.LastName == "Task4Derrotado");
        Assert.Equal("4-3-0", loser.RecordDisplay);    // baseline 4-2-0 + 1 derrota

        var evt = await db.Events.SingleAsync(e => e.Slug == "task4-fight-night");
        Assert.Equal(EventStatus.Completed, evt.Status);
    }

    [Fact]
    public async Task ImportAsync_WithUnknownAthleteSlug_ReportsErrorWithLineNumber()
    {
        Write("events.csv",
            "name,slug,date,venue,city,status\n" +
            "Task4 Broken,task4-broken,2026-06-30T20:00:00Z,Pavilhão,Almada,Published\n");
        Write("fights.csv",
            "event_slug,order,discipline,rounds,round_duration_minutes,weight_class," +
            "catchweight_kg,is_title_fight,is_amateur,red_athlete_slug,blue_athlete_slug\n" +
            "task4-broken,1,Boxing,3,3,-67kg,,0,1,nao-existe,tambem-nao-existe\n");

        var report = await RunAsync();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Errors, e => e.Contains("fights.csv") && e.Contains("nao-existe"));
    }
```

Acrescentar `using Sfc.Domain.Events;` ao topo do ficheiro de testes.

- [ ] **Step 2: Correr e confirmar que falha**

Run: `dotnet test tests/Sfc.Web.Tests --filter "FullyQualifiedName~SeedImporterTests"`
Expected: FAIL — nenhum evento é criado.

- [ ] **Step 3: Implementar**

Em `ImportAsync`, a seguir a `ImportAthletesAsync`:

```csharp
        await ImportEventsAsync(directory, report, ct);
```

E:

```csharp
    private async Task ImportEventsAsync(string directory, ImportReport report,
        CancellationToken ct)
    {
        var eventsPath = Path.Combine(directory, "events.csv");
        if (!File.Exists(eventsPath))
            return;

        var athleteIds = await db.Athletes.AsNoTracking()
            .ToDictionaryAsync(a => a.Slug, a => a.Id, StringComparer.OrdinalIgnoreCase, ct);
        var existingSlugs = (await db.Events.AsNoTracking().Select(e => e.Slug).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fightRows = ReadOptional(directory, "fights.csv");
        var resultRows = ReadOptional(directory, "results.csv");
        var eventIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in CsvTable.Read(eventsPath))
        {
            try
            {
                var slug = row.Required("slug");
                if (!existingSlugs.Add(slug))
                {
                    report.Skipped("events.csv");
                    continue;
                }

                var date = DateTime.Parse(row.Required("date"),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal
                        | System.Globalization.DateTimeStyles.AssumeUniversal);

                var evt = await events.CreateAsync(new EventInput(row.Required("name"),
                    row.Text("description"), date, row.Text("venue"), row.Text("city"),
                    row.Text("tickets_url"), row.Text("stream_url"), slug),
                    banner: null, poster: null, ct);

                eventIds[slug] = evt.Id;
                report.Created("events.csv");

                await AddFightsAsync(evt.Id, slug, fightRows, athleteIds, report, ct);

                var status = row.Enum<EventStatus>("status") ?? EventStatus.Draft;
                if (status is EventStatus.Published or EventStatus.Completed)
                    await events.PublishAsync(evt.Id, ct);

                await SaveResultsAsync(evt.Id, slug, resultRows, athleteIds, report, ct);

                if (status is EventStatus.Completed)
                    await events.CompleteAsync(evt.Id, ct);
            }
            catch (ImportException ex)
            {
                report.Error(ex.Message);
            }
            catch (ArgumentException ex)
            {
                report.Error(row.Fail(ex.Message).Message);
            }
        }
    }

    private async Task AddFightsAsync(Guid eventId, string eventSlug, List<CsvRow> fightRows,
        Dictionary<string, Guid> athleteIds, ImportReport report, CancellationToken ct)
    {
        var rows = fightRows
            .Where(r => string.Equals(r.Text("event_slug"), eventSlug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Int("order") ?? 0);

        foreach (var row in rows)
        {
            try
            {
                var result = await events.AddFightAsync(eventId, new FightInput(
                    AthleteId(row, "red_athlete_slug", athleteIds),
                    AthleteId(row, "blue_athlete_slug", athleteIds),
                    row.Enum<Discipline>("discipline")
                        ?? throw row.Fail("discipline", "valor obrigatório em falta"),
                    row.Int("rounds") ?? 3, row.Int("round_duration_minutes") ?? 3,
                    row.Text("weight_class"), row.Decimal("catchweight_kg"),
                    row.Bool("is_title_fight"), row.Bool("is_amateur")), ct);

                if (result is not CardOperationResult.Success)
                    throw row.Fail($"não foi possível adicionar o combate ({result})");

                report.Created("fights.csv");
            }
            catch (ImportException ex)
            {
                report.Error(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                report.Error(row.Fail(ex.Message).Message);
            }
        }
    }

    private async Task SaveResultsAsync(Guid eventId, string eventSlug, List<CsvRow> resultRows,
        Dictionary<string, Guid> athleteIds, ImportReport report, CancellationToken ct)
    {
        var evt = await events.GetWithCardAsync(eventId, ct);
        if (evt is null)
            return;

        var byOrder = evt.Fights.ToDictionary(f => f.Order, f => f.Id);
        var rows = resultRows
            .Where(r => string.Equals(r.Text("event_slug"), eventSlug, StringComparison.OrdinalIgnoreCase));

        foreach (var row in rows)
        {
            try
            {
                var order = row.Int("fight_order")
                    ?? throw row.Fail("fight_order", "valor obrigatório em falta");
                if (!byOrder.TryGetValue(order, out var fightId))
                    throw row.Fail($"não existe combate com ordem {order} em '{eventSlug}'");

                Guid? winnerId = row.Text("winner_slug") is { } slug
                    ? AthleteId(row, "winner_slug", athleteIds)
                    : null;

                var result = await events.SaveResultAsync(eventId, fightId, new ResultInput(
                    winnerId,
                    row.Enum<FightResultMethod>("method")
                        ?? throw row.Fail("method", "valor obrigatório em falta"),
                    row.Int("round"), row.Text("time")), ct);

                if (result is not ResultOperationResult.Success)
                    throw row.Fail($"não foi possível gravar o resultado ({result})");

                report.Created("results.csv");
            }
            catch (ImportException ex)
            {
                report.Error(ex.Message);
            }
        }
    }

    private static Guid AthleteId(CsvRow row, string column, Dictionary<string, Guid> athleteIds)
    {
        var slug = row.Required(column);
        return athleteIds.TryGetValue(slug, out var id)
            ? id
            : throw row.Fail(column, $"não existe atleta com o slug '{slug}'");
    }

    private static List<CsvRow> ReadOptional(string directory, string file)
    {
        var path = Path.Combine(directory, file);
        return File.Exists(path) ? CsvTable.Read(path) : [];
    }
```

Acrescentar ao topo: `using Sfc.Domain.Events;`.

- [ ] **Step 4: Correr toda a suite de import e confirmar**

Run: `dotnet test tests/Sfc.Web.Tests --filter "FullyQualifiedName~Import"`
Expected: PASS, 16 testes.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Web/Import/SeedImporter.cs tests/Sfc.Web.Tests/Import/SeedImporterTests.cs
git commit -m "Import events, fights and results building real athlete records"
```

---

### Task 5: Comando de linha de comandos

**Files:**
- Create: `src/Sfc.Web/Import/ImportCommand.cs`
- Modify: `src/Sfc.Web/Program.cs:56-66`

**Interfaces:**
- Consumes: `SeedImporter.ImportAsync(string, bool, ct)`, `ImportReport`.
- Produces: `ImportCommand.RunAsync(IServiceProvider services, string[] args) → Task<int>` (0 = sucesso, 1 = erros, 2 = argumentos inválidos).

**Porque não há teste automatizado aqui:** este passo é só wiring e escrita na consola; a lógica toda já está coberta pelas Tasks 1–4. A verificação é a execução manual do Step 3.

- [ ] **Step 1: Implementar o comando**

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Sfc.Web.Import;

/// <summary>CLI entry point for the one-off seed import. Console output is pt-PT.</summary>
public static class ImportCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var directory = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (directory is null)
        {
            Console.Error.WriteLine(
                "Utilização: dotnet run --project src/Sfc.Web -- import <pasta> [--dry-run]");
            return 2;
        }

        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"A pasta '{directory}' não existe.");
            return 2;
        }

        var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);

        using var scope = services.CreateScope();
        var importer = ActivatorUtilities.CreateInstance<SeedImporter>(scope.ServiceProvider);

        Console.WriteLine(dryRun
            ? $"A validar '{directory}' (--dry-run: nada será gravado)..."
            : $"A importar de '{directory}'...");

        var report = await importer.ImportAsync(directory, dryRun);

        Console.WriteLine();
        Console.WriteLine(report.Summary());

        if (!report.HasErrors)
        {
            Console.WriteLine(dryRun ? "Validação concluída sem erros." : "Importação concluída.");
            return 0;
        }

        Console.WriteLine();
        Console.Error.WriteLine($"{report.Errors.Count} erro(s):");
        foreach (var error in report.Errors)
            Console.Error.WriteLine($"  - {error}");

        return 1;
    }
}
```

- [ ] **Step 2: Ligar ao `Program.cs`**

Substituir as linhas 64-66 de `src/Sfc.Web/Program.cs`:

```csharp
await DatabaseSeeder.SeedAsync(app.Services, app.Configuration);

if (args.Length > 0 && args[0] == "import")
    return await ImportCommand.RunAsync(app.Services, args[1..]);

app.Run();
return 0;
```

E acrescentar `using Sfc.Web.Import;` ao bloco de `using` do topo (linha 7-9).

`public partial class Program;` fica onde está, no fim do ficheiro — é o que o `WebApplicationFactory<Program>` usa nos testes. Como os testes arrancam com `args` vazios, o ramo de import nunca dispara em teste.

- [ ] **Step 3: Verificar manualmente**

```bash
cd "D:/Users/160173003/Desktop/SFC-EventsPlanner"
docker compose up -d --wait
mkdir -p /tmp/sfc-smoke && printf 'name,city\nSmoke Gym,Setubal\n' > /tmp/sfc-smoke/clubs.csv
dotnet run --project src/Sfc.Web -- import /tmp/sfc-smoke --dry-run
```

Expected: sai com código 0 e imprime `clubs.csv: 1 criados, 0 saltados` seguido de `Validação concluída sem erros.` A app **não** fica a servir HTTP.

- [ ] **Step 4: Confirmar que a app web continua a arrancar**

Run: `dotnet test tests/Sfc.Web.Tests --filter "FullyQualifiedName~StartupTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Sfc.Web/Import/ImportCommand.cs src/Sfc.Web/Program.cs
git commit -m "Add import verb to the web host command line"
```

---

### Task 6: O dataset de mock

**Files:**
- Create: `data/seed/clubs.csv`, `data/seed/athletes.csv`, `data/seed/events.csv`, `data/seed/fights.csv`, `data/seed/results.csv`
- Test: `tests/Sfc.Web.Tests/Import/SeedDatasetTests.cs`

**Interfaces:**
- Consumes: `SeedImporter.ImportAsync` (Tasks 2–4).
- Produces: os CSVs commitados que alimentam o ensaio.

**Invariantes obrigatórios do dataset** (o teste do Step 1 verifica-os — não são sugestões):

| Invariante | Valor |
|---|---|
| Clubes | 10 |
| Atletas | 45 |
| Menores (idade < 18 em 2026-07-21) sem `public_profile_consent` | ≥ 3 |
| Atletas sem `nickname` | ≥ 15 |
| Atletas com baseline totalmente a zeros (estreantes) | ≥ 5 |
| Nacionalidades diferentes de `Portugal` | ≥ 2 |
| `photo` | nenhuma — a coluna não existe |
| Eventos | 2, ambos `Completed`, datas em 2026-02 e 2026-05 |
| Combates | 10 por evento histórico |
| Resultados | um por combate, cobrindo `Ko`, `Tko`, `UnanimousDecision`, `SplitDecision`, `Draw` e `Forfeit` |
| `baseline_kos ≤ baseline_wins` | em todas as linhas |
| Atletas repetidos dentro do mesmo evento | nenhum |

- [ ] **Step 1: Escrever o teste que falha**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfc.Domain.Events;
using Sfc.Infrastructure.Persistence;
using Sfc.Web.Import;
using Xunit;

namespace Sfc.Web.Tests.Import;

/// <summary>Guards the shipped mock dataset: it must stay uncomfortable enough that the
/// dress rehearsal exercises the RGPD, missing-photo and foreign-nationality paths.</summary>
public class SeedDatasetTests(SfcWebApplicationFactory factory)
    : IClassFixture<SfcWebApplicationFactory>
{
    private static string SeedDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "data", "seed")))
            dir = Path.GetDirectoryName(dir);

        Assert.NotNull(dir);
        return Path.Combine(dir, "data", "seed");
    }

    [Fact]
    public async Task ShippedDataset_ImportsWithoutErrorsAndMeetsInvariants()
    {
        using var scope = factory.Services.CreateScope();
        var importer = ActivatorUtilities.CreateInstance<SeedImporter>(scope.ServiceProvider);

        var report = await importer.ImportAsync(SeedDirectory(), dryRun: false);

        Assert.Empty(report.Errors);
        Assert.Equal(10, report.CountCreated("clubs.csv"));
        Assert.Equal(45, report.CountCreated("athletes.csv"));
        Assert.Equal(2, report.CountCreated("events.csv"));
        Assert.Equal(20, report.CountCreated("fights.csv"));
        Assert.Equal(20, report.CountCreated("results.csv"));

        var db = scope.ServiceProvider.GetRequiredService<SfcDbContext>();
        var athletes = await db.Athletes.AsNoTracking().ToListAsync();

        Assert.True(athletes.Count(a => a.Age < 18 && !a.PublicProfileConsent) >= 3,
            "o dataset tem de conter menores sem consentimento — é o que testa o caminho RGPD");
        Assert.True(athletes.Count(a => a.Nickname is null) >= 15);
        Assert.True(athletes.Count(a => a.Wins == 0 && a.Losses == 0 && a.Draws == 0) >= 5);
        Assert.True(athletes.Count(a => a.Nationality != "Portugal") >= 2);
        Assert.All(athletes, a => Assert.Null(a.PhotoUrl));

        var events = await db.Events.AsNoTracking().ToListAsync();
        Assert.All(events, e => Assert.Equal(EventStatus.Completed, e.Status));

        // Records were built by results, not typed in.
        Assert.Contains(athletes, a => a.ResultWins > 0);
        Assert.Contains(athletes, a => a.ResultLosses > 0);
        Assert.Contains(athletes, a => a.ResultKos > 0);
    }
}
```

- [ ] **Step 2: Correr e confirmar que falha**

Run: `dotnet test tests/Sfc.Web.Tests --filter "FullyQualifiedName~SeedDatasetTests"`
Expected: FAIL — `data/seed` não existe.

- [ ] **Step 3: Escrever os CSVs**

Cabeçalhos exatos (os mesmos que os testes das Tasks 2–4 usam):

```
clubs.csv    name,city,country,contact_email,contact_phone,coaches
athletes.csv first_name,last_name,nickname,date_of_birth,nationality,club_name,coach_name,discipline,weight_class,weight_kg,height_cm,status,public_profile_consent,baseline_wins,baseline_losses,baseline_draws,baseline_kos
events.csv   name,slug,date,venue,city,status
fights.csv   event_slug,order,discipline,rounds,round_duration_minutes,weight_class,catchweight_kg,is_title_fight,is_amateur,red_athlete_slug,blue_athlete_slug
results.csv  event_slug,fight_order,winner_slug,method,round,time
```

Excerto a seguir literalmente na forma (nomes portugueses plausíveis, clubes de artes marciais portugueses fictícios):

```csv
name,city,country,contact_email,contact_phone,coaches
Scorpion Gym Lisboa,Lisboa,Portugal,geral@scorpiongym.pt,912000001,Mestre Rui Tavares; rui@scorpiongym.pt|Kru Ana Bastos
Team Leiria Fight,Leiria,Portugal,info@leiriafight.pt,912000002,Mestre Paulo Nunes
Almada Muay Thai,Almada,Portugal,,912000003,Kru Sérgio Lopes; sergio@almadamt.pt
```

```csv
first_name,last_name,nickname,date_of_birth,nationality,club_name,coach_name,discipline,weight_class,weight_kg,height_cm,status,public_profile_consent,baseline_wins,baseline_losses,baseline_draws,baseline_kos
Rui,Tavares,O Falcão,1994-03-11,Portugal,Scorpion Gym Lisboa,Mestre Rui Tavares,MuayThai,-72kg,71.4,178,Professional,1,14,3,1,8
Marta,Ferreira,,1999-08-22,Portugal,Team Leiria Fight,Mestre Paulo Nunes,Kickboxing,-56kg,55.8,164,Professional,1,7,2,0,2
Tiago,Baptista,,2010-04-17,Portugal,Almada Muay Thai,Kru Sérgio Lopes,MuayThai,-45kg,44.6,152,Amateur,0,0,0,0,0
Yassine,El Amrani,,1997-11-30,Marrocos,Scorpion Gym Lisboa,,Kickboxing,-63kg,62.9,172,Professional,1,9,4,1,5
```

Regras a respeitar ao escrever as 45 linhas:

- `date_of_birth` sempre `yyyy-MM-dd`; os ≥ 3 menores nascem entre 2009 e 2011 e têm `public_profile_consent` a `0`
- todos os amadores com menos de 18 anos ficam com `public_profile_consent = 0`
- `weight_class` no formato `-NNkg` (é o que `Fight.WeightLimitKg` sabe fazer parse) ou vazio
- `discipline` ∈ `MuayThai`, `Kickboxing`, `K1`, `Boxing`, `Mma` (verificado em `Discipline.cs`)
- `status` ∈ `Amateur`, `Professional` (verificado em `AthleteStatus.cs`)
- `method` ∈ `Ko`, `Tko`, `UnanimousDecision`, `SplitDecision`, `MajorityDecision`, `Draw`, `NoContest`, `Disqualification`, `Forfeit` (verificado em `FightResultMethod.cs` — **não existe `Submission`**)
- `status` de evento ∈ `Draft`, `Published`, `Completed`, `Cancelled` (verificado em `EventStatus.cs`)
- os slugs usados em `fights.csv`/`results.csv` são os que `SlugGenerator.Generate("Nome Apelido")` produz (minúsculas, sem acentos, hífen) — para `Yassine El Amrani` é `yassine-el-amrani`
- cada evento usa 20 atletas distintos; nenhum atleta aparece duas vezes no mesmo evento
- `Draw` não leva `winner_slug`; decisões não levam `round` nem `time`

- [ ] **Step 4: Correr e confirmar que passa**

Run: `dotnet test tests/Sfc.Web.Tests --filter "FullyQualifiedName~SeedDatasetTests"`
Expected: PASS. Se falhar por contagem, corrigir o CSV — não o teste.

- [ ] **Step 5: Commit**

```bash
git add data/seed tests/Sfc.Web.Tests/Import/SeedDatasetTests.cs
git commit -m "Add mock seed dataset with minors, missing photos and foreign athletes"
```

---

### Task 7: Scripts de backup e restore

**Files:**
- Create: `scripts/backup.ps1`, `scripts/restore.ps1`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: serviço `postgres` do `docker-compose.yml` (utilizador `sfc`, base `sfc_events`).
- Produces: `scripts/backup.ps1 [-OutputDirectory <dir>]` → caminho do dump; `scripts/restore.ps1 -DumpFile <path>`.

- [ ] **Step 1: Proteger os dumps antes de existirem**

Acrescentar ao fim de `.gitignore`:

```gitignore

# Dumps de base de dados — nunca versionados (docs/05-git-workflow.md)
backups/
```

Fazer isto **antes** de correr qualquer backup: `docs/05-git-workflow.md:92` proíbe commitar dumps, e um `git add -A` distraído a seguir ao primeiro backup mete um dump com dados pessoais no repositório.

- [ ] **Step 2: Escrever `scripts/backup.ps1`**

```powershell
<#
.SYNOPSIS
  Dump da base de dados de desenvolvimento (formato custom, restaurável com pg_restore).
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = "backups"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dumpPath = Join-Path $OutputDirectory "sfc_events_$timestamp.dump"

Write-Host "A criar dump em $dumpPath ..."

docker compose exec -T postgres pg_dump -U sfc -d sfc_events -Fc |
    Set-Content -Path $dumpPath -AsByteStream

if ($LASTEXITCODE -ne 0) {
    throw "pg_dump falhou com o codigo $LASTEXITCODE."
}

$sizeKb = [math]::Round((Get-Item $dumpPath).Length / 1KB, 1)
Write-Host "Dump concluido: $dumpPath ($sizeKb KB)"
$dumpPath
```

- [ ] **Step 3: Escrever `scripts/restore.ps1`**

```powershell
<#
.SYNOPSIS
  Restaura um dump por cima da base de desenvolvimento. DESTRUTIVO.
.DESCRIPTION
  Faz DROP DATABASE / CREATE DATABASE e carrega o dump. Nao apaga o volume Docker.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DumpFile,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $DumpFile)) {
    throw "Ficheiro de dump nao encontrado: $DumpFile"
}

if (-not $Force) {
    Write-Host "Isto APAGA a base 'sfc_events' e restaura a partir de $DumpFile." -ForegroundColor Yellow
    Write-Host "Voltar a correr com -Force para confirmar." -ForegroundColor Yellow
    exit 1
}

Write-Host "A terminar ligacoes abertas a sfc_events ..."
docker compose exec -T postgres psql -U sfc -d postgres -c @'
SELECT pg_terminate_backend(pid) FROM pg_stat_activity
WHERE datname = 'sfc_events' AND pid <> pg_backend_pid();
'@ | Out-Null

Write-Host "A recriar a base ..."
docker compose exec -T postgres psql -U sfc -d postgres -c "DROP DATABASE IF EXISTS sfc_events;"
if ($LASTEXITCODE -ne 0) { throw "DROP DATABASE falhou." }

docker compose exec -T postgres psql -U sfc -d postgres -c "CREATE DATABASE sfc_events OWNER sfc;"
if ($LASTEXITCODE -ne 0) { throw "CREATE DATABASE falhou." }

Write-Host "A restaurar $DumpFile ..."
Get-Content -Path $DumpFile -AsByteStream |
    docker compose exec -T postgres pg_restore -U sfc -d sfc_events --no-owner

if ($LASTEXITCODE -ne 0) {
    throw "pg_restore falhou com o codigo $LASTEXITCODE."
}

Write-Host "Restauro concluido."
```

- [ ] **Step 4: Verificar o ciclo com dados descartáveis**

```bash
cd "D:/Users/160173003/Desktop/SFC-EventsPlanner"
docker compose up -d --wait
dotnet run --project src/Sfc.Web -- import ./data/seed
```

Depois, em PowerShell:

```powershell
.\scripts\backup.ps1
```

Guardar o caminho impresso. Contar antes de destruir (ferramenta **Bash**, não PowerShell — as aspas duplas em `psql` via `docker exec` são consumidas pelo PowerShell nesta máquina):

```bash
docker compose exec -T postgres psql -U sfc -d sfc_events -t -c \
  'select (select count(*) from "Athletes"), (select count(*) from "Events"), (select count(*) from "FightResults");'
```

Expected: `45 | 2 | 20`

Restaurar e recontar:

```powershell
.\scripts\restore.ps1 -DumpFile backups\sfc_events_<timestamp>.dump -Force
```

Repetir a query. Expected: os mesmos `45 | 2 | 20`.

- [ ] **Step 5: Commit**

```bash
git add scripts .gitignore
git commit -m "Add database backup and restore scripts"
```

---

### Task 8: Ensaio geral

**Files:** nenhum ficheiro de código. Produz observações para a Task 10.

Esta task é **manual e partilhada**: o Caio conduz o fluxo humano, o assistente mede o custo estrutural. Nada aqui se automatiza — automatizar o ensaio seria testar o script em vez da plataforma.

- [ ] **Step 1: Base limpa com o pano de fundo**

```bash
cd "D:/Users/160173003/Desktop/SFC-EventsPlanner"
docker compose up -d --wait
dotnet run --project src/Sfc.Web -- import ./data/seed --dry-run
dotnet run --project src/Sfc.Web -- import ./data/seed
```

Expected: `--dry-run` sem erros, seguido do import real sem erros.

- [ ] **Step 2: Arrancar backoffice e portal**

Backoffice via `preview_start` (nunca `dotnet run` em background por Bash). Portal em `npm run dev`. Confirmar login com o `SeedAdmin` do `appsettings.Development.json`.

- [ ] **Step 3: O Caio cria o evento piloto, cronometrado**

12 combates, do zero: criar evento → montar card → publicar → pesagens → 12 resultados em sequência. Registar o tempo por combate. Meta: < 30s.

- [ ] **Step 4: Os três desvios obrigatórios**

Executar **durante** o Step 3, não depois:

1. **Falha de peso** — dar a um atleta um peso oficial acima do limite e tentar resolver com peso combinado. Registar o que foi preciso fazer (esperado: bater na ausência de `Fights/Edit`).
2. **Substituição de última hora** — trocar um atleta num combate já com pesagem gravada. Confirmar o que acontece à pesagem do substituído.
3. **Resultado errado** — gravar um vencedor errado, confirmar o record no perfil do atleta, corrigir, e confirmar que o record reverteu.

- [ ] **Step 5: Medição estrutural (assistente)**

Por cada registo de resultado, a 375×812: número de page loads, campos obrigatórios, cliques da lista do card até «guardado», e tempo de resposta de cada POST (`preview_logs`). Idem para uma linha de pesagem. Screenshots não funcionam nesta máquina — verificação por `read_page`/`get_page_text`.

- [ ] **Step 6: Verificar o caminho RGPD no portal**

Com o evento publicado e pesagens aprovadas, ler `/events/<slug>/results` e procurar os menores sem consentimento. Confirmar se nome + peso aparecem. Guardar o output literal — é a prova que sustenta a pendência 1.

- [ ] **Step 7: Lighthouse**

Parar o dev server do portal antes do build (o `.next` fica bloqueado se ambos correrem):

```bash
cd portal && npm run build && npm start
```

Noutro terminal:

```bash
cd portal
npx lighthouse http://localhost:3000/ --preset=desktop --quiet --chrome-flags="--headless" --output=json --output-path=../lighthouse-home.json
```

Repetir com `--form-factor=mobile` para `/`, `/events/<slug>`, `/fighters/<slug>` e `/events/<slug>/results`. Registar os quatro scores (performance, acessibilidade, boas práticas, SEO). Os JSON ficam fora do repositório — só os números vão para a lista de fricções.

---

### Task 9: Runbook

**Files:**
- Create: `docs/runbook.md`

- [ ] **Step 1: Escrever o runbook**

Secções obrigatórias, todas com comandos reais já verificados nas Tasks 5–8:

1. **Arranque local** — `docker compose up -d --wait`, `appsettings.Development.json` com `SeedAdmin`, `dotnet run --project src/Sfc.Web`, `cd portal && npm run dev`. Nota do MinIO (`mc anonymous set download local/sfc-media`) copiada do `README.md:49-51`.
2. **Importar dados** — `dotnet run --project src/Sfc.Web -- import ./data/seed [--dry-run]`, com o contrato de colunas e a nota de que o `--dry-run` valida sem gravar.
3. **Backup** — `.\scripts\backup.ps1`, onde ficam os ficheiros, e que **não são versionados**.
4. **Restore** — `.\scripts\restore.ps1 -DumpFile <path> -Force`, com o aviso de que é destrutivo e a query de verificação (via Bash).
5. **Variáveis de ambiente** — backoffice: `ConnectionStrings:Default`, `Portal:RevalidateUrl`, `Portal:RevalidateSecret`, `Storage:*`, `SeedAdmin:*`. Portal: `SFC_API_BASE`, `PORTAL_REVALIDATE_SECRET`, `NEXT_PUBLIC_IMAGE_HOST`, `NEXT_PUBLIC_SITE_URL`.
6. **Deploy** — secção com o título **«Em aberto — decisão pendente»**, listando o que `docs/03-arquitetura.md:42` prevê (VPS ou serviço gerido; portal na Vercel) e o que falta decidir. Não inventar passos para infraestrutura que não existe.
7. **No dia do evento** — checklist curta: backup antes de começar, confirmar login no telemóvel, confirmar que o portal revalida após publicar um resultado.

- [ ] **Step 2: Commit**

```bash
git add docs/runbook.md
git commit -m "Add operations runbook with backup and restore procedures"
```

---

### Task 10: Lista de fricções e validação de âmbito

**Files:**
- Create: `docs/plans/2026-07-21-friccoes-evento-piloto.md`

- [ ] **Step 1: Escrever a lista**

Tabela com colunas: **Fricção | Onde dói | Evidência | Custo | Classificação**.

Alimentada por: os três desvios da Task 8 Step 4, a medição da Task 8 Step 5, o resultado RGPD da Task 8 Step 6, os scores do Lighthouse da Task 8 Step 7, e as seis pendências herdadas do design (`docs/plans/2026-07-21-evento-piloto-design.md`, secção 5).

Regra de corte, aplicada a cada linha:
- **Correção** — `docs/01-ambito-fase1.md` já promete a capacidade e ela está lenta, confusa ou partida
- **Funcionalidade nova** — seria preciso editar `docs/01-ambito-fase1.md` para a justificar → vai para `docs/ideias-parqueadas.md`, não se implementa

Cada linha leva evidência concreta (tempo medido, output lido, ficheiro:linha). Uma fricção sem evidência é uma opinião.

- [ ] **Step 2: Correr o guardião de âmbito**

Dispatch do agent `guardiao-ambito` com a lista completa. Ele classifica de forma independente; onde discordar da classificação inicial, prevalece a dele ou vai ao Caio.

- [ ] **Step 3: Mover para `ideias-parqueadas.md` o que ficou fora**

- [ ] **Step 4: Commit**

```bash
git add docs/plans/2026-07-21-friccoes-evento-piloto.md docs/ideias-parqueadas.md
git commit -m "Record friction list from the pilot event rehearsal"
```

---

## Fecho da sessão

Antes de abrir o PR (`docs/05-git-workflow.md`):

- [ ] `dotnet build` sem avisos e `dotnet test` verde na suite completa
- [ ] `guardiao-ambito` (Task 10 Step 2)
- [ ] `/security-review` — atenção ao novo pacote CsvHelper e a `scripts/restore.ps1` (é destrutivo)
- [ ] Confirmar que **nenhum dump** ficou staged: `git status --porcelain | grep backups` sem resultados
- [ ] `gh pr create --fill`

**As correções das fricções não entram neste PR.** Este entrega o importador, o dataset, os scripts, o runbook e a lista classificada. As correções saem num PR seguinte, já com o âmbito validado.
