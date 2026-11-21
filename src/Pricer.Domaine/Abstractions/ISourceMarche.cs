using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Abstractions;

/// <summary>
/// Live quotes, replay files and the offline demo supply the pricing engine through this interface.
/// </summary>
public interface ISourceMarche : IAsyncDisposable
{
    /// <summary>
    /// Load the option contracts listed for the selected currency.
    /// </summary>
    Task<IReadOnlyList<Instrument>> ChargerOptionsAsync(string devise, CancellationToken ct = default);
    /// <summary>
    /// Load listed futures and their trade sizes so the simulated hedge can be rounded to an available contract.
    /// </summary>
    Task<IReadOnlyList<InstrumentFuture>> ChargerFuturesAsync(string devise, CancellationToken ct = default);
    /// <summary>
    /// The abstracts of a currency are loaded in one call to avoid one request per option.
    /// </summary>
    Task<IReadOnlyList<Cotation>> ChargerResumeAsync(string devise, string type, CancellationToken ct = default);
    /// <summary>
    /// Detailed quotes are requested to complete the fields missing from the contract
    /// summary.
    /// </summary>
    Task<Cotation> ObtenirTickerAsync(string instrument, CancellationToken ct = default);
    /// <summary>
    /// We read the spot index and its date to convert the book result to USD.
    /// </summary>
    Task<(double Prix, DateTime Horodatage)> ObtenirIndiceAsync(string devise, CancellationToken ct = default);
    /// <summary>
    /// We are getting the latest official delivery prices to adjust the fallen legs.
    /// </summary>
    Task<IReadOnlyList<PrixReglement>> ObtenirPrixReglementAsync(string devise, int nombre = 10, CancellationToken ct = default);
    /// <summary>
    /// Follow-up is required on instruments useful to the pricer or open positions.
    /// </summary>
    Task SuivreAsync(IEnumerable<string> instruments, CancellationToken ct = default);
    /// <summary>
    /// Ratings are displayed as they arrive, until the reading is cancelled.
    /// </summary>
    IAsyncEnumerable<Cotation> FluxCotationsAsync(CancellationToken ct);
    bool FluxTermine { get; }
}
