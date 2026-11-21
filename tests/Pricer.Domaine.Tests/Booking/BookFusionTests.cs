using Pricer.Domaine.Booking;
using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Tests.Outils;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Tests.Booking;

[TestClass]
public class BookFusionTests
{
    private static readonly DateTime Otc = MarcheDeTest.E1.AddDays(7);
    /// <summary>
    /// On prépare un OTC et ses couvertures à plusieurs dates pour vérifier le suivi indépendant des
    /// jambes.
    /// </summary>
    private static (Blotter Blotter, Book Book, Trade Trade) PositionOtc()
    {
        var t = new Trade
        {
            Id = Guid.NewGuid(),
            Horodatage = MarcheDeTest.Maintenant,
            Nature = NatureTrade.Ouverture,
            TypeStructure = TypeStructure.OptionSurMesure,
            Sens = Sens.Vente,
            Quantite = 10,
            Description = "OTC et couvertures à plusieurs échéances",
            Echeance = Otc,
            Jambes =
            [
                new() { Instrument = "OTC-test", Nature = NatureJambe.OptionSurMesure, Type = TypeOption.Call,
                    Strike = 77000, Echeance = Otc, QuantiteSignee = -10, PrixExecution = 0.02, Future = 74200, Vol = 0.45 },
                new() { Instrument = MarcheDeTest.Nom(MarcheDeTest.E1, 76000, TypeOption.Call), Nature = NatureJambe.OptionCotee,
                    Type = TypeOption.Call, Strike = 76000, Echeance = MarcheDeTest.E1, QuantiteSignee = 3.3,
                    PrixExecution = 0.015, Future = 74000, Vol = 0.45, Couverture = true },
                new() { Instrument = MarcheDeTest.Nom(MarcheDeTest.E2, 80000, TypeOption.Call), Nature = NatureJambe.OptionCotee,
                    Type = TypeOption.Call, Strike = 80000, Echeance = MarcheDeTest.E2, QuantiteSignee = 5.1,
                    PrixExecution = 0.018, Future = 74500, Vol = 0.45, Couverture = true },
                new() { Instrument = "BTC-25DEC26", Nature = NatureJambe.Future, Echeance = MarcheDeTest.E2,
                    QuantiteSignee = -30000.0 / 74500, PrixExecution = 74500, Future = 74500,
                    NotionnelUsd = -30000, Couverture = true, PasNotionnelUsd = 10 }
            ]
        };
        var b = new Blotter(); b.Charger([t]); return (b, new Book(b), t);
    }

    /// <summary>
    /// On replace les cotations du marché de test à la date choisie pour contrôler la fraîcheur sans
    /// changer les prix.
    /// </summary>
    private static EtatMarche MarcheA(DateTime date)
    {
        var e = MarcheDeTest.Etat();
        foreach (string nom in e.Referentiel.Options.Select(i => i.Nom).Concat(e.Referentiel.Futures.Select(i => i.Nom)))
            e.MettreAJour(e.Cotation(nom)! with { Horodatage = date });
        return e;
    }

    /// <summary>
    /// On vérifie que chaque jambe sort à sa propre échéance et que le réalisé reste conservé après le
    /// règlement de l'OTC.
    /// </summary>
    [TestMethod]
    public void Regler_ChaqueJambeASaDate_ConserveLesCouverturesApresOtc()
    {
        var (b, book, t) = PositionOtc();
        var prix = new Dictionary<DateOnly, double>
        {
            [DateOnly.FromDateTime(MarcheDeTest.E1)] = 78000,
            [DateOnly.FromDateTime(Otc)] = 79000,
            [DateOnly.FromDateTime(MarcheDeTest.E2)] = 81000
        };
        var premier = book.ReglerEcheances(MarcheDeTest.E1, prix).Single();
        Assert.AreEqual(1, premier.Jambes.Count);
        Assert.AreEqual(t.Jambes[1].Id, premier.Jambes[0].IdJambeOrigine);
        Assert.AreEqual(10, b.QuantiteOuverte(t.Id));
        var second = book.ReglerEcheances(Otc, prix).Single();
        Assert.AreEqual(1, second.Jambes.Count);
        Assert.AreEqual(0, b.QuantiteOuverte(t.Id));
        Assert.IsTrue(b.ADesJambesOuvertes(t.Id));
        Assert.AreEqual(5.1, b.QuantiteOuverteJambe(t.Id, t.Jambes[2].Id), 1e-12);
        var p = book.Evaluer(MarcheA(Otc)).Positions.Single();
        Assert.IsTrue(p.EstOuverte);
        StringAssert.Contains(p.Statut, "Couverture résiduelle");
        Assert.AreEqual(2, p.JambesOuvertes.Count);
        double realiseAttendu = 3.3 * (2000 / 78000.0 - 0.015) - 10 * (2000 / 79000.0 - 0.02);
        Assert.AreEqual(realiseAttendu, p.PnlRealiseBtc, 1e-12);
        Assert.AreEqual(1, book.ReglerEcheances(MarcheDeTest.E2, prix).Count);
        Assert.IsFalse(b.ADesJambesOuvertes(t.Id));
        Assert.AreEqual(0, book.ReglerEcheances(MarcheDeTest.E2, prix).Count, "aucun double règlement");
    }

