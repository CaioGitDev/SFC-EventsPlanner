using Sfc.Domain.Athletes;

namespace Sfc.Web.Services;

/// <summary>UI display names in pt-PT (code stays in English — CLAUDE.md rule 4).</summary>
public static class PtDisplay
{
    public static string ToDisplay(this Discipline discipline) => discipline switch
    {
        Discipline.MuayThai => "Muay Thai",
        Discipline.Kickboxing => "Kickboxing",
        Discipline.K1 => "K1",
        Discipline.Boxing => "Boxe",
        Discipline.Mma => "MMA",
        _ => discipline.ToString(),
    };

    public static string ToDisplay(this AthleteStatus status) => status switch
    {
        AthleteStatus.Amateur => "Amador",
        AthleteStatus.Professional => "Profissional",
        _ => status.ToString(),
    };
}
