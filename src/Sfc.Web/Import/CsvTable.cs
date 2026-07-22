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

    // A column absent from the header is treated the same as a blank value: seed files are
    // hand-trimmed to the columns that matter for that batch (e.g. a clubs.csv with just
    // name/city), so optional columns are genuinely optional at the file level too. Required()
    // still gives a clear error for whichever case applies.
    public string? Text(string column)
    {
        var trimmed = fields.GetValueOrDefault(column, "").Trim();
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

    // Trim any trailing period from the caller's problem text first: Fail() always appends
    // its own, and domain exception messages (e.g. "Coach name is required.") already end
    // in one, which used to produce a doubled "..".
    public ImportException Fail(string column, string problem)
        => new($"{File}, linha {Line}, coluna '{column}': {problem.TrimEnd('.')}.");

    public ImportException Fail(string problem)
        => new($"{File}, linha {Line}: {problem.TrimEnd('.')}.");
}

public static class CsvTable
{
    /// <summary>
    /// Reads a CSV file. When <paramref name="knownColumns"/> is non-empty, the header is
    /// validated against it once: any header column not in the list is a mistyped or
    /// renamed column and fails the whole file up front, rather than silently reading as
    /// blank on every row (CsvRow.Text is lenient about absent columns for exactly that
    /// reason — this is the file-level signal that replaces it). A header that only
    /// declares a subset of known columns is fine; fixtures legitimately do that.
    /// </summary>
    public static List<CsvRow> Read(string path, params string[] knownColumns)
    {
        var file = Path.GetFileName(path);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            // Blank lines are skipped (CsvHelper default). Real input is a human-maintained
            // Excel export, so stray blank lines are expected; without this a blank line
            // becomes a phantom all-empty row instead of being ignored.
            IgnoreBlankLines = true,
        };

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();
        var header = csv.HeaderRecord
            ?? throw new ImportException($"{file}: ficheiro sem linha de cabeçalho.");

        if (knownColumns.Length > 0)
        {
            var known = new HashSet<string>(knownColumns, StringComparer.OrdinalIgnoreCase);
            var unknown = header.FirstOrDefault(column => !known.Contains(column));
            if (unknown is not null)
                throw new ImportException($"{file}: coluna desconhecida '{unknown}'.");
        }

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
