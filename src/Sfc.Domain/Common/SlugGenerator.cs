using System.Globalization;
using System.Text;

namespace Sfc.Domain.Common;

/// <summary>
/// Generates URL-safe slugs. Uniqueness is the caller's responsibility
/// (domain rule 7: unique per entity within the organization).
/// </summary>
public static class SlugGenerator
{
    public static string Generate(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input is required.", nameof(input));

        var normalized = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }

        var slug = CollapseHyphens(builder.ToString());
        if (slug.Length == 0)
            throw new ArgumentException("Input contains no alphanumeric characters.", nameof(input));

        return slug;
    }

    private static string CollapseHyphens(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasHyphen = true; // trims leading hyphens
        foreach (var c in value)
        {
            if (c == '-')
            {
                if (!previousWasHyphen)
                    builder.Append('-');
                previousWasHyphen = true;
            }
            else
            {
                builder.Append(c);
                previousWasHyphen = false;
            }
        }

        return builder.ToString().TrimEnd('-');
    }
}
