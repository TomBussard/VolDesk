using System.Globalization;

namespace Pricer.Web.Components.Graphiques;

/// <summary>
/// Market values are transformed into positions on the SVG chart, with legible graduations
/// rather than a large number of decimal places.
/// </summary>
public sealed class Echelle(double min, double max, double pixelMin, double pixelMax)
{
    public double Min { get; } = min;
    public double Max { get; } = max > min ? max : min + 1;

    /// <summary>
    /// A business value is projected over the pixels range of the graph, including when the
    /// vertical axis is reversed.
    /// </summary>
    public double Vers(double x) => pixelMin + (x - Min) / (Max - Min) * (pixelMax - pixelMin);

    /// <summary>
    /// A SVG coordinate is written with a decimal point, regardless of the language of the
    /// system.
    /// </summary>
    public static string P(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// A rounded step is chosen to display a few legible markers rather than too detailed
    /// graduations.
    /// </summary>
    public IEnumerable<double> Graduations(int nombre = 5)
    {
        double brut = (Max - Min) / nombre;
        double puissance = Math.Pow(10, Math.Floor(Math.Log10(brut)));
        double pas = new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * puissance).First(p => p >= brut);
        for (double v = Math.Ceiling(Min / pas) * pas; v <= Max + 1e-9; v += pas) yield return v;
    }

    /// <summary>
    /// Add a margin around the values so that the curve does not touch the edges. You can also
    /// include zero when it helps to read the result.
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
