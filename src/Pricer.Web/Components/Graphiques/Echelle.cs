using System.Globalization;

namespace Pricer.Web.Components.Graphiques;

/// <summary>
/// On transforme les valeurs de marché en positions sur le graphique SVG, avec des graduations lisibles
/// plutôt qu'un grand nombre de décimales.
/// </summary>
public sealed class Echelle(double min, double max, double pixelMin, double pixelMax)
{
    public double Min { get; } = min;
    public double Max { get; } = max > min ? max : min + 1;

    /// <summary>
    /// On projette une valeur métier sur la plage de pixels du graphique, y compris lorsque l'axe
    /// vertical est inversé.
    /// </summary>
    public double Vers(double x) => pixelMin + (x - Min) / (Max - Min) * (pixelMax - pixelMin);

    /// <summary>
    /// On écrit une coordonnée SVG avec un point décimal, quelle que soit la langue du système.
    /// </summary>
    public static string P(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// On choisit un pas arrondi pour afficher quelques repères lisibles plutôt que des graduations
    /// trop détaillées.
    /// </summary>
    public IEnumerable<double> Graduations(int nombre = 5)
    {
        double brut = (Max - Min) / nombre;
        double puissance = Math.Pow(10, Math.Floor(Math.Log10(brut)));
        double pas = new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * puissance).First(p => p >= brut);
        for (double v = Math.Ceiling(Min / pas) * pas; v <= Max + 1e-9; v += pas) yield return v;
    }

    /// <summary>
    /// On ajoute une marge autour des valeurs pour que la courbe ne touche pas les bords. On peut aussi
    /// inclure zéro lorsque cela aide à lire le résultat.
    /// </summary>
    public static Echelle Autour(IEnumerable<double> valeurs, double pixelMin, double pixelMax, bool inclureZero, double marge = 0.08)
    {
        var liste = valeurs.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        if (inclureZero) liste.Add(0);
        double min = liste.Count > 0 ? liste.Min() : 0, max = liste.Count > 0 ? liste.Max() : 1;
        double e = (max - min) * marge;
        if (e == 0) e = Math.Max(Math.Abs(max) * 0.1, 1);
        return new Echelle(min - e, max + e, pixelMin, pixelMax);
    }
}
