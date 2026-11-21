using Microsoft.Extensions.Options;
using Pricer.Domaine.Abstractions;
using Pricer.Domaine.Booking;
using Pricer.Domaine.Marche;
using Pricer.Infrastructure.Configuration;
using Pricer.Infrastructure.Services;
using Pricer.Web.Components;
using Pricer.Web.Services;

namespace Pricer.Web;

/// <summary>
/// On relie l'interface Blazor aux mêmes services métier et sources de données que la console. On garde
/// ainsi un seul moteur de calcul pour les deux interfaces et pour tous les modes.
/// </summary>
public class Program
{
    private static readonly Dictionary<string, string> Raccourcis = new()
    {
        ["--mode"] = "Pricer:Mode",
        ["--fichier"] = "Pricer:FichierRejeu",
        ["--enregistrement"] = "Pricer:FichierEnregistrement",
        ["--vitesse"] = "Pricer:VitesseRejeu",
        ["--exports"] = "Pricer:DossierExports",
        ["--port"] = "Urls"
        ,
        ["--devise"] = "Pricer:Devise"
    };

    /// <summary>
    /// On construit le serveur puis on le laisse traiter les requêtes jusqu'à son arrêt.
    /// </summary>
    public static void Main(string[] args) => CreerApplication(args).Run();

    /// <summary>
    /// On configure les services partagés, les routes et les composants Blazor avant de démarrer le
    /// serveur.
    /// </summary>
    public static WebApplication CreerApplication(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddCommandLine(args, Raccourcis);
        if (builder.Configuration["Urls"] is string port && int.TryParse(port, out int p))
            builder.WebHost.UseUrls($"http://localhost:{p}");

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        // On injecte les mêmes services que dans la console pour partager le moteur métier et la gestion des données.
        builder.Services.Configure<OptionsPricer>(builder.Configuration.GetSection(OptionsPricer.Section));
        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OptionsPricer>>().Value.ResoudreChemins();
            bool explicite = args.Any(a => a is "--mode" or "--fichier" or "--devise" || a.StartsWith("--Pricer:", StringComparison.OrdinalIgnoreCase));
            ServiceSession.ChargerAvantDemarrage(options, !explicite);
            return options;
        });
        builder.Services.AddSingleton<IHorloge>(_ => HorlogeMarche.Instance);
        builder.Services.AddSingleton(sp => new ServiceMarche(
            new SourceMarcheDifferee(() => FabriqueSourceDonnees.Creer(sp.GetRequiredService<OptionsPricer>(), sp.GetRequiredService<ILoggerFactory>())),
            sp.GetRequiredService<OptionsPricer>(), sp.GetRequiredService<IHorloge>(),
            sp.GetRequiredService<ILogger<ServiceMarche>>(), possedeSource: true));
        builder.Services.AddSingleton<Blotter>();
        builder.Services.AddSingleton<Book>();
        builder.Services.AddSingleton<EtatApplication>();
        builder.Services.AddSingleton<ServiceSession>();
        builder.Services.AddHostedService<DemarrageMarche>();

        var app = builder.Build();
        if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapGet("/sante", (EtatApplication etat) => Results.Json(new { application = "DeribitDesk-Fusion", pret = etat.Pret, erreur = etat.Erreur }));

        // On sert uniquement un fichier du dossier d'exports pour que le lien de téléchargement corresponde à un résultat généré par l'application.
        app.MapGet("/telecharger/{nom}", (string nom, OptionsPricer options) =>
        {
            string chemin = Path.Combine(Path.GetFullPath(options.DossierExports), Path.GetFileName(nom));
            return File.Exists(chemin) ? Results.File(chemin, "application/octet-stream", Path.GetFileName(chemin)) : Results.NotFound();
        });

        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        return app;
    }
}
