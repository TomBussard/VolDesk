using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Pricer.Domaine.Booking;

namespace Pricer.Infrastructure.Exports;

/// <summary>
/// On utilise une interface commune pour exporter le même blotter et le même book, quel que soit le format
/// choisi.
/// </summary>
public interface IExportateur
{
    string Format { get; }
    /// <summary>
    /// On écrit le blotter et les données du book utiles au format choisi.
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
/// On centralise le choix de l'exportateur pour que l'écran n'ait pas à connaître l'écriture propre à
/// chaque format.
/// </summary>
public static class FabriqueExportateurs
{
    /// <summary>
    /// On choisit l’exportateur correspondant au format demandé et on refuse un format inconnu.
    /// </summary>
    public static IExportateur Creer(FormatExport format) => format switch
    {
        FormatExport.Csv => new ExportCsv(),
        FormatExport.Excel => new ExportExcel(),
        FormatExport.Json => new ExportJson(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Format d'export inconnu.")
    };

    /// <summary>
    /// On propose un exportateur pour chaque format disponible, dans l’ordre défini par l’énumération.
    /// </summary>
    public static IEnumerable<IExportateur> Tous() => Enum.GetValues<FormatExport>().Select(Creer);

    /// <summary>
    /// On crée le dossier de sortie puis on ajoute la date et l’heure au nom du fichier exporté.
    /// </summary>
    internal static string Horodater(string dossier, string nom, string extension)
    {
        Directory.CreateDirectory(dossier);
        return Path.Combine(dossier, $"{nom}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}");
    }
}

/// <summary>
/// On écrit les CSV avec CsvHelper et une culture invariante : le séparateur et le point décimal restent
/// stables d'un ordinateur à l'autre.
/// </summary>
public sealed class ExportCsv : IExportateur
{
    public string Format => "CSV";

    /// <summary>
    /// On écrit cinq CSV séparés pour le blotter, les positions, les grecques, les jambes ouvertes et la
    /// synthèse.
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
    /// On écrit les propriétés des lignes en CSV avec les mêmes conventions numériques sur tous les
    /// ordinateurs.
    /// </summary>
    /// <param name="fichier">On remplace le contenu de ce fichier par le nouvel export.</param>
    public static void Ecrire<T>(string fichier, IEnumerable<T> lignes)
    {
        using var writer = new StreamWriter(fichier);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));
        csv.WriteRecords(lignes);
    }

    /// <summary>
    /// On relit le CSV avec les mêmes conventions que l’écriture pour retrouver des objets du type
    /// demandé.
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
/// On a choisi ClosedXML pour produire un vrai classeur .xlsx sans exiger une installation d'Excel. On
/// écrit les tableaux par plages entières avec InsertTable pour conserver leurs en-têtes et leurs colonnes.
/// </summary>
public sealed class ExportExcel : IExportateur
{
    public string Format => "Excel";

    /// <summary>
    /// On construit le classeur avec une synthèse et des feuilles détaillées pour le blotter, le book,
    /// les grecques et les jambes ouvertes.
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
    /// On crée une feuille et son tableau, on fige la ligne d’en-tête et on ajuste les colonnes. On
    /// indique explicitement une liste vide.
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
/// On sauvegarde le blotter en JSON pour retrouver ses opérations entre deux sessions et conserver les
/// liens entre les trades et leurs jambes.
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
    /// On exporte le blotter en JSON pour conserver les liens entre opérations et permettre une
    /// restauration ultérieure.
    /// </summary>
    public IReadOnlyList<string> Exporter(IReadOnlyList<Trade> blotter, EtatBook book, string dossier)
    {
        string fichier = FabriqueExportateurs.Horodater(dossier, "blotter", "json");
        Sauvegarder(blotter, fichier);
        return [fichier];
    }

    /// <summary>
    /// On sérialise toutes les opérations dans un fichier temporaire avant de remplacer la sauvegarde
    /// précédente.
    /// </summary>
    /// <param name="blotter">On conserve les identifiants et les références des jambes pour pouvoir reconstruire le book.</param>
    public static void Sauvegarder(IReadOnlyList<Trade> blotter, string fichier)
    {
        string? dossier = Path.GetDirectoryName(Path.GetFullPath(fichier));
        if (dossier is not null) Directory.CreateDirectory(dossier);
        // On écrit d'abord une copie complète, puis on remplace le fichier de la session.
        string temporaire = fichier + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaire, JsonSerializer.Serialize(blotter, Options));
        File.Move(temporaire, fichier, overwrite: true);
    }

    /// <summary>
    /// On relit les opérations sauvegardées et on laisse remonter une erreur de contenu plutôt que de
    /// masquer un fichier corrompu.
    /// </summary>
    /// <returns>On renvoie une liste vide si le fichier n’existe pas ou si son contenu JSON vaut null.</returns>
    public static List<Trade> Charger(string fichier) =>
        File.Exists(fichier) ? JsonSerializer.Deserialize<List<Trade>>(File.ReadAllText(fichier), Options) ?? [] : [];
}

