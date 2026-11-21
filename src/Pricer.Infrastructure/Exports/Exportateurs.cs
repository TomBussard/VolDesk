using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Pricer.Domaine.Booking;

namespace Pricer.Infrastructure.Exports;

/// <summary>
/// A common interface is used to export the same floater and book, regardless of the format
/// chosen.
/// </summary>
public interface IExportateur
{
    string Format { get; }
    /// <summary>
    /// We write the slotter and the data of the book useful in the chosen format.
    /// </summary>
    /// <param name="dossier">On place les fichiers générés dans ce dossier.</param>
    /// <returns>On renvoie les chemins des fichiers créés pour pouvoir les afficher ou les télécharger.</returns>
    IReadOnlyList<string> Exporter(IReadOnlyList<Trade> blotter, EtatBook book, string dossier);
}

public enum FormatExport
{
    Csv,
    Excel,
    Json
}

/// <summary>
/// The exporter's choice is centralized so that the screen does not have to know the proper
/// writing for each format.
/// </summary>
public static class FabriqueExportateurs
{
    /// <summary>
    /// The exporter is selected for the requested format and an unknown format is refused.
    /// </summary>
    public static IExportateur Creer(FormatExport format) => format switch
    {
        FormatExport.Csv => new ExportCsv(),
        FormatExport.Excel => new ExportExcel(),
        FormatExport.Json => new ExportJson(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Format d'export inconnu.")
    };

    /// <summary>
    /// An exporter is proposed for each available format in the sequence defined by the
    /// enumeration.
    /// </summary>
    public static IEnumerable<IExportateur> Tous() => Enum.GetValues<FormatExport>().Select(Creer);

    /// <summary>
    /// Create the output folder and add the date and time to the name of the exported file.
    /// </summary>
    internal static string Horodater(string dossier, string nom, string extension)
    {
        Directory.CreateDirectory(dossier);
        return Path.Combine(dossier, $"{nom}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}");
    }
}

/// <summary>
/// CSV is written with CsvHelper and an invariant culture: the separator and decimal point
/// remain stable from one computer to another.
/// </summary>
public sealed class ExportCsv : IExportateur
{
    public string Format => "CSV";

    /// <summary>
    /// Five separate CSVs are written to float, positions, Greeks, open legs and synthesis.
    /// </summary>
    /// <returns>On renvoie les cinq chemins dans cet ordre.</returns>
    public IReadOnlyList<string> Exporter(IReadOnlyList<Trade> blotter, EtatBook book, string dossier)
    {
        string fichierBlotter = FabriqueExportateurs.Horodater(dossier, "blotter", "csv");
        string fichierBook = FabriqueExportateurs.Horodater(dossier, "book", "csv");
        string fichierGrecques = FabriqueExportateurs.Horodater(dossier, "grecques", "csv");
        string fichierJambes = FabriqueExportateurs.Horodater(dossier, "jambes", "csv");
        string fichierSynthese = FabriqueExportateurs.Horodater(dossier, "synthese", "csv");
        Ecrire(fichierBlotter, blotter.SelectMany(t => t.VersLignes()));
        Ecrire(fichierBook, book.VersLignes());
        Ecrire(fichierGrecques, book.VersLignesGrecques());
        Ecrire(fichierJambes, book.VersJambesOuvertes());
        Ecrire(fichierSynthese, book.VersSynthese());
        return [fichierBlotter, fichierBook, fichierGrecques, fichierJambes, fichierSynthese];
    }

    /// <summary>
    /// The properties of the CSV lines are written with the same digital conventions on all
    /// computers.
    /// </summary>
    /// <param name="fichier">On remplace le contenu de ce fichier par le nouvel export.</param>
    public static void Ecrire<T>(string fichier, IEnumerable<T> lignes)
    {
        using var writer = new StreamWriter(fichier);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
        csv.WriteRecords(lignes);
    }

    /// <summary>
    /// The CSV is reread with the same conventions as writing to find objects of the type
    /// requested.
    /// </summary>
    /// <returns>On matérialise toutes les lignes avant de fermer le fichier.</returns>
    public static List<T> Lire<T>(string fichier)
    {
        using var reader = new StreamReader(fichier);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));
        return csv.GetRecords<T>().ToList();
    }
}

/// <summary>
/// We chose ClosedXML to produce a real .xlsx binder without requiring an Excel installation.
/// We write entire arrays with InsertTable to keep their headers and columns.
/// </summary>
public sealed class ExportExcel : IExportateur
{
    public string Format => "Excel";

