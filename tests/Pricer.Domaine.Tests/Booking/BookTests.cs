using Pricer.Domaine.Booking;
using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Tests.Outils;

namespace Pricer.Domaine.Tests.Booking;

[TestClass]
public class BookTests
{
    /// <summary>
    /// We open a spread on the test market and return the necessary objects to the book
    /// assertions.
    /// </summary>
    private static (Blotter, Pricer.Domaine.Booking.Book, Marche.EtatMarche, Trade) BookerCallSpread(double quantite = 2)
    {
        var etat = MarcheDeTest.Etat();
        var blotter = new Blotter();
        var c = PricerStructure.Coter(FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [72000, 80000], etat.Referentiel), Sens.Achat, quantite, etat);
        return (blotter, new Pricer.Domaine.Booking.Book(blotter), etat, blotter.Booker(c));
    }

    /// <summary>
    /// It is checked that the history retains the actually used Bid/ask sides for each leg.
    /// </summary>
    [TestMethod]
    public void Booker_EnregistreLesJambesAuxPrixExecutables()
    {
        var (_, _, etat, t) = BookerCallSpread();

        Assert.AreEqual(NatureTrade.Ouverture, t.Nature);
        Assert.AreNotEqual(Guid.Empty, t.Id);
        Assert.AreEqual(2, t.Jambes[0].QuantiteSignee);
        Assert.AreEqual(-2, t.Jambes[1].QuantiteSignee);
        Assert.AreEqual(etat.Cotation(t.Jambes[0].Instrument)!.Ask, t.Jambes[0].PrixExecution);
        Assert.AreEqual(etat.Cotation(t.Jambes[1].Instrument)!.Bid, t.Jambes[1].PrixExecution);
        Assert.IsTrue(t.PrimeBtc > 0);
        Assert.AreEqual(MarcheDeTest.Futures[MarcheDeTest.E1], t.Jambes[0].Future);
    }

    /// <summary>
    /// It is verified that an incomplete indicative rating cannot become a registered
    /// execution.
    /// </summary>
    [TestMethod]
    public void Booker_StructureNonTraitable_Refusee()
    {
        var etat = MarcheDeTest.Etat();
        string nom = MarcheDeTest.Nom(MarcheDeTest.E1, 80000, TypeOption.Call);
        etat.MettreAJour(etat.Cotation(nom)! with { Bid = null });
        var c = PricerStructure.Coter(FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [72000, 80000], etat.Referentiel), Sens.Achat, 1, etat);

        Assert.ThrowsException<PricerException>(() => new Blotter().Booker(c));
    }

    /// <summary>
    /// It is verified that the initial valuation takes into account the premium paid, i.e. the
    /// range cost.
    /// </summary>
    [TestMethod]
    public void Evaluer_JusteApresLeTrade_PnlLatentEgalAuMarkMoinsPrime()
    {
        var (_, book, etat, t) = BookerCallSpread();

        var e = book.Evaluer(etat);
        var p = e.Positions.Single();

        double valeurMark = t.Jambes.Sum(j => j.QuantiteSignee * etat.Cotation(j.Instrument)!.Mark!.Value);
        Assert.AreEqual(valeurMark, p.ValeurMarkBtc, 1e-12);
        Assert.AreEqual(valeurMark - t.PrimeBtc, p.PnlLatentBtc, 1e-12);
        Assert.IsTrue(p.PnlLatentBtc < 0, "acheter à l'ask coûte la demi-fourchette");
        Assert.AreEqual(e.PnlTotalBtc * MarcheDeTest.Indice, e.PnlTotalUsd, 1e-9);
    }

    /// <summary>
    /// It is verified that a partial closure correctly allocates the premium between the
    /// balance remaining and the result achieved.
    /// </summary>
    [TestMethod]
    public void Deboucler_Moitie_QuantiteOuverteEtPnlRealise()
    {
        var (blotter, book, etat, t) = BookerCallSpread(2);

        var d = blotter.Deboucler(t.Id, 1, etat);

        Assert.AreEqual(t.Id, d.IdTradeOrigine);
        Assert.AreEqual(NatureTrade.Debouclage, d.Nature);
        Assert.AreEqual(1, blotter.QuantiteOuverte(t.Id), 1e-12);
        Assert.AreEqual(-1, d.Jambes[0].QuantiteSignee, 1e-12);
        var p = book.Evaluer(etat).Positions.Single();
        // We find the cost of fork on the debunked half, without movement of the market.
        Assert.AreEqual(-d.PrimeBtc - 0.5 * t.PrimeBtc, p.PnlRealiseBtc, 1e-12);
        Assert.IsTrue(p.PnlRealiseBtc < 0);
    }

    /// <summary>
    /// It is verified that a fence cannot create an opposite position by exceeding the
    /// available quantity.
    /// </summary>
    [TestMethod]
    public void Deboucler_PlusQueLaQuantiteOuverte_Refuse()
    {
        var (blotter, _, etat, t) = BookerCallSpread(1);
        blotter.Deboucler(t.Id, 0.6, etat);

        var ex = Assert.ThrowsException<SaisieInvalideException>(() => blotter.Deboucler(t.Id, 0.5, etat));
        Assert.AreEqual(CodeErreurSaisie.QuantiteDebouclageExcessive, ex.CodeErreur);
    }

    /// <summary>
    /// It is verified that a full de-coupling does not retain an open position for the trade.
    /// </summary>
    [TestMethod]
    public void Deboucler_Total_PositionSortDuBook()
    {
        var (blotter, book, etat, t) = BookerCallSpread(1);
        blotter.Deboucler(t.Id, 1, etat);

        var e = book.Evaluer(etat);
        Assert.AreEqual(0, e.Ouvertes.Count());
        Assert.AreEqual(0, e.GrecquesTotales.Delta, 1e-15);
        Assert.AreEqual(e.PnlRealiseBtc, e.PnlTotalBtc, 1e-15);
    }

    /// <summary>
    /// It is verified that the settlement uses the official payoff and transfers it to the
    /// realized result.
    /// </summary>
    [TestMethod]
    public void ReglerEcheances_OptionEchue_PayoffSurPrixDeReglementDansLeRealise()
    {
        var (blotter, book, _, t) = BookerCallSpread(1);
        var apres = MarcheDeTest.E1.AddHours(1);
        var prix = new Dictionary<DateOnly, double> { [DateOnly.FromDateTime(MarcheDeTest.E1)] = 76000 };

        var reglements = book.ReglerEcheances(apres, prix);

        Assert.AreEqual(1, reglements.Count);
        Assert.AreEqual(0, blotter.QuantiteOuverte(t.Id));
        var etatApres = MarcheDeTest.Etat(e => e.MettreAJour(e.Cotation(MarcheDeTest.Nom(MarcheDeTest.E2, 70000, TypeOption.Call))! with { Horodatage = apres }));
        var p = book.Evaluer(etatApres).Positions.Single();
        double payoffBtc = (76000 - 72000) / 76000.0;
        Assert.AreEqual(payoffBtc - t.PrimeBtc, p.PnlRealiseBtc, 1e-12);
        Assert.IsFalse(p.EstOuverte);
    }

    /// <summary>
    /// It is verified that the sensitivities of different maturities remain separate before
    /// total aggregation.
    /// </summary>
    [TestMethod]
    public void Evaluer_DeuxEcheances_GrecquesGroupeesParEcheance()
    {
        var etat = MarcheDeTest.Etat();
        var blotter = new Blotter();
        blotter.Booker(PricerStructure.Coter(FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [72000, 80000], etat.Referentiel), Sens.Achat, 1, etat));
        blotter.Booker(PricerStructure.Coter(FabriqueStructures.Creer(TypeStructure.Straddle, MarcheDeTest.E2, [74000], etat.Referentiel), Sens.Vente, 3, etat));
        blotter.Booker(PricerStructure.Coter(FabriqueStructures.Creer(TypeStructure.PutSpread, MarcheDeTest.E1, [66000, 70000], etat.Referentiel), Sens.Achat, 1, etat));

        var e = new Pricer.Domaine.Booking.Book(blotter).Evaluer(etat);

        Assert.AreEqual(2, e.GrecquesParEcheance.Count);
        Assert.AreEqual(e.Positions.Sum(p => p.Grecques.Vega), e.GrecquesTotales.Vega, 1e-9);
        Assert.IsTrue(e.GrecquesParEcheance[MarcheDeTest.E2].Vega < 0, "straddle vendu : vega négatif");
    }

    /// <summary>
    /// It is verified that the CTA and its registered covers give the expected residues in the
    /// book.
    /// </summary>
    [TestMethod]
    public void BookerSurMesure_AvecCouverture_GrecquesDuBookQuasiNeutres()
    {
        var etat = MarcheDeTest.Etat();
        var blotter = new Blotter();
        var option = new JambeSurMesure(TypeOption.Call, 77000, new DateTime(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc), 1);
        var p = MoteurCouverture.CalculerMeilleure(option, Sens.Achat, 20, etat);

        var t = blotter.BookerSurMesure(p, etat.Maintenant);
        var e = new Pricer.Domaine.Booking.Book(blotter).Evaluer(etat);

        Assert.AreEqual(TypeStructure.OptionSurMesure, t.TypeStructure);
        Assert.AreEqual(p.GrecquesGlobales.Vega, e.GrecquesTotales.Vega, 1e-6);
        Assert.IsTrue(Math.Abs(e.GrecquesTotales.Vega) < Math.Abs(p.GrecquesOption.Vega) * 0.02);
        Assert.IsTrue(e.Positions.Single().PnlLatentBtc > 0, "le client paie théorique + coût + marge");
    }

    /// <summary>
    /// It is verified that several positions on the same instrument do not create duplicate
    /// subscriptions.
    /// </summary>
    [TestMethod]
    public void InstrumentsSuivis_SansDoublons()
    {
        var etat = MarcheDeTest.Etat();
        var blotter = new Blotter();
        var s = FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [72000, 80000], etat.Referentiel);
        blotter.Booker(PricerStructure.Coter(s, Sens.Achat, 1, etat));
        blotter.Booker(PricerStructure.Coter(s, Sens.Vente, 1, etat));

        Assert.AreEqual(2, new Pricer.Domaine.Booking.Book(blotter).InstrumentsSuivis().Count);
    }
}
