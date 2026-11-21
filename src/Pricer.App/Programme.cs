using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pricer.App.Ecrans;
using Pricer.Domaine.Abstractions;
using Pricer.Domaine.Booking;
using Pricer.Domaine.Marche;
using Pricer.Infrastructure.Configuration;
using Pricer.Infrastructure.Exports;
using Pricer.Infrastructure.Services;
using Spectre.Console;

namespace Pricer.App;

/// <summary>
/// On prépare les services de la console au même endroit : on lit la configuration, on ouvre le journal et
/// on relie les dépendances. On garde ainsi les écrans séparés du choix de la source de données.
/// </summary>
public static class Programme
{
    /// <summary>
    /// On associe les arguments courts aux réglages de l'application, par exemple --mode Rejeu --fichier
    /// x.jsonl --vitesse 10.
    /// </summary>
    private static readonly Dictionary<string, string> Raccourcis = new()
    {
        ["--mode"] = "Pricer:Mode",
        ["--fichier"] = "Pricer:FichierRejeu",
        ["--enregistrement"] = "Pricer:FichierEnregistrement",
        ["--vitesse"] = "Pricer:VitesseRejeu",
        ["--exports"] = "Pricer:DossierExports",
        ["--devise"] = "Pricer:Devise"
    };

    /// <summary>
    /// On choisit le parcours demandé en ligne de commande, on initialise le marché et on lance la
    /// console, la démonstration ou l’export de statistiques.
    /// </summary>
    /// <param name="args">On reçoit les réglages et les options du parcours, par exemple --mode Rejeu ou --demo.</param>
    /// <returns>On renvoie zéro à la fin du parcours, ou un si le chargement initial du marché échoue.</returns>
    public static async Task<int> Main(string[] args)
    {
        bool demo = args.Contains("--demo");
        int iStat = Array.IndexOf(args, "--statistiques");
        string? dossierStatistiques = iStat >= 0 && iStat + 1 < args.Length ? args[iStat + 1] : null;
        if (iStat >= 0) args = args.Where((_, i) => i != iStat && i != iStat + 1).ToArray();
        int iLatex = Array.IndexOf(args, "--latex");
        string? dossierLatex = iLatex >= 0 && iLatex + 1 < args.Length ? args[iLatex + 1] : null;
        if (iLatex >= 0) args = args.Where((_, i) => i != iLatex && i != iLatex + 1).ToArray();
        bool sansBlotter = args.Contains("--nouveau");
        int iDuree = Array.IndexOf(args, "--duree");
        int? dureeSecondes = iDuree >= 0 && iDuree + 1 < args.Length && int.TryParse(args[iDuree + 1], out int d) ? d : null;
        string[] argsConfig = args.Where((a, i) => a is not "--demo" and not "--nouveau" and not "--duree" && !(iDuree >= 0 && i == iDuree + 1)).ToArray();

        if (Console.IsOutputRedirected) AnsiConsole.Profile.Width = 180; // On garde des tableaux assez larges lorsque la sortie est enregistrée par une démo ou un test.

        await using var services = ConstruireServices(argsConfig, out var options);
        var log = services.GetRequiredService<ILogger<ServiceMarche>>();
        log.LogInformation("Démarrage en mode {Mode}", options.Mode);

        HorlogeMarche.Instance.Reinitialiser(); // On repart de l'heure du premier message pour ne pas réutiliser l'horloge de la session précédente.
        ServiceMarche marche;
        try
        {
            marche = services.GetRequiredService<ServiceMarche>(); // On laisse le conteneur libérer ce service à la fin du programme.
            await AnsiConsole.Status().StartAsync($"Chargement des options {options.Devise} ({options.Mode})…", _ => marche.DemarrerAsync());
        }
        catch (Exception ex) when (ex is Domaine.Exceptions.PricerException or HttpRequestException or TaskCanceledException)
        {
            log.LogError(ex, "Démarrage impossible");
            AnsiConsole.MarkupLine($"[bold red]Démarrage impossible :[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[grey]Sans connexion, lancer en rejeu : dotnet run -- --mode Rejeu --fichier data/replays/demo.jsonl.gz[/]");
            return 1;
        }

        var blotter = services.GetRequiredService<Blotter>();
        if (!demo && !sansBlotter) blotter.Charger(ExportJson.Charger(options.FichierBlotter));

        using var arret = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; arret.Cancel(); };

