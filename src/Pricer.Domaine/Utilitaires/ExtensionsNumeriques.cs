using System.Globalization;

namespace Pricer.Domaine.Utilitaires;

/// <summary>
/// Rounds and formats used by calculations are grouped together to maintain the same
/// conventions everywhere.
/// </summary>
public static class ExtensionsNumeriques
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Rounding to the nearest multiple of steps. Decimal is used to prevent binary
    /// inaccuracies from turning 0.1 times 3 into an out-of-step amount.
    /// </summary>
    public static double ArrondirAuPas(this double valeur, double pas)
    {
        if (pas <= 0) throw new ArgumentOutOfRangeException(nameof(pas), "Le pas doit être strictement positif.");
        decimal p = (decimal)pas;
        decimal n = Math.Round((decimal)valeur / p, MidpointRounding.AwayFromZero);
        return (double)(n * p);
    }

    /// <summary>
    /// It is verified that the quantity is a multiple step taking into account the small
    /// numerical tolerance.
    /// </summary>
    public static bool EstMultipleDe(this double valeur, double pas)
    {
        decimal ratio = (decimal)valeur / (decimal)pas;
        return Math.Abs(ratio - Math.Round(ratio)) < 1e-9m;
    }

    /// <summary>
    /// A BTC amount is formatted with six decimal places and its unit for outputs that
    /// explicitly use this currency.
    /// </summary>
    public static string EnBtc(this double valeur) => valeur.ToString("0.000000", Fr) + " BTC";
    /// <summary>
    /// A USD amount is formatted with two decimal places and the French separators are
    /// formatted for text output.
    /// </summary>
    public static string EnUsd(this double valeur) => valeur.ToString("#,##0.00", Fr) + " $";
    /// <summary>
    /// From a decimal volatility to a percentage display with two decimal places.
    /// </summary>
    public static string EnPointsDeVol(this double vol) => (vol * 100).ToString("0.00", Fr) + " %";
    /// <summary>
    /// Decimal percentage variation is displayed by keeping the sign.
    /// </summary>
    public static string EnPourcentage(this double x) => (x * 100).ToString("+0;-0;0", Fr) + " %";
}
