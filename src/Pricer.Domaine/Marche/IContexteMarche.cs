using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Marche;

/// <summary>
/// The pricer and book are given a market reading view to separate the calculations from the
/// data arrival.
/// </summary>
public interface IContexteMarche
{
    ReferentielOptions Referentiel { get; }
    DateTime Maintenant { get; }
    double? Indice { get; }
    /// <summary>
    /// The final rating of an instrument is requested by accepting that it is still
    /// unavailable.
    /// </summary>
    Cotation? Cotation(string nomInstrument);
    /// <summary>
    /// A future exploitable for maturity is sought from recent options, then from the future
    /// side of the same maturity.
    /// </summary>
    double? Future(DateTime echeance);
    /// <summary>
    /// Smiles calculated from marks are exposed to re-use the same interpolation in all OTC
    /// calculations.
    /// </summary>
    IReadOnlyList<SmileEcheance> Smiles();
}
