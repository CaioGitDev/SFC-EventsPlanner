namespace Sfc.Domain.Clubs;

/// <summary>
/// Simple value object — coaches are not users in Fase 1 (docs/02-modelo-dominio.md).
/// Persisted as part of the Club JSON column.
/// </summary>
public class Coach
{
    public string Name { get; private set; }
    public string? Contact { get; private set; }

    private Coach()
    {
        Name = null!;
    }

    public Coach(string name, string? contact = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Coach name is required.", nameof(name));

        Name = name.Trim();
        Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim();
    }
}
