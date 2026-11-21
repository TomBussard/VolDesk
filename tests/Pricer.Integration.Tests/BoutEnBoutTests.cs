using Pricer.App;

namespace Pricer.Integration.Tests;

/// <summary>
/// End-to-end test (E2E): the full application starts in replay mode on the demo record,
/// without network, executes the scenario (rating, refusal to enter, cover, booking, debunking,
/// book) and produces its exports.
/// </summary>
[TestClass]
public class BoutEnBoutTests
{
    /// <summary>
    /// We go back to the solution to retrieve the test data without imposing a launch file.
    /// </summary>
    private static string RacineDepot()
    {
        var dossier = new DirectoryInfo(AppContext.BaseDirectory);
        while (dossier is not null && !File.Exists(Path.Combine(dossier.FullName, "DeribitPricer.sln"))) dossier = dossier.Parent;
        return dossier?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable.");
    }

    /// <summary>
    /// We check that a complete console path can process structures and produce files without a
    /// connection.
    /// </summary>
    [TestMethod]
    [Timeout(120_000)]
    public async Task Main_ModeRejeuDemo_TradesBookesEtFichiersExportes()
    {
        string replay = Path.Combine(RacineDepot(), "data", "replays", "demo.jsonl.gz");
        if (!File.Exists(replay)) Assert.Inconclusive("Enregistrement de démonstration absent.");
        string exports = Directory.CreateTempSubdirectory("pricer-e2e-").FullName;

        try
        {
            int code = await Programme.Main(["--mode", "Rejeu", "--fichier", replay, "--vitesse", "0", "--exports", exports, "--demo"]);

            Assert.AreEqual(0, code);
            var fichiers = Directory.GetFiles(exports);
            Assert.AreEqual(5, fichiers.Count(f => f.EndsWith(".csv")));
            Assert.AreEqual(1, fichiers.Count(f => f.EndsWith(".xlsx")));
            Assert.AreEqual(1, fichiers.Count(f => f.EndsWith(".json")));

            var blotter = Pricer.Infrastructure.Exports.ExportJson.Charger(fichiers.Single(f => f.EndsWith(".json")));
            Assert.IsTrue(blotter.Count >= 6, $"au moins 4 structures + 1 OTC + 1 débouclage, obtenu {blotter.Count}");
            Assert.IsTrue(blotter.Any(t => t.TypeStructure == Pricer.Domaine.Modeles.TypeStructure.OptionSurMesure &&
                t.Jambes.Any(j => j.Nature == Pricer.Domaine.Modeles.NatureJambe.Future)), "L'OTC doit réellement être traité avec sa couverture.");
            Assert.IsTrue(blotter.Any(t => t.Nature == Pricer.Domaine.Booking.NatureTrade.Debouclage && t.IdTradeOrigine is not null));
        }
        finally
        {
            Directory.Delete(exports, true);
        }
    }

    /// <summary>
    /// A missing file is checked for failure explained instead of abruptly stopping the
    /// application.
    /// </summary>
    [TestMethod]
    public async Task Main_FichierDeRejeuAbsent_CodeRetour1SansException()
    {
        int code = await Programme.Main(["--mode", "Rejeu", "--fichier", "absent.jsonl", "--demo"]);
        Assert.AreEqual(1, code);
    }
}
