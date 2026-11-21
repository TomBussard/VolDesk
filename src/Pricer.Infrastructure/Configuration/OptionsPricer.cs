using Pricer.Domaine.Modeles;

namespace Pricer.Infrastructure.Configuration;

/// <summary>
/// The settings are grouped in the Pricer section to change source or currency without changing
/// the code.
/// </summary>
public sealed class OptionsPricer
{
    public const string Section = "Pricer";

    public ModeDonnees Mode { get; set; } = ModeDonnees.Live;
    public string Devise { get; set; } = "BTC";
    public string UrlRest { get; set; } = "https://www.deribit.com/api/v2/";
    public string UrlWebSocket { get; set; } = "wss://www.deribit.com/ws/api/v2";
    /// <summary>
    /// This is the JSON Lines file that will receive the recorded session data.
    /// </summary>
    public string FichierEnregistrement { get; set; } = "data/replays/session.jsonl";
    /// <summary>
    /// Here you choose the file you want to read again when the application works without a
    /// connection.
    /// </summary>
    public string FichierRejeu { get; set; } = "data/replays/demo.jsonl.gz";
    /// <summary>
    /// 1 is used for the original rhythm, 10 for a ten times faster rhythm and 0 for an
    /// unanticipated reading.
    /// </summary>
    public double VitesseRejeu { get; set; } = 1;
    /// <summary>
    /// REST calls are limited with a token bucket to respect the authorized flow without
    /// blocking small batches of queries.
    /// </summary>
    public double RequetesParSeconde { get; set; } = 5;
    public int Rafale { get; set; } = 10;
    /// <summary>
    /// The summary of options to have recent smiles, even for untracked instruments, is updated
    /// periodically.
    /// </summary>
    public int IntervalleResumeSecondes { get; set; } = 15;
    /// <summary>
    /// The WebSocket ticker range is chosen, for example 100ms or agg2, according to the
    /// desired update rate.
    /// </summary>
    public string IntervalleTicker { get; set; } = "100ms";
    /// <summary>
    /// The trade margin is expressed in points of volatility to convert it with the vega of the
    /// custom option.
    /// </summary>
    public double MargeVolPoints { get; set; } = 0.5;
    public string DossierExports { get; set; } = "exports";
    public string FichierBlotter { get; set; } = "exports/blotter.json";
    public string FichierSession { get; set; } = "data/session-active.json";
    public string CleBookSession { get; set; } = "";
    public DateTime? ReprendreAu { get; set; }
    public string? AvertissementSession { get; set; }
    /// <summary>
    /// The number of expiries followed during a capture is limited to keep a workable file
    /// without saving the entire market.
    /// </summary>
    public int EcheancesEnregistrees { get; set; } = 4;
    /// <summary>
    /// Strikes close to the future are recorded in this relative band; 0.25 corresponds to more
    /// or less 25%.
    /// </summary>
    public double LargeurEnregistrement { get; set; } = 0.25;

    /// <summary>
    /// The path to the root of the solution is attached when found. This allows you to run the
    /// application from the root or from a subproject without moving the data files.
    /// </summary>
    public OptionsPricer ResoudreChemins()
    {
        string racine = TrouverRacine(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();
        // A path relative to the found root is attached while maintaining an already absolute path.
        string Absolu(string chemin) => Path.IsPathRooted(chemin) ? chemin : Path.GetFullPath(Path.Combine(racine, chemin));
        FichierEnregistrement = Absolu(FichierEnregistrement);
        FichierRejeu = Absolu(FichierRejeu);
        DossierExports = Absolu(DossierExports);
        FichierBlotter = Absolu(FichierBlotter);
        FichierSession = Absolu(FichierSession);
        return this;
    }

    /// <summary>
    /// The folders are retrieved from the path provided to find the solution file.
    /// </summary>
    /// <returns>On renvoie la racine trouvée, ou null si aucun dossier parent ne correspond.</returns>
    private static string? TrouverRacine(string depart)
    {
        for (var d = new DirectoryInfo(depart); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "DeribitPricer.sln"))) return d.FullName;
        return null;
    }
}
