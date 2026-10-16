namespace Lilia.Admin;

/// <summary>Dates as the operator reads them: Paris time, short.</summary>
public static class Fmt
{
    private static readonly TimeZoneInfo Paris = TryZone("Europe/Paris");

    public static string When(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Paris).ToString("d MMM HH:mm");

    public static string When(DateTime? utc) => utc is { } u ? When(u) : "–";

    public static string Day(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Paris).ToString("d MMM yyyy");

    public static string Number(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");

    private static TimeZoneInfo TryZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }
}
