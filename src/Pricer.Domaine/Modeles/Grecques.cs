namespace Pricer.Domaine.Modeles;

/// <summary>
/// On conserve des unités communes : delta en unités de sous-jacent, gamma par USD, vega en USD par
/// point de vol et theta en USD par jour. On additionne ensuite les risques pondérés par les quantités
/// signées avec les opérateurs + et *.
/// </summary>
public readonly record struct Grecques(double Delta, double Gamma, double Vega, double Theta)
{
    public static Grecques Zero => new(0, 0, 0, 0);

    /// <summary>
    /// On additionne les quatre sensibilités composante par composante pour agréger des jambes dans les
    /// mêmes unités.
    /// </summary>
    public static Grecques operator +(Grecques a, Grecques b) =>
        new(a.Delta + b.Delta, a.Gamma + b.Gamma, a.Vega + b.Vega, a.Theta + b.Theta);

    /// <summary>
    /// On retire les sensibilités d'une position pour mesurer l'écart de risque entre deux ensembles.
    /// </summary>
    public static Grecques operator -(Grecques a, Grecques b) => a + (-1.0 * b);

    /// <summary>
    /// On pondère chaque grecque par la quantité signée ; une vente inverse ainsi le signe de l'exposition.
    /// </summary>
    public static Grecques operator *(double quantite, Grecques g) =>
        new(quantite * g.Delta, quantite * g.Gamma, quantite * g.Vega, quantite * g.Theta);
}
