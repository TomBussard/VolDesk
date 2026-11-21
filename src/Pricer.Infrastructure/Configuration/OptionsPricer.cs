using Pricer.Domaine.Modeles;

namespace Pricer.Infrastructure.Configuration;

/// <summary>
/// On regroupe les réglages dans la section Pricer pour changer de source ou de devise sans modifier le
/// code.
/// </summary>
public sealed class OptionsPricer
{
    public const string Section = "Pricer";

    public ModeDonnees Mode { get; set; } = ModeDonnees.Live;
    public string Devise { get; set; } = "BTC";
    public string UrlRest { get; set; } = "https://www.deribit.com/api/v2/";
    public string UrlWebSocket { get; set; } = "wss://www.deribit.com/ws/api/v2";
    /// <summary>On indique ici le fichier JSON Lines qui recevra les données de la session enregistrée.</summary>
    public string FichierEnregistrement { get; set; } = "data/replays/session.jsonl";
    /// <summary>On choisit ici le fichier à relire lorsque l'application fonctionne sans connexion.</summary>
    public string FichierRejeu { get; set; } = "data/replays/demo.jsonl.gz";
    /// <summary>
    /// On utilise 1 pour le rythme d'origine, 10 pour un rythme dix fois plus rapide et 0 pour une lecture
    /// sans attente.
    /// </summary>
    public double VitesseRejeu { get; set; } = 1;
    /// <summary>
    /// On limite les appels REST avec un seau à jetons pour respecter le débit autorisé sans bloquer les
    /// petits lots de requêtes.
    /// </summary>
    public double RequetesParSeconde { get; set; } = 5;
    public int Rafale { get; set; } = 10;
    /// <summary>
    /// On actualise périodiquement le résumé des options pour disposer de smiles récents, même pour les
    /// instruments non suivis à l'écran.
    /// </summary>
    public int IntervalleResumeSecondes { get; set; } = 15;
    /// <summary>
    /// On choisit l'intervalle des tickers WebSocket, par exemple 100ms ou agg2, selon le rythme de mise à
    /// jour souhaité.
    /// </summary>
    public string IntervalleTicker { get; set; } = "100ms";
    /// <summary>
    /// On exprime la marge commerciale en points de volatilité pour la convertir avec le vega de l'option
    /// sur mesure.
    /// </summary>
    public double MargeVolPoints { get; set; } = 0.5;
    public string DossierExports { get; set; } = "exports";
    public string FichierBlotter { get; set; } = "exports/blotter.json";
    public string FichierSession { get; set; } = "data/session-active.json";
    public string CleBookSession { get; set; } = "";
    public DateTime? ReprendreAu { get; set; }
    public string? AvertissementSession { get; set; }
    /// <summary>
    /// On limite le nombre d'échéances suivies pendant une capture pour conserver un fichier exploitable
    /// sans enregistrer tout le marché.
    /// </summary>
    public int EcheancesEnregistrees { get; set; } = 4;
    /// <summary>
    /// On enregistre les strikes proches du future dans cette bande relative ; 0,25 correspond à plus ou
    /// moins 25 %.
    /// </summary>
    public double LargeurEnregistrement { get; set; } = 0.25;

    /// <summary>
    /// On rattache les chemins relatifs à la racine de la solution lorsqu'on la trouve. On peut ainsi
    /// lancer l'application depuis la racine ou depuis un sous-projet sans déplacer les fichiers de
    /// données.
    /// </summary>
    public OptionsPricer ResoudreChemins()
    {
        string racine = TrouverRacine(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();
        // On rattache un chemin relatif à la racine trouvée tout en conservant un chemin déjà absolu.
        string Absolu(string chemin) => Path.IsPathRooted(chemin) ? chemin : Path.GetFullPath(Path.Combine(racine, chemin));
        FichierEnregistrement = Absolu(FichierEnregistrement);
        FichierRejeu = Absolu(FichierRejeu);
        DossierExports = Absolu(DossierExports);
        FichierBlotter = Absolu(FichierBlotter);
        FichierSession = Absolu(FichierSession);
        return this;
    }

    /// <summary>
    /// On remonte les dossiers à partir du chemin fourni pour retrouver le fichier de solution.
    /// </summary>
    /// <returns>On renvoie la racine trouvée, ou null si aucun dossier parent ne correspond.</returns>
    private static string? TrouverRacine(string depart)
    {
        for (var d = new DirectoryInfo(depart); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "DeribitPricer.sln"))) return d.FullName;
        return null;
    }
}
