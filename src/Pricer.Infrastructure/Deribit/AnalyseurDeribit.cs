using System.Globalization;
using System.Text.Json;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Infrastructure.Deribit;

/// <summary>
/// On transforme le JSON Deribit en objets du domaine avec System.Text.Json, en conservant les champs
/// réellement présents dans les réponses.
/// </summary>
public static class AnalyseurDeribit
{
    /// <summary>
    /// On convertit l’horodatage Unix de Deribit, exprimé en millisecondes, en date UTC.
    /// </summary>
    /// <param name="ms">On compte les millisecondes écoulées depuis le 1er janvier 1970 UTC.</param>
    public static DateTime DepuisMillisecondes(long ms) => DateTime.UnixEpoch.AddMilliseconds(ms);

    /// <summary>
    /// On lit les caractéristiques contractuelles d’une option sans les confondre avec sa cotation
    /// courante.
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
    /// On construit la fiche d’un future et on distingue le contrat perpétuel, qui ne possède pas
    /// d’échéance datée.
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
    /// On lit de la même façon un ticker REST et un message reçu sur un canal ticker, car ils décrivent
    /// les mêmes informations de marché.
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
    /// On lit une ligne du résumé de marché en laissant absentes les grecques et les IV bid/ask que cette
    /// réponse ne fournit pas.
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
    /// On associe une date de règlement au prix officiel de livraison publié pour cette journée.
    /// </summary>
    public static PrixReglement Reglement(JsonElement e) => new(
        DateOnly.ParseExact(e.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        e.GetProperty("delivery_price").GetDouble());

    /// <summary>
    /// On accepte uniquement un champ JSON numérique fini ; un champ absent, nul ou invalide reste
    /// indisponible.
    /// </summary>
    private static double? Nombre(JsonElement e, string propriete) =>
        e.TryGetProperty(propriete, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetDouble(out double n) && double.IsFinite(n) ? n : null;

    /// <summary>
    /// On écarte les valeurs nulles ou négatives lorsque le champ doit représenter une valeur
    /// strictement positive.
    /// </summary>
    private static double? NombrePositif(JsonElement e, string propriete) => Nombre(e, propriete) is > 0 and var x ? x : null;

    /// <summary>
    /// On conserve une valeur absente lorsque Deribit ne publie aucun ordre. Un prix ou une quantité nuls
    /// ne doivent pas devenir un prix exécutable.
    /// </summary>
    private static double? PrixStrictementPositif(JsonElement e, string prix, string quantite) =>
        Nombre(e, prix) is > 0 and var p && (Nombre(e, quantite) ?? 1) > 0 ? p : null;
}