    /// <summary>
    /// The binder is built with a synthesis and detailed sheets to float, book, Greeks and open
    /// legs.
    /// </summary>
    public IReadOnlyList<string> Exporter(IReadOnlyList<Trade> blotter, EtatBook book, string dossier)
    {
        string fichier = FabriqueExportateurs.Horodater(dossier, "pricer_deribit", "xlsx");
        using var classeur = new XLWorkbook();

        AjouterFeuille(classeur, "Blotter", blotter.SelectMany(t => t.VersLignes()).ToList());
        var feuilleBook = AjouterFeuille(classeur, "Book", book.VersLignes().ToList());
        AjouterFeuille(classeur, "Grecques", book.VersLignesGrecques().ToList());
        AjouterFeuille(classeur, "Jambes", book.VersJambesOuvertes().ToList());

        var synthese = classeur.Worksheets.Add("Synthèse", 1);
        object[,] cellules =
        {
            { "Horodatage (UTC)", book.Horodatage.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
            { "Indice (USD)", book.Indice },
            { $"Valeur au mark ({book.Devise})", book.ValeurMarkBtc },
            { $"Valeur théorique ({book.Devise})", book.ValeurTheoriqueBtc },
            { $"P&L latent ({book.Devise})", book.PnlLatentBtc },
            { $"P&L réalisé ({book.Devise})", book.PnlRealiseBtc },
            { $"P&L total ({book.Devise})", book.PnlTotalBtc },
            { "P&L total (USD)", book.PnlTotalUsd },
            { "Delta", book.GrecquesTotales.Delta },
            { "Gamma", book.GrecquesTotales.Gamma },
            { "Vega (USD / pt)", book.GrecquesTotales.Vega },
            { "Theta (USD / jour)", book.GrecquesTotales.Theta }
        };
        for (int i = 0; i < cellules.GetLength(0); i++)
        {
            synthese.Cell(i + 1, 1).Value = cellules[i, 0].ToString();
            synthese.Cell(i + 1, 2).Value = cellules[i, 1] is double d
                ? double.IsFinite(d) ? XLCellValue.FromObject(d) : XLCellValue.FromObject("Indisponible")
                : XLCellValue.FromObject(cellules[i, 1].ToString());
        }
        synthese.Cell(14, 1).Value = "Valorisation";
        synthese.Cell(14, 2).Value = book.Statut;
        synthese.Cell(15, 1).Value = "Risque";
        synthese.Cell(15, 2).Value = book.RisqueComplet ? "Complet" : "Partiel";
        synthese.Column(1).Style.Font.Bold = true;
        synthese.Columns().AdjustToContents();
        feuilleBook.Column("K").Style.NumberFormat.Format = "#,##0.00";

        classeur.SaveAs(fichier);
        return [fichier];
    }

    /// <summary>
    /// You create a sheet and its table, freeze the header line and adjust the columns. You
    /// explicitly indicate an empty list.
    /// </summary>
    private static IXLWorksheet AjouterFeuille<T>(XLWorkbook classeur, string nom, IReadOnlyList<T> lignes)
    {
        var feuille = classeur.Worksheets.Add(nom);
        if (lignes.Count > 0)
            feuille.Cell(1, 1).InsertTable(lignes, nom, true).Theme = XLTableTheme.TableStyleMedium2;
        else
            feuille.Cell(1, 1).Value = "Aucune ligne";
        feuille.SheetView.FreezeRows(1);
        feuille.Columns().AdjustToContents(1, 200);
        return feuille;
    }
}

/// <summary>
/// The floater is saved in JSON to recover its operations between two sessions and maintain the
/// links between the trades and their legs.
/// </summary>
public sealed class ExportJson : IExportateur
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Format => "JSON";

    /// <summary>
    /// The floater is exported to JSON to maintain the links between operations and allow
    /// further restoration.
    /// </summary>
    public IReadOnlyList<string> Exporter(IReadOnlyList<Trade> blotter, EtatBook book, string dossier)
    {
        string fichier = FabriqueExportateurs.Horodater(dossier, "blotter", "json");
        Sauvegarder(blotter, fichier);
        return [fichier];
    }

    /// <summary>
    /// Serialize all operations in a temporary file before replacing the previous backup.
    /// </summary>
    /// <param name="blotter">On conserve les identifiants et les références des jambes pour pouvoir reconstruire le book.</param>
    public static void Sauvegarder(IReadOnlyList<Trade> blotter, string fichier)
    {
        string? dossier = Path.GetDirectoryName(Path.GetFullPath(fichier));
        if (dossier is not null) Directory.CreateDirectory(dossier);
        // First write a full copy, then replace the session file.
        string temporaire = fichier + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaire, JsonSerializer.Serialize(blotter, Options));
        File.Move(temporaire, fichier, overwrite: true);
    }

    /// <summary>
    /// We reread the saved operations and let a content error go back instead of masking a
    /// corrupted file.
    /// </summary>
    /// <returns>On renvoie une liste vide si le fichier n’existe pas ou si son contenu JSON vaut null.</returns>
    public static List<Trade> Charger(string fichier) =>
        File.Exists(fichier) ? JsonSerializer.Deserialize<List<Trade>>(File.ReadAllText(fichier), Options) ?? [] : [];
}
