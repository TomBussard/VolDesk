using System.Globalization;

namespace Pricer.Web.Services;

/// <summary>
/// On utilise les mêmes formats français dans toutes les pages et on distingue une valeur absente d'un
/// résultat nul.
/// </summary>
public static class Format
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// On formate une valeur crypto avec la précision demandée ; une valeur absente ou non finie reste
    /// indisponible.
    /// </summary>
    public static string Btc(double? x, int decimales = 6) => x is double v && double.IsFinite(v) ? v.ToString("N" + decimales, Fr) : "—";
    /// <summary>
    /// On affiche le montant USD avec les mêmes séparateurs français que les autres valeurs numériques.
    /// </summary>
    public static string Usd(double? x, int decimales = 2) => Btc(x, decimales);
    /// <summary>
    /// On applique le format numérique choisi en conservant un indicateur d'indisponibilité pour les
    /// données absentes.
    /// </summary>
    public static string Nombre(double? x, string format) => x is double v && double.IsFinite(v) ? v.ToString(format, Fr) : "—";
    /// <summary>
    /// On convertit une proportion décimale en pourcentage avant de l'afficher avec son unité.
    /// </summary>
    public static string Pct(double? x, int decimales = 2) => x is double v && double.IsFinite(v) ? (v * 100).ToString("N" + decimales, Fr) + " %" : "—";

    /// <summary>
    /// On ajoute un signe aux résultats positifs pour distinguer les gains des pertes dans les
    /// tableaux.
    /// </summary>
    public static string Signe(double? x, int decimales)
    {
        if (x is not double v || !double.IsFinite(v)) return "—";
        double r = Math.Round(v, decimales);
        if (r == 0) r = 0;
        string f = "0." + new string('0', decimales);
        return r.ToString("+" + f + ";-" + f + ";" + f, Fr);
    }

    /// <summary>
    /// On affiche la date et l'heure de l'échéance pour distinguer les contrats qui ne se règlent pas
    /// au même instant.
    /// </summary>
    public static string Echeance(DateTime e) => e.ToString("dd MMM yy", Fr) + " · " + e.ToString("HH:mm", Fr);
    /// <summary>
    /// On produit le libellé court de la date, indépendant de la langue de Windows, pour les tableaux
    /// de marché.
    /// </summary>
    public static string Court(DateTime e) => e.ToString("ddMMMyy", CultureInfo.InvariantCulture).ToUpperInvariant();
    /// <summary>
    /// On choisit la couleur associée au signe de la valeur ; zéro et les données absentes restent
    /// neutres.
    /// </summary>
    public static string ClasseSigne(double? x) => x is > 0 ? "pos" : x is < 0 ? "neg" : "";
}
