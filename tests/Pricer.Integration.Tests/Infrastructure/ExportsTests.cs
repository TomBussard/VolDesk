using ClosedXML.Excel;
using Pricer.Domaine.Booking;
using Pricer.Domaine.Modeles;
using Pricer.Infrastructure.Exports;

namespace Pricer.Integration.Tests.Infrastructure;

/// <summary>
/// Export integration tests: writing to disk and then rereading with the same library.
/// </summary>
[TestClass]
public class ExportsTests
{
    private string _dossier = "";
    private static readonly DateTime T = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime E = new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// We create a temporary folder specific to this test to isolate its files from other
    /// executions.
    /// </summary>
    [TestInitialize]
    public void Preparer() => _dossier = Directory.CreateTempSubdirectory("pricer-exports-").FullName;

    /// <summary>
    /// The temporary files are removed from the script once the checks are completed.
    /// </summary>
    [TestCleanup]
    public void Nettoyer() => Directory.Delete(_dossier, true);

    /// <summary>
    /// A stable trade is created whose fields and legs can be read in exported formats.
    /// </summary>
    private static Trade Trade(Guid id) => new()
    {
        Id = id,
        Horodatage = T,
        Nature = NatureTrade.Ouverture,
        TypeStructure = TypeStructure.CallSpread,
        Description = "CallSpread 20NOV26 72000C/80000C",
        Sens = Sens.Achat,
        Quantite = 2,
        Echeance = E,
        Jambes =
        [
            new JambeExecutee { Instrument = "BTC-20NOV26-72000-C", Nature = NatureJambe.OptionCotee, Type = TypeOption.Call, Strike = 72000, Echeance = E, QuantiteSignee = 2, PrixExecution = 0.0425, Future = 74000.5, Vol = 0.45 },
            new JambeExecutee { Instrument = "BTC-20NOV26-80000-C", Nature = NatureJambe.OptionCotee, Type = TypeOption.Call, Strike = 80000, Echeance = E, QuantiteSignee = -2, PrixExecution = 0.0075, Future = 74000.5, Vol = 0.47 }
        ]
    };

    /// <summary>
    /// History and book status are combined to compare the three exporters on the same values.
    /// </summary>
    private static (List<Trade>, EtatBook) Donnees()
    {
        var t = Trade(Guid.NewGuid());
        var etat = new EtatBook
        {
            Horodatage = T,
            Indice = 73900,
            Positions =
            [
                new PositionStructure
                {
                    Origine = t, QuantiteOuverte = 2, ValeurMarkBtc = 0.069, ValeurTheoriqueBtc = 0.0695, PrimeOuverteBtc = t.PrimeBtc,
                    PnlRealiseBtc = 0, Grecques = new Grecques(0.4, 1e-5, 12.5, -40), JambesOuvertes = []
                }
            ],
            GrecquesParEcheance = new Dictionary<DateTime, Grecques> { [E] = new Grecques(0.4, 1e-5, 12.5, -40) }
        };
        return ([t], etat);
    }

    /// <summary>
    /// The CSV is read again to check its legs, columns and numerical notation.
    /// </summary>
    [TestMethod]
    public void ExportCsv_RelectureCsvHelper_UneLigneParJambeEtPointDecimal()
    {
        var (trades, etat) = Donnees();
        var fichiers = new ExportCsv().Exporter(trades, etat, _dossier);

        var blotter = ExportCsv.Lire<LigneBlotter>(fichiers[0]);
        var book = ExportCsv.Lire<LigneBook>(fichiers[1]);

        Assert.AreEqual(2, blotter.Count);
        Assert.AreEqual(0.0425, blotter[0].PrixExecution);
        Assert.AreEqual(trades[0].PrimeBtc, blotter[0].PrimeTradeBtc, 1e-15);
        Assert.AreEqual((etat.Positions[0].PnlLatentBtc) * 73900, book[0].PnlTotalUsd!.Value, 1e-9);
        StringAssert.Contains(File.ReadAllText(fichiers[0]), "0.0425", "culture invariante : point décimal");
    }

