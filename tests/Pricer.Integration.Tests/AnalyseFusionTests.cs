using System.Text.Json;
using Pricer.Infrastructure.Deribit;

namespace Pricer.Integration.Tests;

[TestClass]
public class AnalyseFusionTests
{
    /// <summary>
    /// On vérifie qu'une réponse incomplète ne crée pas de sensibilités nulles qui donneraient une
    /// fausse neutralité.
    /// </summary>
    [TestMethod]
    public void Ticker_GrecquesPartielles_ResteIndisponibleSansInventerDesZeros()
    {
        using var doc = JsonDocument.Parse("""{"instrument_name":"BTC-20NOV26-74000-C","timestamp":1794398400000,"mark_price":0.03,"greeks":{"delta":0.5}}""");
        var c = AnalyseurDeribit.Ticker(doc.RootElement);
        Assert.IsNull(c.GrecquesDeribit);
        Assert.IsNull(c.Future);
    }

    /// <summary>
    /// On vérifie que le statut fermé reçu du marché empêche de considérer la quote comme négociable.
    /// </summary>
    [TestMethod]
    public void Ticker_MarcheFerme_ConserveSonEtat()
    {
        using var doc = JsonDocument.Parse("""{"instrument_name":"BTC-20NOV26-74000-C","timestamp":1794398400000,"state":"closed"}""");
        Assert.AreEqual("closed", AnalyseurDeribit.Ticker(doc.RootElement).Etat);
    }
}
