using Pricer.Domaine.Abstractions;
using Pricer.Domaine.Modeles;

namespace Pricer.Infrastructure.Services;

/// <summary>
/// The source is opened at market start to display errors in the interface.
/// </summary>
/// <param name="creer">On évalue cette fonction seulement au premier accès aux données du marché.</param>
public sealed class SourceMarcheDifferee(Func<ISourceMarche> creer) : ISourceMarche
{
    public ISourceMarche? SourceActuelle { get; private set; }
    private ISourceMarche Interne => SourceActuelle ??= creer();
    public bool FluxTermine => SourceActuelle?.FluxTermine ?? false;
    /// <summary>
    /// The source is opened when the first need arises and then the option loading is
    /// delegated.
    /// </summary>
    public Task<IReadOnlyList<Instrument>> ChargerOptionsAsync(string d, CancellationToken ct = default) => Interne.ChargerOptionsAsync(d, ct);
    /// <summary>
    /// The source is opened if necessary and the futures are loaded through it.
    /// </summary>
    public Task<IReadOnlyList<InstrumentFuture>> ChargerFuturesAsync(string d, CancellationToken ct = default) => Interne.ChargerFuturesAsync(d, ct);
    /// <summary>
    /// The requested summary is delegated to the source created at the first access.
    /// </summary>
    public Task<IReadOnlyList<Cotation>> ChargerResumeAsync(string d, string type, CancellationToken ct = default) => Interne.ChargerResumeAsync(d, type, ct);
    /// <summary>
    /// The request for a ticker is sent to the source without opening at the time of building
    /// the services.
    /// </summary>
    public Task<Cotation> ObtenirTickerAsync(string n, CancellationToken ct = default) => Interne.ObtenirTickerAsync(n, ct);
    /// <summary>
    /// The index price and its time stamping are delegated to the deferred source.
    /// </summary>
    public Task<(double Prix, DateTime Horodatage)> ObtenirIndiceAsync(string d, CancellationToken ct = default) => Interne.ObtenirIndiceAsync(d, ct);
    /// <summary>
    /// The currency and number of settlement prices to be charged shall be transmitted to the
    /// source.
    /// </summary>
    public Task<IReadOnlyList<PrixReglement>> ObtenirPrixReglementAsync(string d, int n = 10, CancellationToken ct = default) => Interne.ObtenirPrixReglementAsync(d, n, ct);
    /// <summary>
    /// The subscription to the instruments at the source actually used by the session is
    /// delegated.
    /// </summary>
    public Task SuivreAsync(IEnumerable<string> n, CancellationToken ct = default) => Interne.SuivreAsync(n, ct);
    /// <summary>
    /// The flow of the source is exposed without changing the ratings or their order of
    /// arrival.
    /// </summary>
    public IAsyncEnumerable<Cotation> FluxCotationsAsync(CancellationToken ct) => Interne.FluxCotationsAsync(ct);
    /// <summary>
    /// The source is released only if it has already been created; closing the service must not
    /// cause its opening.
    /// </summary>
    public ValueTask DisposeAsync() => SourceActuelle?.DisposeAsync() ?? ValueTask.CompletedTask;
}
