using System.Runtime.CompilerServices;
using Pricer.Domaine.Abstractions;
using Pricer.Domaine.Modeles;
using Pricer.Infrastructure.Transport;

namespace Pricer.Infrastructure.Deribit;

/// <summary>
/// On traduit les réponses publiques Deribit en objets métier sans imposer un transport précis. On utilise
/// donc le même analyseur pour le direct, l'enregistrement et le rejeu.
/// </summary>
/// <param name="transport">On fournit le transport qui échange les messages JSON de la session.</param>
/// <param name="intervalleTicker">On choisit l’intervalle ajouté aux noms des canaux d’abonnement.</param>
public sealed class SourceMarcheDeribit(ITransportDeribit transport, string intervalleTicker = "100ms") : ISourceMarche
{
    public ITransportDeribit Transport { get; } = transport;
    public bool FluxTermine => Transport.FluxTermine;

    /// <summary>
    /// On demande les options non échues de la devise et on transforme leurs fiches JSON en instruments
    /// métier.
    /// </summary>
    public async Task<IReadOnlyList<Instrument>> ChargerOptionsAsync(string devise, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/get_instruments", Params(("currency", devise), ("kind", "option"), ("expired", "false")), ct).ConfigureAwait(false);
        return r.Resultat.EnumerateArray().Select(AnalyseurDeribit.Option).ToList();
    }

    /// <summary>
    /// On charge les fiches des futures non échus, dont les contrats perpétuels, pour construire le
    /// référentiel de couverture.
    /// </summary>
    public async Task<IReadOnlyList<InstrumentFuture>> ChargerFuturesAsync(string devise, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/get_instruments", Params(("currency", devise), ("kind", "future"), ("expired", "false")), ct).ConfigureAwait(false);
        return r.Resultat.EnumerateArray().Select(AnalyseurDeribit.Future).ToList();
    }

    /// <summary>
    /// On charge un premier ensemble de cotations d’options ou de futures et on conserve l’horodatage de
    /// la réponse.
    /// </summary>
    /// <param name="type">On utilise option ou future selon le résumé demandé.</param>
    public async Task<IReadOnlyList<Cotation>> ChargerResumeAsync(string devise, string type, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/get_book_summary_by_currency", Params(("currency", devise), ("kind", type)), ct).ConfigureAwait(false);
        return r.Resultat.EnumerateArray().Select(e => AnalyseurDeribit.Resume(e, r.HorodatageServeur)).ToList();
    }

    /// <summary>
    /// On récupère le ticker détaillé d’un instrument pour disposer de ses prix, de ses volatilités et
    /// de ses grecques publiées.
    /// </summary>
    public async Task<Cotation> ObtenirTickerAsync(string instrument, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/ticker", Params(("instrument_name", instrument)), ct).ConfigureAwait(false);
        return AnalyseurDeribit.Ticker(r.Resultat);
    }

    /// <summary>
    /// On lit l’indice USD de la crypto et on conserve son heure pour synchroniser le marché.
    /// </summary>
    /// <returns>On renvoie le prix de l’indice en USD et l’horodatage serveur.</returns>
    public async Task<(double Prix, DateTime Horodatage)> ObtenirIndiceAsync(string devise, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/get_index_price", Params(("index_name", $"{devise.ToLowerInvariant()}_usd")), ct).ConfigureAwait(false);
        return (r.Resultat.GetProperty("index_price").GetDouble(), r.HorodatageServeur);
    }

    /// <summary>
    /// On demande les derniers prix de livraison de l’indice pour régler les jambes arrivées à échéance.
    /// </summary>
    /// <param name="nombre">On limite la réponse au nombre de journées demandé.</param>
    public async Task<IReadOnlyList<PrixReglement>> ObtenirPrixReglementAsync(string devise, int nombre = 10, CancellationToken ct = default)
    {
        var r = await Transport.AppelerAsync("public/get_delivery_prices",
            Params(("index_name", $"{devise.ToLowerInvariant()}_usd"), ("count", nombre.ToString(System.Globalization.CultureInfo.InvariantCulture))), ct).ConfigureAwait(false);
        return r.Resultat.GetProperty("data").EnumerateArray().Select(AnalyseurDeribit.Reglement).ToList();
    }

    /// <summary>
    /// On traduit les noms d’instruments en canaux ticker en gardant l’intervalle de mise à jour
    /// configuré.
    /// </summary>
    public Task SuivreAsync(IEnumerable<string> instruments, CancellationToken ct = default) =>
        Transport.SouscrireAsync(instruments.Select(i => $"ticker.{i}.{intervalleTicker}"), ct);

    /// <summary>
    /// On transforme uniquement les messages des canaux ticker en cotations métier transmises au fil de
    /// leur arrivée.
    /// </summary>
    public async IAsyncEnumerable<Cotation> FluxCotationsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var m in Transport.LireFluxAsync(ct).ConfigureAwait(false))
            if (m.Canal.StartsWith("ticker.", StringComparison.Ordinal))
                yield return AnalyseurDeribit.Ticker(m.Donnees);
    }

    /// <summary>
    /// On regroupe les paires de paramètres dans un dictionnaire avant de les transmettre au transport.
    /// </summary>
    private static Dictionary<string, string> Params(params (string Cle, string Valeur)[] p) => p.ToDictionary(x => x.Cle, x => x.Valeur);

    /// <summary>
    /// On libère le transport sous-jacent lorsque la source de marché n’est plus utilisée.
    /// </summary>
    public ValueTask DisposeAsync() => Transport.DisposeAsync();
}
