using System.Globalization;

namespace Pricer.Web.Services;

/// <summary>Desk display conventions. A missing quote has its own label so it cannot be read as zero.</summary>
public static class Format
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("en-US");

    public static string Btc(double? x, int decimales = 6) => x is double v && double.IsFinite(v) ? v.ToString("N" + decimales, DisplayCulture) : "n/a";
    public static string Usd(double? x, int decimales = 2) => Btc(x, decimales);
    public static string Nombre(double? x, string format) => x is double v && double.IsFinite(v) ? v.ToString(format, DisplayCulture) : "n/a";
    public static string Pct(double? x, int decimales = 2) => x is double v && double.IsFinite(v) ? (v * 100).ToString("N" + decimales, DisplayCulture) + " %" : "n/a";

    // Rounding can produce negative zero for a small loss. Display it as zero.
    public static string Signe(double? x, int decimales)
    {
        if (x is not double v || !double.IsFinite(v)) return "n/a";
        double r = Math.Round(v, decimales);
        if (r == 0) r = 0;
        string f = "0." + new string('0', decimales);
        return r.ToString("+" + f + ";-" + f + ";" + f, DisplayCulture);
    }

    public static string Echeance(DateTime e) => e.ToString("dd MMM yy", DisplayCulture) + " · " + e.ToString("HH:mm", DisplayCulture);
    public static string Court(DateTime e) => e.ToString("ddMMMyy", CultureInfo.InvariantCulture).ToUpperInvariant();
    public static string ClasseSigne(double? x) => x is > 0 ? "pos" : x is < 0 ? "neg" : "";
}
