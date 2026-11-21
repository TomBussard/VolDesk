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
/// The console services are prepared in the same place: the configuration is read, the log is
/// opened and the dependencies are connected, so the screens are kept separate from the data
/// source choice.
/// </summary>
public static class Programme
{
    /// <summary>
    /// Short arguments are associated with the application settings, e.g. --Replay mode --file
    /// x.jsonl --speed 10.
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
    /// We select the route requested in the command line, initialize the market and launch the
    /// console, demonstration or export of statistics.
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
            // The accelerated reading is completed before comparing the latest available data to the Deribit fields.
            while (!marche.FluxTermine && !arret.IsCancellationRequested) await Task.Delay(200);
            var lignes = Analyse.AnalyseMarche.Valider(marche.EtatCourant);
            Analyse.AnalyseMarche.Ecrire(lignes, dossierStatistiques);
            AnsiConsole.MarkupLine($"[green]{lignes.Count} options validées → {Markup.Escape(dossierStatistiques)}[/]");
            return 0;
        }

        if (dureeSecondes is int duree)
        {
            // The duration of a capture can be limited without opening menus, which facilitates validation records.
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
    /// The JSON configuration is combined with the command arguments, and then the services
    /// that share the market and the book are linked.
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
        // We let the container close the log at the end of each session.
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
