using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Abstractions;

/// <summary>
/// On utilise le même contrat pour le direct, le rejeu et la démonstration. Les calculs reçoivent le
/// référentiel, les cotations et les règlements sans connaître leur mode de chargement.
/// </summary>
public interface ISourceMarche : IAsyncDisposable
{
    /// <summary>
    /// On charge les caractéristiques des options de la devise pour construire le référentiel.
    /// </summary>
    Task<IReadOnlyList<Instrument>> ChargerOptionsAsync(string devise, CancellationToken ct = default);
    /// <summary>
    /// On charge les futures et leurs pas de négociation pour choisir une couverture réellement cotée.
    /// </summary>
    Task<IReadOnlyList<InstrumentFuture>> ChargerFuturesAsync(string devise, CancellationToken ct = default);
    /// <summary>On charge les résumés d'une devise en un appel afin d'éviter une requête par option.</summary>
    Task<IReadOnlyList<Cotation>> ChargerResumeAsync(string devise, string type, CancellationToken ct = default);
    /// <summary>
    /// On demande une cotation détaillée pour compléter les champs absents du résumé de marché.
    /// </summary>
    Task<Cotation> ObtenirTickerAsync(string instrument, CancellationToken ct = default);
    /// <summary>
    /// On lit l'indice spot et sa date pour convertir le résultat du book en USD.
    /// </summary>
    Task<(double Prix, DateTime Horodatage)> ObtenirIndiceAsync(string devise, CancellationToken ct = default);
    /// <summary>
    /// On récupère les derniers prix officiels de livraison pour régler les jambes échues.
    /// </summary>
    Task<IReadOnlyList<PrixReglement>> ObtenirPrixReglementAsync(string devise, int nombre = 10, CancellationToken ct = default);
    /// <summary>On demande le suivi des instruments utiles au pricer ou aux positions ouvertes.</summary>
    Task SuivreAsync(IEnumerable<string> instruments, CancellationToken ct = default);
    /// <summary>
    /// On expose les cotations au fur et à mesure de leur arrivée, jusqu'à l'annulation de la lecture.
    /// </summary>
    IAsyncEnumerable<Cotation> FluxCotationsAsync(CancellationToken ct);
    bool FluxTermine { get; }
}
