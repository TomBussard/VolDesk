using System.Runtime.CompilerServices;
using Pricer.Domaine.Abstractions;
using Pricer.Domaine.Modeles;
using Pricer.Infrastructure.Transport;

namespace Pricer.Infrastructure.Deribit;

/// <summary>
/// Deribit's public responses are translated into business objects without requiring specific
/// transport, so the same analyser is used for direct, recording and replay.
/// </summary>
/// <param name="transport">On fournit le transport qui échange les messages JSON de la session.</param>
/// <param name="intervalleTicker">On choisit l’intervalle ajouté aux noms des canaux d’abonnement.</param>
public sealed class SourceMarcheDeribit(ITransportDeribit transport, string intervalleTicker = "100ms") : ISourceMarche
{
    public ITransportDeribit Transport { get; } = transport;
    public bool FluxTermine => Transport.FluxTermine;

    /// <summary>
    /// We ask for the unmatured options of the currency and we turn their JSON cards into
    /// business instruments.
    /// </summary>
    public async Task<IReadOnlyList<Instrument>> ChargerOptionsAsync(string devise, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/get_instruments", Params(("currency", devise), ("kind", "option"), ("expired", "false")), ct).ConfigureAwait(false);
        return r.Resultat.EnumerateArray().Select(AnalyseurDeribit.Option).ToList();
    }

    /// <summary>
    /// The files of unmatured futures, including perpetual contracts, are loaded to build the
    /// hedging repository.
    /// </summary>
    public async Task<IReadOnlyList<InstrumentFuture>> ChargerFuturesAsync(string devise, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/get_instruments", Params(("currency", devise), ("kind", "future"), ("expired", "false")), ct).ConfigureAwait(false);
        return r.Resultat.EnumerateArray().Select(AnalyseurDeribit.Future).ToList();
    }

    /// <summary>
    /// A first set of options or future quotes is loaded and the response time stamp is
    /// maintained.
    /// </summary>
    /// <param name="type">On utilise option ou future selon le résumé demandé.</param>
    public async Task<IReadOnlyList<Cotation>> ChargerResumeAsync(string devise, string type, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/get_book_summary_by_currency", Params(("currency", devise), ("kind", type)), ct).ConfigureAwait(false);
        return r.Resultat.EnumerateArray().Select(e => AnalyseurDeribit.Resume(e, r.HorodatageServeur)).ToList();
    }

    /// <summary>
    /// The detailed ticker of an instrument is retrieved to dispose of its prices, volatility
    /// and published Greeks.
    /// </summary>
    public async Task<Cotation> ObtenirTickerAsync(string instrument, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/ticker", Params(("instrument_name", instrument)), ct).ConfigureAwait(false);
        return AnalyseurDeribit.Ticker(r.Resultat);
    }

    /// <summary>
    /// We read the USD index of the crypto and we keep his hour to synchronize the market.
    /// </summary>
    /// <returns>On renvoie le prix de l’indice en USD et l’horodatage serveur.</returns>
    public async Task<(double Prix, DateTime Horodatage)> ObtenirIndiceAsync(string devise, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/get_index_price", Params(("index_name", $"{devise.ToLowerInvariant()}_usd")), ct).ConfigureAwait(false);
        return (r.Resultat.GetProperty("index_price").GetDouble(), r.HorodatageServeur);
    }

    /// <summary>
    /// The latest delivery prices of the index are requested to adjust the matured legs.
    /// </summary>
    /// <param name="nombre">On limite la réponse au nombre de journées demandé.</param>
    public async Task<IReadOnlyList<PrixReglement>> ObtenirPrixReglementAsync(string devise, int nombre = 10, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/get_delivery_prices",
            Params(("index_name", $"{devise.ToLowerInvariant()}_usd"), ("count", nombre.ToString(System.Globalization.CultureInfo.InvariantCulture))), ct).ConfigureAwait(false);
        return r.Resultat.GetProperty("data").EnumerateArray().Select(AnalyseurDeribit.Reglement).ToList();
    }

    /// <summary>
    /// The instrument names are translated into ticker channels by keeping the update interval
    /// configured.
    /// </summary>
    public Task SuivreAsync(IEnumerable<string> instruments, CancellationToken ct = default) =>
        Transport.SouscrireAsync(instruments.Select(i => $"ticker.{i}.{intervalleTicker}"), ct);

    /// <summary>
    /// Only the messages of the ticker channels are transformed into business ratings
    /// transmitted as they arrive.
    /// </summary>
    public async IAsyncEnumerable<Cotation> FluxCotationsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var m in Transport.LireFluxAsync(ct).ConfigureAwait(false))
            if (m.Canal.StartsWith("ticker.", StringComparison.Ordinal))
                yield return AnalyseurDeribit.Ticker(m.Donnees);
    }

    /// <summary>
    /// Parameter pairs are grouped into a dictionary before they are transmitted to transport.
    /// </summary>
    private static Dictionary<string, string> Params(params (string Cle, string Valeur)[] p) => p.ToDictionary(x => x.Cle, x => x.Valeur);

    /// <summary>
    /// The underlying transport is released when the market source is no longer used.
    /// </summary>
    public ValueTask DisposeAsync() => Transport.DisposeAsync();
}
