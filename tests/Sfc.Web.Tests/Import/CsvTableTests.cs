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
        // Two columns on purpose: in a single-column file a blank line and a row with an
        // empty required field are indistinguishable, and this test is about the latter.
        var path = WriteCsv("athletes.csv", "first_name,city\n,Braga\n");

        var rows = CsvTable.Read(path);
        var ex = Assert.Throws<ImportException>(() => rows[0].Required("first_name"));

        Assert.Contains("athletes.csv", ex.Message);
        Assert.Contains("2", ex.Message);
        Assert.Contains("first_name", ex.Message);
    }

    [Fact]
    public void Read_SkipsBlankLinesWithoutInventingRows()
    {
        // Humans leave stray blank lines when editing spreadsheets. A phantom all-empty
        // row would later fail validation pointing at a line that holds no data.
        var path = WriteCsv("clubs.csv",
            "name,city\nScorpion Gym,Lisboa\n\nLeiria Fight,Leiria\n\n");

        var rows = CsvTable.Read(path);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Scorpion Gym", rows[0].Required("name"));
        Assert.Equal("Leiria Fight", rows[1].Required("name"));
        Assert.Equal(4, rows[1].Line);   // physical line, blank line counted
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

    [Fact]
    public void Read_WithUnknownHeaderColumn_ThrowsNamingColumnAndFile()
    {
        // A typo'd or renamed header (e.g. email_contacto instead of contact_email) used to
        // silently produce a blank field on every row. Validating against the known column
        // list at file level restores that signal without making Text() throw again.
        var path = WriteCsv("clubs.csv", "name,email_contacto\nScorpion Gym,geral@scorpion.pt\n");

        var ex = Assert.Throws<ImportException>(
            () => CsvTable.Read(path, "name", "city", "contact_email"));

        Assert.Contains("clubs.csv", ex.Message);
        Assert.Contains("email_contacto", ex.Message);
    }

    [Fact]
    public void Read_WithHeaderDeclaringOnlySubsetOfKnownColumns_IsAccepted()
    {
        // Fixtures legitimately declare only the columns that matter for that batch — a
        // header missing some known columns is not an error, only an unrecognised one is.
        var path = WriteCsv("clubs.csv", "name,city\nScorpion Gym,Lisboa\n");

        var rows = CsvTable.Read(path, "name", "city", "country", "contact_email");

        Assert.Single(rows);
        Assert.Equal("Scorpion Gym", rows[0].Required("name"));
    }

    [Fact]
    public void Text_WhenColumnAbsentFromHeader_ReturnsNull()
    {
        // Direct coverage for the lenient Text() behaviour — previously only exercised
        // indirectly through SeedImporterTests.
        var path = WriteCsv("clubs.csv", "name\nScorpion Gym\n");

        var rows = CsvTable.Read(path);

        Assert.Null(rows[0].Text("contact_email"));
    }

    [Fact]
    public void Fail_WithProblemEndingInPeriod_DoesNotDoubleThePeriod()
    {
        var path = WriteCsv("a.csv", "name\nJoao\n");
        var row = CsvTable.Read(path)[0];

        var ex = row.Fail("Coach name is required.");

        Assert.Equal("a.csv, linha 2: Coach name is required.", ex.Message);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
