using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Pricer.Domaine.Booking;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Infrastructure.Configuration;
using Pricer.Infrastructure.Exports;
using Pricer.Infrastructure.Services;
using Pricer.Infrastructure.Transport;
using Pricer.Web.Services;

namespace Pricer.Integration.Tests;

[TestClass]
public class LectureEtSessionTests
{
    private string _dossier = "";
    private WebApplication? _app;
    /// <summary>
    /// On crée un dossier temporaire propre à ce test pour isoler ses fichiers des autres exécutions.
    /// </summary>
    [TestInitialize] public void Preparer() => _dossier = Directory.CreateTempSubdirectory("fusion-lecture-").FullName;
    /// <summary>
    /// On arrête les services de marché et on supprime le dossier temporaire pour ne pas conserver de
    /// flux après le test.
    /// </summary>
    [TestCleanup]
    public async Task Nettoyer()
    {
        if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        Directory.Delete(_dossier, true);
    }

    /// <summary>
    /// On vérifie que la pause conserve un marché cohérent et que le pas ne laisse passer qu'une mise à
    /// jour.
    /// </summary>
    [TestMethod]
    public async Task Rejeu_PauseEtPas_SansAvanceDuTempsNiDuRest()
    {
        string fichier = Path.Combine(_dossier, "lecture.jsonl");
        File.WriteAllLines(fichier, [
            """{"t":"2026-11-11T10:00:00Z","type":"rest","cle":"indice?","donnees":{"prix":100}}""",
            """{"t":"2026-11-11T10:00:00.100Z","type":"rest","cle":"indice?","donnees":{"prix":101}}""",
            """{"t":"2026-11-11T10:00:00.100Z","type":"ws","cle":"ticker.X.100ms","donnees":{}}""",
            """{"t":"2026-11-11T10:00:00.200Z","type":"ws","cle":"ticker.X.100ms","donnees":{}}"""]);
        await using var r = new TransportRejeu(fichier, 1);
        r.Lecture.Pause();
        await using var lecture = r.LireFluxAsync(CancellationToken.None).GetAsyncEnumerator();
        var premier = lecture.MoveNextAsync().AsTask();
        await Task.Delay(60);
        Assert.IsFalse(premier.IsCompleted, "la pause doit retenir la première cotation");
        Assert.AreEqual(100, (await r.AppelerAsync("indice", new Dictionary<string, string>())).Resultat.GetProperty("prix").GetInt32());
        DateTime arret = r.MaintenantVirtuel;
        r.Lecture.DonneeSuivante();
        Assert.IsTrue(await premier.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.IsTrue(r.Lecture.EnPause);
        Assert.IsTrue(r.MaintenantVirtuel > arret);
        Assert.AreEqual(101, (await r.AppelerAsync("indice", new Dictionary<string, string>())).Resultat.GetProperty("prix").GetInt32());
        var second = lecture.MoveNextAsync().AsTask();
        await Task.Delay(60);
        Assert.IsFalse(second.IsCompleted, "un pas ne doit pas laisser passer une deuxième cotation");
        r.Lecture.DonneeSuivante();
        Assert.IsTrue(await second.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(2, r.MessagesEmis);
        r.Lecture.Reprendre();
        Assert.IsFalse(await lecture.MoveNextAsync());
        Assert.IsTrue(r.FluxTermine);
    }

    /// <summary>
    /// On vérifie que la démonstration produit des instruments et un indice adaptés à la devise
    /// choisie.
    /// </summary>
    [DataTestMethod]
    [DataRow("BTC", 100000d)]
    [DataRow("ETH", 3500d)]
    public async Task Demonstration_DeviseReelleDuModeleEtEchellePropres(string devise, double indice)
    {
        var o = new OptionsPricer { Mode = ModeDonnees.Demonstration, Devise = devise, FichierSession = Path.Combine(_dossier, "active.json") };
        using var journaux = Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { });
        await using var source = FabriqueSourceDonnees.Creer(o, journaux);
        var instruments = await source.ChargerOptionsAsync(devise);
        Assert.IsTrue(instruments.All(i => i.Devise == devise && i.Nom.StartsWith(devise + "-", StringComparison.Ordinal)));
        Assert.AreEqual(indice, (await source.ObtenirIndiceAsync(devise)).Prix);
        Assert.IsTrue(instruments.Any(i => Math.Abs(i.Strike - indice) < 1e-9));
        Assert.IsTrue(instruments.All(i => i.PasQuantite == (devise == "BTC" ? 0.1 : 1)));
    }

    /// <summary>
    /// On construit les services avec une démonstration locale et une configuration de test isolée.
    /// </summary>
    private async Task DemarrerAsync()
    {
        _app = Pricer.Web.Program.CreerApplication(["--mode", "Demonstration", "--devise", "BTC", "--port", "http://127.0.0.1:0", "--Pricer:FichierSession", Path.Combine(_dossier, "active.json"), "--exports", Path.Combine(_dossier, "exports")]);
        await _app.StartAsync();
        var etat = _app.Services.GetRequiredService<EtatApplication>();
        for (int n = 0; n < 200 && !etat.Pret && etat.Erreur is null; n++) await Task.Delay(20);
        Assert.IsTrue(etat.Pret, etat.Erreur ?? "marché non chargé");
        _app.Services.GetRequiredService<ServiceMarche>().Rejeu!.Lecture.Pause();
    }

    /// <summary>
    /// On vérifie qu'une opération est sauvegardée immédiatement et qu'une nouvelle session ne détruit
    /// pas l'ancien portefeuille.
    /// </summary>
    [TestMethod]
    public async Task Session_TradeSauvegardePuisRecommencer_AncienBookConserveEtDevisesIsolees()
    {
        await DemarrerAsync();
        var services = _app!.Services;
        var session = services.GetRequiredService<ServiceSession>();
        var marche = services.GetRequiredService<ServiceMarche>();
        var blotter = services.GetRequiredService<Blotter>();
        var etat = marche.EtatCourant;
        var structure = FabriqueStructures.Creer(TypeStructure.CallSpread, etat.Referentiel.Echeances[1], [90000, 100000], etat.Referentiel);
        blotter.Booker(PricerStructure.Coter(structure, Sens.Achat, 1, etat));
        string ancien = session.CheminBook;
        Assert.AreEqual(1, ExportJson.Charger(ancien).Count, "la sauvegarde doit suivre le booking immédiatement");
        await session.RecommencerAsync();
        string nouveau = session.CheminBook;
        Assert.AreNotEqual(ancien, nouveau);
        Assert.AreEqual(0, blotter.Trades.Count);
        Assert.AreEqual(1, ExportJson.Charger(ancien).Count);
        await session.ChangerAsync(ModeDonnees.Demonstration, "ETH", "data/replays/demo.jsonl.gz");
        Assert.AreEqual("ETH", marche.EtatCourant.Referentiel.Devise);
        Assert.AreEqual(0, blotter.Trades.Count);
        Assert.AreNotEqual(nouveau, session.CheminBook);
        await session.ChangerAsync(ModeDonnees.Demonstration, "BTC", "data/replays/demo.jsonl.gz");
        Assert.AreEqual(nouveau, session.CheminBook, "le retour à BTC doit retrouver sa dernière session");
    }

    /// <summary>
    /// On vérifie qu'une configuration illisible laisse démarrer une démonstration tout en conservant
    /// le fichier à examiner.
    /// </summary>
    [TestMethod]
    public void ConfigurationIllisible_RetourDemoSansEcraserLeFichier()
    {
        string fichier = Path.Combine(_dossier, "active.json");
        File.WriteAllText(fichier, "ceci n'est pas du JSON");
        var o = new OptionsPricer { FichierSession = fichier };
        ServiceSession.ChargerAvantDemarrage(o, true);
        Assert.AreEqual(ModeDonnees.Demonstration, o.Mode);
        Assert.IsNotNull(o.AvertissementSession);
        Assert.AreEqual("ceci n'est pas du JSON", File.ReadAllText(fichier));
    }

    /// <summary>
    /// On vérifie que le book d'une source ou d'une devise ne peut pas être repris par une autre.
    /// </summary>
    [TestMethod]
    public void Source_LesClesLiveEtDemoEtDevisesSontDistinctes()
    {
        string Cle(ModeDonnees m, string d) => ServiceSession.CleSource(new OptionsPricer { Mode = m, Devise = d });
        Assert.AreNotEqual(Cle(ModeDonnees.Live, "BTC"), Cle(ModeDonnees.Demonstration, "BTC"));
        Assert.AreNotEqual(Cle(ModeDonnees.Demonstration, "BTC"), Cle(ModeDonnees.Demonstration, "ETH"));
        Assert.AreEqual(Cle(ModeDonnees.Live, "BTC"), Cle(ModeDonnees.Enregistrement, "BTC"));
    }
}
