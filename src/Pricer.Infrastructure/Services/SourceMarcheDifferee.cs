using Pricer.Domaine.Abstractions;
using Pricer.Domaine.Modeles;

namespace Pricer.Infrastructure.Services;

/// <summary>On ouvre la source au démarrage du marché pour afficher les erreurs dans l'interface.</summary>
/// <param name="creer">On évalue cette fonction seulement au premier accès aux données du marché.</param>
public sealed class SourceMarcheDifferee(Func<ISourceMarche> creer) : ISourceMarche
{
    public ISourceMarche? SourceActuelle { get; private set; }
    private ISourceMarche Interne => SourceActuelle ??= creer();
    public bool FluxTermine => SourceActuelle?.FluxTermine ?? false;
    /// <summary>
    /// On ouvre la source au premier besoin puis on lui délègue le chargement des options.
    /// </summary>
    public Task<IReadOnlyList<Instrument>> ChargerOptionsAsync(string d, CancellationToken ct = default) => Interne.ChargerOptionsAsync(d, ct);
    /// <summary>
    /// On ouvre la source si nécessaire puis on charge les futures par son intermédiaire.
    /// </summary>
    public Task<IReadOnlyList<InstrumentFuture>> ChargerFuturesAsync(string d, CancellationToken ct = default) => Interne.ChargerFuturesAsync(d, ct);
    /// <summary>
    /// On délègue le résumé demandé à la source créée au premier accès.
    /// </summary>
    public Task<IReadOnlyList<Cotation>> ChargerResumeAsync(string d, string type, CancellationToken ct = default) => Interne.ChargerResumeAsync(d, type, ct);
    /// <summary>
    /// On transmet la demande de ticker à la source sans l’ouvrir au moment de construire les services.
    /// </summary>
    public Task<Cotation> ObtenirTickerAsync(string n, CancellationToken ct = default) => Interne.ObtenirTickerAsync(n, ct);
    /// <summary>
    /// On délègue la lecture du prix d’indice et de son horodatage à la source différée.
    /// </summary>
    public Task<(double Prix, DateTime Horodatage)> ObtenirIndiceAsync(string d, CancellationToken ct = default) => Interne.ObtenirIndiceAsync(d, ct);
    /// <summary>
    /// On transmet à la source la devise et le nombre de prix de règlement à charger.
    /// </summary>
    public Task<IReadOnlyList<PrixReglement>> ObtenirPrixReglementAsync(string d, int n = 10, CancellationToken ct = default) => Interne.ObtenirPrixReglementAsync(d, n, ct);
    /// <summary>
    /// On délègue l’abonnement aux instruments à la source réellement utilisée par la session.
    /// </summary>
    public Task SuivreAsync(IEnumerable<string> n, CancellationToken ct = default) => Interne.SuivreAsync(n, ct);
    /// <summary>
    /// On expose le flux de la source sans modifier les cotations ni leur ordre d’arrivée.
    /// </summary>
    public IAsyncEnumerable<Cotation> FluxCotationsAsync(CancellationToken ct) => Interne.FluxCotationsAsync(ct);
    /// <summary>
    /// On libère la source seulement si elle a déjà été créée ; fermer le service ne doit pas provoquer
    /// son ouverture.
    /// </summary>
    public ValueTask DisposeAsync() => SourceActuelle?.DisposeAsync() ?? ValueTask.CompletedTask;
}
