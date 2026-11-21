using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Tests.Outils;

namespace Pricer.Domaine.Tests.Structures;

[TestClass]
public class PricerStructureTests
{
    /// <summary>
    /// On vérifie qu'un marché synthétique cohérent ne déclenche pas d'alerte de prix inutile.
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
    /// On vérifie qu'un écart au mark supérieur au seuil devient visible dans la cotation.
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
    /// On vérifie la cohérence économique des deux côtés exécutables de la structure.
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
    /// On vérifie chaque flux d'une structure achetée pour détecter une inversion de bid et d'ask.
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
    /// On vérifie que l'absence d'un côté nécessaire empêche le traitement de toute la structure.
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
    /// On vérifie que la vente inverse les expositions tout en réutilisant les mêmes paramètres de
    /// marché.
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
    /// On vérifie l'agrégation des grecques sur un jeu de données où la comparaison est connue.
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
    /// On vérifie qu'un OTC conserve un prix de modèle sans lui attribuer artificiellement une cotation
    /// Deribit.
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
