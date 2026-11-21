namespace Pricer.Domaine.Modeles;

/// <summary>
/// Common units are retained: delta in underlying units, gamma per USD, vega in USD per volatility
/// point and theta in USD per day. The risk weighted by the quantities signed with operators +
/// and * is then added.
/// </summary>
public readonly record struct Grecques(double Delta, double Gamma, double Vega, double Theta)
{
    public static Grecques Zero => new(0, 0, 0, 0);

    /// <summary>
    /// The four component sensitivities per component are added together to aggregate legs in
    /// the same units.
    /// </summary>
    public static Grecques operator +(Grecques a, Grecques b) =>
        new(a.Delta + b.Delta, a.Gamma + b.Gamma, a.Vega + b.Vega, a.Theta + b.Theta);

    /// <summary>
    /// The sensitivities of a position are removed to measure the risk gap between two sets.
    /// </summary>
    public static Grecques operator -(Grecques a, Grecques b) => a + (-1.0 * b);

    /// <summary>
    /// We weigh each Greek by the signed quantity; a sale reverses so the sign of the
    /// exhibition.
    /// </summary>
    public static Grecques operator *(double quantite, Grecques g) =>
        new(quantite * g.Delta, quantite * g.Gamma, quantite * g.Vega, quantite * g.Theta);
}
