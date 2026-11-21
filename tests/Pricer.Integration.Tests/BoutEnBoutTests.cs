using Pricer.App;

namespace Pricer.Integration.Tests;

/// <summary>
/// Test de bout en bout (E2E) : l'application complète démarre en mode rejeu sur l'enregistrement
/// de démonstration, sans réseau, exécute le scénario (cotations, refus de saisie, couverture,
/// booking, débouclage, book) et produit ses exports.
/// </summary>
[TestClass]
public class BoutEnBoutTests
{
    /// <summary>
    /// On remonte jusqu'à la solution pour retrouver les données du test sans imposer un dossier de
    /// lancement.
    /// </summary>
    private static string RacineDepot()
    {
        var dossier = new DirectoryInfo(AppContext.BaseDirectory);
        while (dossier is not null && !File.Exists(Path.Combine(dossier.FullName, "DeribitPricer.sln"))) dossier = dossier.Parent;
        return dossier?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable.");
    }

    /// <summary>
    /// On vérifie qu'un parcours console complet peut traiter les structures et produire les fichiers
    /// sans connexion.
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
    /// On vérifie qu'un fichier absent produit un échec expliqué au lieu d'arrêter brutalement
    /// l'application.
    /// </summary>
    [TestMethod]
    public async Task Main_FichierDeRejeuAbsent_CodeRetour1SansException()
    {
        int code = await Programme.Main(["--mode", "Rejeu", "--fichier", "absent.jsonl", "--demo"]);
        Assert.AreEqual(1, code);
    }
}

