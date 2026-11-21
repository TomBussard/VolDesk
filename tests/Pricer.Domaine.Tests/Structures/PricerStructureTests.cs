using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Tests.Outils;

namespace Pricer.Domaine.Tests.Structures;

[TestClass]
public class PricerStructureTests
{
    /// <summary>
    /// It is verified that a coherent synthetic market does not trigger unnecessary price
    /// alerts.
    /// </summary>
    [TestMethod]
    public void Coter_MarkCoherentAvecLaVol_AucunEcartSignale()
    {
        var etat = MarcheDeTest.Etat();
        var s = FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [72000, 80000], etat.Referentiel);

        var c = PricerStructure.Coter(s, Sens.Achat, 1, etat);

        Assert.IsTrue(c.Lignes.All(l => !l.EcartMarkSignale));
        Assert.IsTrue(c.Lignes.All(l => Math.Abs(l.VolMark!.EcartPoints!.Value) < 1e-6));
        Assert.AreEqual(c.MarkUnitaireBtc!.Value, c.TheoriqueUnitaireBtc, 1e-10);
    }

    /// <summary>
    /// It is verified that a mark difference above the threshold becomes visible in the
    /// quote.
    /// </summary>
    [TestMethod]
    public void Coter_MarkDecaleDe0_0002Btc_EcartSignale()
    {
        var etat = MarcheDeTest.Etat();
        string nom = MarcheDeTest.Nom(MarcheDeTest.E1, 72000, TypeOption.Call);
        etat.MettreAJour(etat.Cotation(nom)! with { Mark = etat.Cotation(nom)!.Mark + 0.0002 });
        var s = FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [72000, 80000], etat.Referentiel);

        var c = PricerStructure.Coter(s, Sens.Achat, 1, etat);

        Assert.IsTrue(c.Lignes[0].EcartMarkSignale);
        Assert.IsFalse(c.Lignes[1].EcartMarkSignale);
        Assert.IsTrue(c.Lignes[0].VolMark!.Signale, "La vol du mark recalculée diffère alors de mark_iv.");
    }

    /// <summary>
    /// The economic coherence of the two executable sides of the structure is verified.
    /// </summary>
    [TestMethod]
    public void Coter_PrixExecutables_AchatAuDessusDeLaVenteEtFourchettePositive()
    {
        var etat = MarcheDeTest.Etat();
        var s = FabriqueStructures.Creer(TypeStructure.ButterflyCall, MarcheDeTest.E2, [70000, 74000, 78000], etat.Referentiel);

        var c = PricerStructure.Coter(s, Sens.Achat, 2, etat);

        Assert.IsTrue(c.Traitable);
        Assert.IsTrue(c.PrixExecutableAchatBtc > c.TheoriqueUnitaireBtc);
        Assert.IsTrue(c.PrixExecutableVenteBtc < c.TheoriqueUnitaireBtc);
        Assert.AreEqual(c.PrixExecutableAchatBtc!.Value - c.PrixExecutableVenteBtc!.Value, c.FourchetteBtc!.Value, 1e-15);
        Assert.AreEqual(2 * c.PrixExecutableAchatBtc!.Value, c.PrimeTotaleBtc!.Value, 1e-12);
    }

    /// <summary>
    /// Each stream of a purchased structure is checked to detect an inversion of bid and ask.
    /// </summary>
    [TestMethod]
    public void Coter_PrixExecutableAchat_PaieAskDesJambesAcheteesEtRecoitBidDesVendues()
    {
        var etat = MarcheDeTest.Etat();
        var s = FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [72000, 80000], etat.Referentiel);
        var c = PricerStructure.Coter(s, Sens.Achat, 1, etat);

        double attendu = c.Lignes[0].Cotation!.Ask!.Value - c.Lignes[1].Cotation!.Bid!.Value;
        Assert.AreEqual(attendu, c.PrixExecutableAchatBtc!.Value, 1e-15);
    }

    /// <summary>
    /// It is verified that the absence of a necessary side prevents treatment of the entire
    /// structure.
    /// </summary>
    [TestMethod]
    public void Coter_JambeSansBid_StructureNonTraitable()
    {
        var etat = MarcheDeTest.Etat();
        string nom = MarcheDeTest.Nom(MarcheDeTest.E1, 80000, TypeOption.Call);
        etat.MettreAJour(etat.Cotation(nom)! with { Bid = null });
        var s = FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [72000, 80000], etat.Referentiel);

        var c = PricerStructure.Coter(s, Sens.Vente, 1, etat);

        Assert.IsFalse(c.Traitable);
        Assert.IsNull(c.PrixExecutableBtc);
        Assert.AreEqual(StatutVol.PrixAbsent, c.Lignes[1].VolBid!.Calculee.Statut);
    }

    /// <summary>
    /// It is verified that the sale reverses the exposures while reusing the same market
    /// parameters.
    /// </summary>
    [TestMethod]
    public void Coter_Vente_GrecquesOpposeesALAchat()
    {
        var etat = MarcheDeTest.Etat();
        var s = FabriqueStructures.Creer(TypeStructure.PutSpread, MarcheDeTest.E2, [66000, 72000], etat.Referentiel);

        var achat = PricerStructure.Coter(s, Sens.Achat, 1, etat).Grecques;
        var vente = PricerStructure.Coter(s, Sens.Vente, 1, etat).Grecques;

        Assert.AreEqual(-achat.Delta, vente.Delta, 1e-12);
        Assert.AreEqual(-achat.Vega, vente.Vega, 1e-12);
    }

    /// <summary>
    /// Aggregation of Greeks is verified on a dataset where the comparison is known.
    /// </summary>
    [TestMethod]
    public void Coter_GrecquesDeLaStructure_EgalesAuxGrecquesDeribitSurMarcheSynthetique()
    {
        var etat = MarcheDeTest.Etat();
        var s = FabriqueStructures.Creer(TypeStructure.Strangle, MarcheDeTest.E2, [68000, 80000], etat.Referentiel);
        var c = PricerStructure.Coter(s, Sens.Achat, 1.5, etat);

        Assert.AreEqual(c.GrecquesDeribit!.Value.Delta, c.Grecques.Delta, 1e-12);
        Assert.AreEqual(c.GrecquesDeribit!.Value.Theta, c.Grecques.Theta, 1e-9);
    }

    /// <summary>
    /// It is verified that an OTC retains a model price without artificially assigning a
    /// Deribit rating.
    /// </summary>
    [TestMethod]
    public void Coter_OptionSurMesure_PrixTheoriqueSansMarkNiExecutable()
    {
        var etat = MarcheDeTest.Etat();
        var s = FabriqueStructures.CreerSurMesure(TypeOption.Call, new DateTime(2026, 12, 4, 8, 0, 0, DateTimeKind.Utc), 77000, etat);

        var c = PricerStructure.Coter(s, Sens.Achat, 1, etat);

        Assert.IsTrue(c.TheoriqueUnitaireBtc > 0);
        Assert.IsNull(c.MarkUnitaireBtc);
        Assert.IsFalse(c.Traitable);
    }
}
