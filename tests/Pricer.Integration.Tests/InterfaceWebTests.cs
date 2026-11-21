using System.Net;
using Pricer.Web;

namespace Pricer.Integration.Tests;

/// <summary>
/// End-to-end test of the web interface: the Blazor server starts playing again on the demo
/// recording, loads the market and serves all its pages.
/// </summary>
[TestClass]
public class InterfaceWebTests
{
    /// <summary>
    /// We go back to the solution to retrieve the test data without imposing a launch file.
    /// </summary>
    private static string RacineDepot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DeribitPricer.sln"))) d = d.Parent;
        return d!.FullName;
    }

    /// <summary>
    /// We check that the local server is responding on its pages and finish loading the
    /// registered market.
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
                StringAssert.Contains(await reponse.Content.ReadAsStringAsync(), "VolDesk", page);
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