    /// <summary>
    /// On vérifie qu'une petite clôture respecte les pas des options et du notionnel USD du future.
    /// </summary>
    [TestMethod]
    public void Deboucler_PetitProrata_RespectePasOptionsEtMontantFuture()
    {
        var (b, _, t) = PositionOtc();
        var fermeture = b.Deboucler(t.Id, 0.1, MarcheDeTest.Etat());
        foreach (var j in fermeture.Jambes)
        {
            Assert.IsTrue(t.Jambes.Any(o => o.Id == j.IdJambeOrigine));
            Assert.AreNotEqual(j.Id, j.IdJambeOrigine);
            Assert.IsTrue(j.Nature == NatureJambe.Future
                ? j.NotionnelUsd.EstMultipleDe(j.PasNotionnelUsd)
                : Math.Abs(j.QuantiteSignee).EstMultipleDe(j.PasQuantite));
        }
        Assert.IsFalse(fermeture.Jambes.Any(j => Math.Abs(j.QuantiteSignee + 0.033) < 1e-9));
        Assert.AreEqual(9.9, b.QuantiteOuverte(t.Id), 1e-9);
    }

    /// <summary>
    /// On vérifie que les instruments de couverture encore ouverts restent débouclables après la
    /// disparition de l'OTC.
    /// </summary>
    [TestMethod]
    public void Deboucler_ApresReglementOtc_FermeLaCouvertureResiduelle()
    {
        var (b, book, t) = PositionOtc();
        book.ReglerEcheances(Otc, new Dictionary<DateOnly, double>
        { [DateOnly.FromDateTime(MarcheDeTest.E1)] = 78000, [DateOnly.FromDateTime(Otc)] = 79000 });
        var d = b.Deboucler(t.Id, 0, MarcheA(Otc));
        Assert.AreEqual(2, d.Jambes.Count);
        Assert.IsFalse(b.ADesJambesOuvertes(t.Id));
        Assert.IsFalse(book.Evaluer(MarcheA(Otc)).Positions.Single().EstOuverte);
    }

    /// <summary>
    /// On vérifie qu'un prix de règlement invalide laisse la position ouverte pour attendre une donnée
    /// utilisable.
    /// </summary>
    [DataTestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(0.0)]
    public void Regler_PrixInvalide_PositionConservee(double prix)
    {
        var (b, book, t) = PositionOtc();
        Assert.ThrowsException<PricerException>(() => b.Regler(t.Id, prix, MarcheDeTest.E1));
        Assert.AreEqual(0, book.ReglerEcheances(MarcheDeTest.E1,
            new Dictionary<DateOnly, double> { [DateOnly.FromDateTime(MarcheDeTest.E1)] = prix }).Count);
        Assert.AreEqual(1, b.Trades.Count);
        Assert.AreEqual(3.3, b.QuantiteOuverteJambe(t.Id, t.Jambes[1].Id));
    }

    /// <summary>
    /// On vérifie qu'une valeur au mark manquante reste indisponible et distincte de la valeur de
    /// modèle.
    /// </summary>
    [TestMethod]
    public void Evaluer_MarkAbsent_NeSubstitueNiZeroNiTheorique()
    {
        var (b, book, t) = PositionOtc();
        var e = MarcheDeTest.Etat();
        var nom = t.Jambes[2].Instrument;
        e.MettreAJour(e.Cotation(nom)! with { Mark = null });
        var etat = book.Evaluer(e);
        Assert.IsFalse(etat.ValorisationComplete);
        Assert.IsFalse(etat.RisqueComplet);
        Assert.IsTrue(double.IsNaN(etat.PnlTotalBtc));
        StringAssert.Contains(etat.Positions.Single().Statut, "mark absent");
        Assert.AreEqual(0, etat.PnlRealiseBtc);
        Assert.AreEqual(1, b.Trades.Count);
    }

    /// <summary>
    /// On vérifie que le P&amp;L et le delta du future suivent son notionnel USD lorsque le prix du
    /// marché change.
    /// </summary>
    [TestMethod]
    public void FutureInverse_PrixBouge_NotionnelConstantPnlEtDeltaCorrects()
    {
        var (b, _, t) = PositionOtc();
        var j = t.Jambes[3] with { Couverture = false, QuantiteSignee = 30000.0 / 74500, NotionnelUsd = 30000 };
        t = t with { Jambes = [j], Quantite = 1, TypeStructure = TypeStructure.OptionSimple };
        b.Charger([t]);
        var e = MarcheDeTest.Etat();
        e.MettreAJour(e.Cotation(j.Instrument)! with { Mark = 90000, Bid = 89995, Ask = 90005 });
        var etat = new Book(b).Evaluer(e);
        Assert.AreEqual(30000 * (1 / 74500.0 - 1 / 90000.0), etat.PnlTotalBtc, 1e-12);
        Assert.AreEqual(30000 / 90000.0, etat.GrecquesTotales.Delta, 1e-12);
        var d = b.Deboucler(t.Id, 1, e);
        Assert.AreEqual(-30000, d.Jambes.Single().NotionnelUsd, 1e-7);
        Assert.AreEqual(30000 * (1 / 74500.0 - 1 / 89995.0), new Book(b).Evaluer(e).PnlRealiseBtc, 1e-12);
    }

    /// <summary>
    /// On vérifie qu'une fermeture incohérente ne remplace pas le portefeuille déjà chargé.
    /// </summary>
    [TestMethod]
    public void Charger_FermetureCorrompue_RefuseSansRemplacerLeBookCourant()
    {
        var (b, _, t) = PositionOtc();
        var fermeture = t with
        {
            Id = Guid.NewGuid(),
            Nature = NatureTrade.Debouclage,
            IdTradeOrigine = t.Id,
            Jambes = [t.Jambes[0] with { Id = Guid.NewGuid(), IdJambeOrigine = Guid.NewGuid(), QuantiteSignee = 1 }]
        };
        Assert.ThrowsException<PricerException>(() => b.Charger([t, fermeture]));
        Assert.AreEqual(1, b.Trades.Count);
        Assert.AreEqual(t.Id, b.Trades.Single().Id);
        Assert.AreEqual(10, b.QuantiteOuverte(t.Id));
    }
}
