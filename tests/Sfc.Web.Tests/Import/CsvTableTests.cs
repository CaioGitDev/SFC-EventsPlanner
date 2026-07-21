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
