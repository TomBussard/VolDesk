using System.Net;
using Pricer.Web;

namespace Pricer.Integration.Tests;

/// <summary>
/// Test de bout en bout de l'interface web : le serveur Blazor démarre en rejeu sur l'enregistrement
/// de démonstration, charge le marché et sert toutes ses pages.
/// </summary>
[TestClass]
public class InterfaceWebTests
{
    /// <summary>
    /// On remonte jusqu'à la solution pour retrouver les données du test sans imposer un dossier de
    /// lancement.
    /// </summary>
    private static string RacineDepot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DeribitPricer.sln"))) d = d.Parent;
        return d!.FullName;
    }

    /// <summary>
    /// On vérifie que le serveur local répond sur ses pages et finit de charger le marché enregistré.
    /// </summary>
    [TestMethod]
    [Timeout(120_000)]
    public async Task Serveur_ModeRejeu_ToutesLesPagesRepondentEtLeMarcheSeCharge()
    {
        string replay = Path.Combine(RacineDepot(), "data", "replays", "demo.jsonl.gz");
        if (!File.Exists(replay)) Assert.Inconclusive("Enregistrement de démonstration absent.");
        string exports = Directory.CreateTempSubdirectory("pricer-web-").FullName;

        await using var app = Program.CreerApplication(["--mode", "Rejeu", "--fichier", replay, "--vitesse", "0", "--exports", exports, "--port", "5099",
            "--contentRoot", Path.Combine(RacineDepot(), "src", "Pricer.Web"), "--Pricer:FichierSession", Path.Combine(exports, "session.json")]);
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5099") };
            var etat = (Pricer.Web.Services.EtatApplication)app.Services.GetService(typeof(Pricer.Web.Services.EtatApplication))!;
            for (int i = 0; i < 100 && !etat.Pret && etat.Erreur is null; i++) await Task.Delay(100);

            Assert.IsNull(etat.Erreur, etat.Erreur);
            Assert.IsTrue(etat.Pret, "le marché doit être chargé");
            foreach (var page in new[] { "/", "/referentiel", "/pricer", "/sur-mesure", "/book", "/blotter", "/exports", "/donnees" })
            {
                var reponse = await client.GetAsync(page);
                Assert.AreEqual(HttpStatusCode.OK, reponse.StatusCode, page);
                StringAssert.Contains(await reponse.Content.ReadAsStringAsync(), "Deribit Desk", page);
            }
            Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/app.css")).StatusCode);
            StringAssert.Contains(await client.GetStringAsync("/sante"), "DeribitDesk-Fusion");
            Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/telecharger/inexistant.csv")).StatusCode);
        }
        finally
        {
            await app.StopAsync();
            Directory.Delete(exports, true);
        }
    }
}
