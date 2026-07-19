using Sfc.Domain.Athletes;
using Sfc.Domain.Events;

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

    public static string ToDisplay(this EventStatus status) => status switch
    {
        EventStatus.Draft => "Rascunho",
        EventStatus.Published => "Publicado",
        EventStatus.Completed => "Concluído",
        EventStatus.Cancelled => "Cancelado",
        _ => status.ToString(),
    };

    public static string ToDisplay(this FightBilling billing) => billing switch
    {
        FightBilling.Main => "Combate principal",
        FightBilling.CoMain => "Co-main",
        FightBilling.Card => "Card",
        _ => billing.ToString(),
    };

    public static string ToDisplay(this FightStatus status) => status switch
    {
        FightStatus.Scheduled => "Agendado",
        FightStatus.Completed => "Concluído",
        FightStatus.Cancelled => "Cancelado",
        FightStatus.NoContest => "No contest",
        _ => status.ToString(),
    };

    public static string ToDisplay(this FightResultMethod method) => method switch
    {
        FightResultMethod.Ko => "KO",
        FightResultMethod.Tko => "TKO",
        FightResultMethod.UnanimousDecision => "Decisão unânime",
        FightResultMethod.SplitDecision => "Decisão dividida",
        FightResultMethod.MajorityDecision => "Decisão por maioria",
        FightResultMethod.Draw => "Empate",
        FightResultMethod.NoContest => "No contest",
        FightResultMethod.Disqualification => "Desqualificação",
        FightResultMethod.Forfeit => "Desistência",
        _ => method.ToString(),
    };

    /// <summary>Readable pt-PT result line, e.g. "Vitória de Ana Silva por KO — R2 1:34".</summary>
    public static string? ResultSummary(this Fight fight)
    {
        var result = fight.Result;
        if (result is null)
            return null;
        if (result.Method == FightResultMethod.Draw)
            return "Empate";
        if (result.Method == FightResultMethod.NoContest)
            return "No contest";

        var winner = result.WinnerAthleteId == fight.RedCornerAthleteId
            ? fight.RedCornerAthlete
            : fight.BlueCornerAthlete;
        var name = winner is null ? "?" : $"{winner.FirstName} {winner.LastName}";
        var summary = $"Vitória de {name} por {result.Method.ToDisplay()}";
        if (result.Round is not null)
            summary += $" — R{result.Round}";
        if (result.Time is not null)
            summary += $" {result.Time}";
        return summary;
    }
}