        if (dossierStatistiques is not null)
        {
            // On termine la lecture accélérée avant de comparer les dernières données disponibles aux champs Deribit.
            while (!marche.FluxTermine && !arret.IsCancellationRequested) await Task.Delay(200);
            var lignes = Analyse.AnalyseMarche.Valider(marche.EtatCourant);
            Analyse.AnalyseMarche.Ecrire(lignes, dossierStatistiques);
            AnsiConsole.MarkupLine($"[green]{lignes.Count} options validées → {Markup.Escape(dossierStatistiques)}[/]");
            return 0;
        }

        if (dureeSecondes is int duree)
        {
            // On peut limiter la durée d'une capture sans ouvrir les menus, ce qui facilite les enregistrements de validation.
            await AnsiConsole.Status().StartAsync($"Session {options.Mode} pendant {duree} s…", async ctx =>
            {
                var fin = DateTime.UtcNow.AddSeconds(duree);
                while (DateTime.UtcNow < fin && !arret.IsCancellationRequested && !marche.FluxTermine)
                {
                    ctx.Status($"{options.Mode} : {marche.MessagesRecus} ticks reçus, {(fin - DateTime.UtcNow).TotalSeconds:0} s restantes");
                    await Task.Delay(1000);
                }
            });
            AnsiConsole.MarkupLine($"[green]Session terminée : {marche.MessagesRecus} ticks.[/]");
            return 0;
        }

        if (demo)
        {
            var scenario = services.GetRequiredService<ScenarioDemo>();
            scenario.DossierLatex = dossierLatex;
            var resultat = await scenario.ExecuterAsync(arret.Token);
            AnsiConsole.MarkupLine($"[bold green]Démonstration terminée :[/] {resultat.Trades} trades, {resultat.Fichiers.Count} fichiers exportés.");
            return 0;
        }

        await services.GetRequiredService<Application>().ExecuterAsync(arret.Token);
        return 0;
    }

    /// <summary>
    /// On combine la configuration JSON et les arguments de commande, puis on relie les services qui
    /// partagent le marché et le book.
    /// </summary>
    /// <param name="options">On renvoie aussi les options résolues pour accéder aux chemins et au mode réellement utilisés.</param>
    /// <returns>On renvoie le conteneur à libérer en fin de session.</returns>
    public static ServiceProvider ConstruireServices(string[] args, out OptionsPricer options)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddCommandLine(args, Raccourcis)
            .Build();

        var services = new ServiceCollection();
        services.Configure<OptionsPricer>(configuration.GetSection(OptionsPricer.Section));
        services.AddLogging(b =>
        {
            var niveau = Enum.TryParse<LogLevel>(configuration["Logging:NiveauMinimal"], out var n) ? n : LogLevel.Information;
            b.SetMinimumLevel(niveau);
        });
        // On laisse le conteneur fermer le journal à la fin de chaque session.
        services.AddSingleton<ILoggerProvider>(_ => new JournalFichierProvider(
            configuration["Logging:Fichier"] ?? "logs/pricer.log",
            Enum.TryParse<LogLevel>(configuration["Logging:NiveauMinimal"], out var niveau) ? niveau : LogLevel.Information));

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<OptionsPricer>>().Value.ResoudreChemins());
        services.AddSingleton<IHorloge>(_ => HorlogeMarche.Instance);
        services.AddSingleton(sp => FabriqueSourceDonnees.Creer(sp.GetRequiredService<OptionsPricer>(), sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<ServiceMarche>();
        services.AddSingleton<Blotter>();
        services.AddSingleton<Book>();
        services.AddSingleton<Application>();
        services.AddSingleton<ScenarioDemo>();

        var fournisseur = services.BuildServiceProvider();
        options = fournisseur.GetRequiredService<OptionsPricer>();
        return fournisseur;
    }
}