    /// <summary>
    /// The binder is reopened to check its five sheets and the actual values recorded.
    /// </summary>
    [TestMethod]
    public void ExportExcel_RelectureClosedXml_CinqFeuillesEtValeurs()
    {
        var (trades, etat) = Donnees();
        string fichier = new ExportExcel().Exporter(trades, etat, _dossier).Single();

        using var classeur = new XLWorkbook(fichier);
        CollectionAssert.AreEqual(new[] { "Synthèse", "Blotter", "Book", "Grecques", "Jambes" }, classeur.Worksheets.Select(w => w.Name).ToArray());
        var blotter = classeur.Worksheet("Blotter");
        Assert.AreEqual("BTC-20NOV26-72000-C", blotter.Cell(2, 9).GetString());
        Assert.AreEqual(2, blotter.Tables.Single().DataRange.RowCount());
        Assert.AreEqual("TOTAL", classeur.Worksheet("Grecques").Cell(3, 1).GetString());
        Assert.AreEqual(73900, classeur.Worksheet("Synthèse").Cell(2, 2).GetDouble());
    }

    /// <summary>
    /// It is verified that a JSON round trip retains trade IDs, parameters, and legs.
    /// </summary>
    [TestMethod]
    public void ExportJson_SauvegarderPuisCharger_BlotterIdentique()
    {
        var (trades, _) = Donnees();
        string fichier = Path.Combine(_dossier, "blotter.json");

        ExportJson.Sauvegarder(trades, fichier);
        var recharge = ExportJson.Charger(fichier);

        Assert.AreEqual(trades[0].Id, recharge[0].Id);
        Assert.AreEqual(trades[0].PrimeBtc, recharge[0].PrimeBtc, 1e-15);
        Assert.AreEqual(TypeStructure.CallSpread, recharge[0].TypeStructure);
        StringAssert.Contains(File.ReadAllText(fichier), "\"CallSpread\"", "enums sérialisés en texte");

        var blotter = new Blotter();
        blotter.Charger(recharge);
        Assert.AreEqual(2, blotter.QuantiteOuverte(trades[0].Id));
    }

    /// <summary>
    /// It is verified that each requested format has its exporter and that the available
    /// formats are consistent.
    /// </summary>
    [TestMethod]
    public void FabriqueExportateurs_TroisFormats()
    {
        CollectionAssert.AreEqual(new[] { "CSV", "Excel", "JSON" }, FabriqueExportateurs.Tous().Select(e => e.Format).ToArray());
    }

    /// <summary>
    /// It is verified that exports distinguish an absent value from a market value equal to
    /// zero.
    /// </summary>
    [TestMethod]
    public void Exports_ValorisationManquante_CellulesVidesEtStatutExplicite()
    {
        var (trades, original) = Donnees();
        var etat = original with
        {
            Positions = [original.Positions[0] with
        {
            ValeurMarkBtc = double.NaN, ValeurTheoriqueBtc = double.NaN,
            ValorisationComplete = false, RisqueComplet = false, Statut = "Mark absent"
        }]
        };
        var fichiers = new ExportCsv().Exporter(trades, etat, _dossier);
        Assert.AreEqual(5, fichiers.Count);
        var ligne = ExportCsv.Lire<LigneBook>(fichiers[1]).Single();
        Assert.IsNull(ligne.ValeurMarkBtc);
        Assert.IsNull(ligne.PnlTotalUsd);
        Assert.AreEqual("Mark absent", ligne.Statut);
        StringAssert.Contains(File.ReadAllText(fichiers[2]), "Partiel");
        string excel = new ExportExcel().Exporter(trades, etat, _dossier).Single();
        using var classeur = new XLWorkbook(excel);
        Assert.AreEqual("Indisponible", classeur.Worksheet("Synthèse").Cell(3, 2).GetString());
        Assert.AreEqual("Partiel", classeur.Worksheet("Synthèse").Cell(15, 2).GetString());
        Assert.IsTrue(classeur.Worksheet("Book").Cell(2, 5).IsEmpty());
    }

    /// <summary>
    /// It is verified that a backup replacement retains the last complete list of trades.
    /// </summary>
    [TestMethod]
    public void Sauvegarde_DeuxEcrituresSuccessives_GardeLaDerniereVersionComplete()
    {
        var (trades, _) = Donnees();
        string fichier = Path.Combine(_dossier, "session.json");
        ExportJson.Sauvegarder(trades, fichier);
        var autre = Trade(Guid.NewGuid());
        trades.Add(autre);
        ExportJson.Sauvegarder(trades, fichier);
        var recharge = ExportJson.Charger(fichier);
        Assert.AreEqual(2, recharge.Count);
        Assert.AreEqual(autre.Id, recharge[1].Id);
        Assert.AreEqual(0, Directory.GetFiles(_dossier, "*.tmp").Length);
    }
}
