using System.Text.Json;
using Pricer.Domaine.Modeles;
using Pricer.Infrastructure.Deribit;

namespace Pricer.Integration.Tests.Infrastructure;

[TestClass]
public class AnalyseurDeribitTests
{
    // On conserve une réponse ticker avec un bid inexploitable et une bid_iv nulle pour vérifier leur lecture.
    private const string Ticker = """
    {"timestamp":1794413218961,"state":"open","greeks":{"delta":-0.98855,"gamma":1e-05,"vega":1.8925,"theta":-14.44349,"rho":-5.77311},
     "index_price":73854.89,"instrument_name":"BTC-14NOV26-80000-P","mark_price":0.0829,"best_ask_price":0.0845,"best_bid_price":0.081,
     "mark_iv":40.63,"bid_iv":0.0,"ask_iv":62.94,"underlying_price":73883.6,"best_ask_amount":7.5,"best_bid_amount":7.5}
    """;

    private const string Instrument = """
    {"instrument_name":"BTC-12NOV26-66000-C","base_currency":"BTC","strike":66000.0,"expiration_timestamp":1794470400000,
     "option_type":"call","contract_size":1.0,"min_trade_amount":0.1,"tick_size":0.0001,"settlement_period":"day"}
    """;

    /// <summary>
    /// On vérifie les conversions de prix, de volatilité, d'heure et de grecques à la lecture d'un
    /// ticker.
    /// </summary>
    [TestMethod]
    public void Ticker_ChampsDeribit_ConvertisDansLaCotation()
    {
        var c = AnalyseurDeribit.Ticker(JsonDocument.Parse(Ticker).RootElement);

        Assert.AreEqual("BTC-14NOV26-80000-P", c.NomInstrument);
        Assert.AreEqual(0.081, c.Bid);
        Assert.AreEqual(0.0845, c.Ask);
        Assert.AreEqual(0.0829, c.Mark);
        Assert.AreEqual(40.63, c.MarkIv);
        Assert.IsNull(c.BidIv, "Deribit publie 0 : traité comme absent");
        Assert.AreEqual(73883.6, c.Future);
        Assert.AreEqual(-0.98855, c.GrecquesDeribit!.Value.Delta);
        Assert.AreEqual(new DateTime(2026, 11, 11, 16, 6, 58, 961, DateTimeKind.Utc), c.Horodatage);
    }

    /// <summary>
    /// On vérifie qu'un bid nul sans liquidité n'est pas conservé comme une offre acheteuse valide.
    /// </summary>
    [TestMethod]
    public void Ticker_BidAZeroSansQuantite_BidAbsent()
    {
        string sansBid = Ticker.Replace("\"best_bid_price\":0.081", "\"best_bid_price\":0.0").Replace("\"best_bid_amount\":7.5", "\"best_bid_amount\":0.0");
        Assert.IsNull(AnalyseurDeribit.Ticker(JsonDocument.Parse(sansBid).RootElement).Bid);
    }

    /// <summary>
    /// On vérifie que les caractéristiques de négociation et d'échéance du contrat sont chargées
    /// ensemble.
    /// </summary>
    [TestMethod]
    public void Option_ReferentielDeribit_InstrumentComplet()
    {
        var i = AnalyseurDeribit.Option(JsonDocument.Parse(Instrument).RootElement);

        Assert.AreEqual(66000, i.Strike);
        Assert.AreEqual(TypeOption.Call, i.Type);
        Assert.AreEqual(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc), i.Echeance);
        Assert.AreEqual(0.1, i.PasQuantite);
    }

    /// <summary>
    /// On vérifie la lecture de la date et du prix utilisés ensuite pour les règlements du book.
    /// </summary>
    [TestMethod]
    public void Reglement_DateEtPrix()
    {
        var r = AnalyseurDeribit.Reglement(JsonDocument.Parse("""{"date":"2026-11-11","delivery_price":73833.43}""").RootElement);
        Assert.AreEqual(new DateOnly(2026, 11, 11), r.Date);
        Assert.AreEqual(73833.43, r.Prix);
    }
}
