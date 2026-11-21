using System.Globalization;
using System.Text.Json;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Infrastructure.Deribit;

/// <summary>
/// The JSON Deribit is transformed into objects of the domain with System.Text.Json, keeping
/// the fields actually present in the answers.
/// </summary>
public static class AnalyseurDeribit
{
    /// <summary>
    /// Deribit Unix time stamping is converted to UTC in milliseconds.
    /// </summary>
    /// <param name="ms">On compte les millisecondes écoulées depuis le 1er janvier 1970 UTC.</param>
    public static DateTime DepuisMillisecondes(long ms) => DateTime.UnixEpoch.AddMilliseconds(ms);

    /// <summary>
    /// The contractual characteristics of an option are read without confusing them with its
    /// current rating.
    /// </summary>
    public static Instrument Option(JsonElement e) => new()
    {
        Nom = e.GetProperty("instrument_name").GetString()!,
        Devise = e.GetProperty("base_currency").GetString()!,
        Strike = e.GetProperty("strike").GetDouble(),
        Echeance = DepuisMillisecondes(e.GetProperty("expiration_timestamp").GetInt64()),
        Type = e.GetProperty("option_type").GetString() == "call" ? TypeOption.Call : TypeOption.Put,
        TailleContrat = e.GetProperty("contract_size").GetDouble(),
        QuantiteMinimale = e.GetProperty("min_trade_amount").GetDouble(),
        PasCotation = e.GetProperty("tick_size").GetDouble(),
        Actif = !e.TryGetProperty("is_active", out var actif) || actif.ValueKind != JsonValueKind.False,
        Etat = e.TryGetProperty("state", out var etat) ? etat.GetString() ?? "open" : "open"
    };

    /// <summary>
    /// The sheet of a future is constructed and the perpetual contract is distinguished, which
    /// does not have a date of expiry.
    /// </summary>
    public static InstrumentFuture Future(JsonElement e)
    {
        bool perpetuel = e.TryGetProperty("settlement_period", out var p) && p.GetString() == "perpetual";
        return new InstrumentFuture
        {
            Nom = e.GetProperty("instrument_name").GetString()!,
            Devise = e.GetProperty("base_currency").GetString()!,
            Echeance = perpetuel ? DateTime.MaxValue : DepuisMillisecondes(e.GetProperty("expiration_timestamp").GetInt64()),
            TailleContratUsd = e.GetProperty("contract_size").GetDouble(),
            QuantiteMinimaleUsd = e.GetProperty("min_trade_amount").GetDouble()
        };
    }

    /// <summary>
    /// A REST ticker and a message received on a ticker channel are read in the same way, as
    /// they describe the same market information.
    /// </summary>
    public static Cotation Ticker(JsonElement e)
    {
        Grecques? grecques = null;
        if (e.TryGetProperty("greeks", out var g) && g.ValueKind == JsonValueKind.Object &&
            Nombre(g, "delta") is double delta && Nombre(g, "gamma") is double gamma &&
            Nombre(g, "vega") is double vega && Nombre(g, "theta") is double theta)
            grecques = new Grecques(delta, gamma, vega, theta);

        return new Cotation
        {
            NomInstrument = e.GetProperty("instrument_name").GetString()!,
            Horodatage = DepuisMillisecondes(e.GetProperty("timestamp").GetInt64()),
            Bid = PrixStrictementPositif(e, "best_bid_price", "best_bid_amount"),
            Ask = PrixStrictementPositif(e, "best_ask_price", "best_ask_amount"),
            Mark = Nombre(e, "mark_price"),
            BidIv = NombrePositif(e, "bid_iv"),
            AskIv = NombrePositif(e, "ask_iv"),
            MarkIv = Nombre(e, "mark_iv"),
            Future = Nombre(e, "underlying_price"),
            Indice = Nombre(e, "index_price"),
            GrecquesDeribit = grecques,
            EstComplete = true,
            Etat = e.TryGetProperty("state", out var etat) ? etat.GetString() ?? "open" : "open"
        };
    }

    /// <summary>
    /// A line of the market summary is read by leaving the Greeks and the IV bid/asks missing
    /// that this answer does not provide.
    /// </summary>
    public static Cotation Resume(JsonElement e, DateTime horodatageServeur) => new()
    {
        NomInstrument = e.GetProperty("instrument_name").GetString()!,
        Horodatage = e.TryGetProperty("creation_timestamp", out var ts) ? DepuisMillisecondes(ts.GetInt64()) : horodatageServeur,
        Bid = NombrePositif(e, "bid_price"),
        Ask = NombrePositif(e, "ask_price"),
        Mark = Nombre(e, "mark_price"),
        MarkIv = Nombre(e, "mark_iv"),
        Future = Nombre(e, "underlying_price"),
        Indice = Nombre(e, "estimated_delivery_price"),
        EstComplete = false
    };

    /// <summary>
    /// A payment date is associated with the official delivery price published for this day.
    /// </summary>
    public static PrixReglement Reglement(JsonElement e) => new(
        DateOnly.ParseExact(e.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        e.GetProperty("delivery_price").GetDouble());

    /// <summary>
    /// Only a finished digital JSON field is accepted; an absent, invalid or invalid field
    /// remains unavailable.
    /// </summary>
    private static double? Nombre(JsonElement e, string propriete) =>
        e.TryGetProperty(propriete, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetDouble(out double n) && double.IsFinite(n) ? n : null;

    /// <summary>
    /// The null or negative values are discarded when the field must be a strictly positive
    /// value.
    /// </summary>
    private static double? NombrePositif(JsonElement e, string propriete) => Nombre(e, propriete) is > 0 and var x ? x : null;

    /// <summary>
    /// An absent value is retained when Deribit does not publish any order. A null price or
    /// quantity must not become an executable price.
    /// </summary>
    private static double? PrixStrictementPositif(JsonElement e, string prix, string quantite) =>
        Nombre(e, prix) is > 0 and var p && (Nombre(e, quantite) ?? 1) > 0 ? p : null;
}
