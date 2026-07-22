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
