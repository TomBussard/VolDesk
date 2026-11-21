using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Marche;

/// <summary>On donne au pricer et au book une vue en lecture du marché pour séparer les calculs de l'arrivée des données.</summary>
public interface IContexteMarche
{
    ReferentielOptions Referentiel { get; }
    DateTime Maintenant { get; }
    double? Indice { get; }
    /// <summary>
    /// On demande la dernière cotation d'un instrument en acceptant qu'elle soit encore indisponible.
    /// </summary>
    Cotation? Cotation(string nomInstrument);
    /// <summary>
    /// On cherche un future exploitable pour l'échéance à partir des options récentes, puis du future coté de
    /// même échéance.
    /// </summary>
    double? Future(DateTime echeance);
    /// <summary>
    /// On expose les smiles calculés à partir des marks pour réutiliser la même interpolation dans tous les
    /// calculs OTC.
    /// </summary>
    IReadOnlyList<SmileEcheance> Smiles();
}
