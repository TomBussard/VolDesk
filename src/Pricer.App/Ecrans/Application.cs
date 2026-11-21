using System.Globalization;
using Microsoft.Extensions.Logging;
using Pricer.Domaine.Booking;
using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Utilitaires;
using Pricer.Infrastructure.Configuration;
using Pricer.Infrastructure.Exports;
using Pricer.Infrastructure.Services;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pricer.App.Ecrans;

public enum ChoixMenu
{
    Referentiel,
    Pricer,
    SurMesure,
    Book,
    Blotter,
    Exports,
    Quitter
}

/// <summary>
/// On organise la console autour d'un menu principal et d'écrans qui suivent les données. On utilise une
/// énumération pour relier chaque choix à l'action correspondante.
/// </summary>
/// <param name="marche">On utilise ce service pour suivre les données et gérer les règlements.</param>
/// <param name="blotter">On enregistre ici les ouvertures et les fermetures demandées dans les écrans.</param>
/// <param name="book">On valorise les positions reconstruites à partir du même blotter.</param>
/// <param name="options">On reprend les chemins de sortie et le mode de la session.</param>
/// <param name="log">On conserve les erreurs métier utiles au diagnostic de la console.</param>
public sealed class Application(ServiceMarche marche, Blotter blotter, Book book, OptionsPricer options, ILogger<Application> log)
{
    private static readonly IReadOnlyDictionary<ChoixMenu, string> Libelles = new Dictionary<ChoixMenu, string>
    {
        [ChoixMenu.Referentiel] = "1. Référentiel des options",
        [ChoixMenu.Pricer] = "2. Pricer de structures (live) → profil → booking",
        [ChoixMenu.SurMesure] = "3. Option sur mesure + couverture",
        [ChoixMenu.Book] = "4. Suivi du book (live)",
        [ChoixMenu.Blotter] = "5. Blotter et débouclage",
        [ChoixMenu.Exports] = "6. Exports CSV / Excel / JSON",
        [ChoixMenu.Quitter] = "7. Quitter"
    };

    private EtatMarche Etat => marche.EtatCourant;

    /// <summary>
    /// On affiche le menu, on exécute l’écran choisi et on présente les erreurs métier sans arrêter
    /// toute la console. On sauvegarde le blotter à la sortie.
    /// </summary>
    public async Task ExecuterAsync(CancellationToken ct)
    {
        var choix = ChoixMenu.Referentiel;
        do
        {
            AnsiConsole.Write(new Rule($"[bold yellow]Pricer Deribit[/] — mode [bold]{options.Mode}[/] — {Etat.Referentiel.NombreOptions} options — {marche.MessagesRecus} ticks reçus — {Etat.Maintenant:dd/MM HH:mm:ss} UTC").LeftJustified());
            choix = AnsiConsole.Prompt(new SelectionPrompt<ChoixMenu>().Title("Que voulez-vous faire ?").UseConverter(c => Libelles[c]).AddChoices(Enum.GetValues<ChoixMenu>()));
            try
            {
                switch (choix)
                {
                    case ChoixMenu.Referentiel: EcranReferentiel(); break;
                    case ChoixMenu.Pricer: await EcranPricerAsync(ct); break;
                    case ChoixMenu.SurMesure: await EcranSurMesureAsync(ct); break;
                    case ChoixMenu.Book: await EcranBookAsync(ct); break;
                    case ChoixMenu.Blotter: await EcranBlotterAsync(ct); break;
                    case ChoixMenu.Exports: EcranExports(); break;
                    case ChoixMenu.Quitter: break;
                }
            }
            catch (SaisieInvalideException ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Saisie refusée (code {(int)ex.CodeErreur} {ex.CodeErreur}) :[/] {Markup.Escape(ex.Message)}");
            }
            catch (PricerException ex)
            {
                AnsiConsole.MarkupLine($"[bold red]{Markup.Escape(ex.GetType().Name)} :[/] {Markup.Escape(ex.Message)}");
                log.LogWarning(ex, "Erreur métier dans l'écran {Ecran}", choix);
            }
        } while (choix != ChoixMenu.Quitter);

        ExportJson.Sauvegarder(blotter.Trades, options.FichierBlotter);
        AnsiConsole.MarkupLine($"[grey]Blotter sauvegardé dans {Markup.Escape(options.FichierBlotter)}.[/]");
    }

    /// <summary>
    /// On propose seulement les échéances du référentiel chargé pour éviter une sélection sans
    /// instruments disponibles.
    /// </summary>
    /// <returns>On renvoie l’échéance sélectionnée par l’utilisateur.</returns>
    private DateTime ChoisirEcheance() =>
        AnsiConsole.Prompt(new SelectionPrompt<DateTime>().Title("Échéance :").PageSize(15)
            .UseConverter(e => $"{Rendu.Echeance(e)}  ({Maturite.EnAnnees(e, Etat.Maintenant) * 365:0.0} j)")
            .AddChoices(Etat.Referentiel.Echeances.Where(e => e > Etat.Maintenant)));

    /// <summary>
    /// On laisse choisir une échéance puis on affiche côte à côte ses calls et ses puts.
    /// </summary>
    private void EcranReferentiel()
    {
        var e = ChoisirEcheance();
        AnsiConsole.Write(Rendu.Referentiel(Etat, e));
    }

    /// <summary>
    /// On recueille les paramètres de la structure, on suit ses instruments et on affiche sa cotation
    /// avant de proposer les scénarios et le booking.
    /// </summary>
    private async Task EcranPricerAsync(CancellationToken ct)
    {
        var type = AnsiConsole.Prompt(new SelectionPrompt<TypeStructure>().Title("Structure :")
            .UseConverter(t => FabriqueStructures.Catalogue.TryGetValue(t, out var r) ? r.Nom : t == TypeStructure.OptionSimple ? "Call ou put coté" : t.ToString())
            .AddChoices(FabriqueStructures.Catalogue.Keys.Append(TypeStructure.OptionSimple)));
        var echeance = ChoisirEcheance();
        var strikes = string.Join("  ", Etat.Referentiel.Strikes(echeance).Select(p => p.Strike.ToString("0", CultureInfo.InvariantCulture)));
        AnsiConsole.MarkupLine($"[grey]Strikes cotés : {strikes}[/]   future : {Rendu.Usd(Etat.Future(echeance))}");

        Structure structure;
        if (type == TypeStructure.OptionSimple)
        {
            var typeOption = AnsiConsole.Prompt(new SelectionPrompt<TypeOption>().Title("Call ou put ?").AddChoices(TypeOption.Call, TypeOption.Put));
            structure = FabriqueStructures.CreerOptionSimple(typeOption, echeance, Saisie.Nombre("Strike :"), Etat.Referentiel);
        }
        else
        {
            int n = FabriqueStructures.Catalogue[type].NombreStrikes;
            structure = FabriqueStructures.Creer(type, echeance, Saisie.Strikes($"{n} strike(s), séparés par des espaces :"), Etat.Referentiel);
        }
        var sens = AnsiConsole.Prompt(new SelectionPrompt<Sens>().Title("Sens :").AddChoices(Sens.Achat, Sens.Vente));
        double quantite = Saisie.Nombre("Quantité (multiple de 0,1 BTC) :", 1);
        ValidateurStructure.ValiderQuantite(quantite, Blotter.PasQuantite);

        await marche.SuivreAsync(structure.Jambes.OfType<ICotableSurMarche>().Select(j => j.Instrument.Nom), ct);
        await AfficherEnContinuAsync(() => Rendu.Cotation(PricerStructure.Coter(structure, sens, quantite, Etat)), ct);

        var cotation = PricerStructure.Coter(structure, sens, quantite, Etat);
        var action = AnsiConsole.Prompt(new SelectionPrompt<string>().AddChoices("Afficher le profil de P&L", "Booker au prix exécutable", "Retour"));
        if (action == "Afficher le profil de P&L")
        {
            AnsiConsole.Write(Rendu.Profil(ProfilPnL.Calculer(cotation, Etat)));
            if (!AnsiConsole.Confirm("Booker la structure au prix exécutable du moment ?", false)) return;
            cotation = PricerStructure.Coter(structure, sens, quantite, Etat);
        }
        else if (action == "Retour") return;

        var trade = blotter.Booker(cotation);
        AnsiConsole.MarkupLine($"[bold green]Trade {trade.Id} booké[/] — prime {Rendu.Btc(trade.PrimeBtc)} BTC.");
    }

    /// <summary>
    /// On saisit une option OTC, on calcule sa couverture avec la paire choisie ou recherchée
    /// automatiquement, puis on propose de traiter l’ensemble.
    /// </summary>
    private async Task EcranSurMesureAsync(CancellationToken ct)
    {
        var typeOption = AnsiConsole.Prompt(new SelectionPrompt<TypeOption>().Title("Call ou put sur mesure ?").AddChoices(TypeOption.Call, TypeOption.Put));
        double strike = Saisie.Nombre("Strike (libre) :");
        var echeance = Saisie.DateEcheance("Échéance (libre, ex. 2027-01-15) :");
        var structure = FabriqueStructures.CreerSurMesure(typeOption, echeance, strike, Etat);
        var jambe = (JambeSurMesure)structure.Jambes[0];
        var sensClient = AnsiConsole.Prompt(new SelectionPrompt<Sens>().Title("Le client :").UseConverter(s => s == Sens.Achat ? "achète l'option" : "vend l'option").AddChoices(Sens.Achat, Sens.Vente));
        double quantite = Saisie.Nombre("Quantité (multiple de 0,1) :", 10);
        ValidateurStructure.ValiderQuantite(quantite, Blotter.PasQuantite);

        PropositionCouverture proposition;
        if (AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Options de couverture :").AddChoices("Choix automatique (recommandé)", "Choisir la paire")) == "Choisir la paire")
        {
            // On fait choisir une option réellement cotée pour construire la paire de couverture demandée.
            Instrument ChoisirOption(string titre)
            {
                var e = ChoisirEcheance();
                double k = Saisie.Nombre($"{titre} — strike :");
                var t = AnsiConsole.Prompt(new SelectionPrompt<TypeOption>().AddChoices(TypeOption.Call, TypeOption.Put));
                return ValidateurStructure.ValiderStrikeCote(Etat.Referentiel, e, k, t);
            }
            var h1 = ChoisirOption("Option 1");
            var h2 = ChoisirOption("Option 2");
            await marche.SuivreAsync([h1.Nom, h2.Nom, Etat.Referentiel.FuturePourEcheance(echeance).Nom], ct);
            proposition = MoteurCouverture.Calculer(jambe, sensClient, quantite, h1, h2, Etat, options.MargeVolPoints);
            await AfficherEnContinuAsync(() => Rendu.Couverture(MoteurCouverture.Calculer(jambe, sensClient, quantite, h1, h2, Etat, options.MargeVolPoints)), ct);
            proposition = MoteurCouverture.Calculer(jambe, sensClient, quantite, h1, h2, Etat, options.MargeVolPoints);
        }
        else
        {
            proposition = MoteurCouverture.CalculerMeilleure(jambe, sensClient, quantite, Etat, options.MargeVolPoints);
            var couvrants = proposition.Lignes.Select(l => l.Nom).ToList();
            await marche.SuivreAsync(couvrants, ct);
            var h1 = Etat.Referentiel.Obtenir(couvrants[0]);
            var h2 = Etat.Referentiel.Obtenir(couvrants[1]);
            await AfficherEnContinuAsync(() => Rendu.Couverture(MoteurCouverture.Calculer(jambe, sensClient, quantite, h1, h2, Etat, options.MargeVolPoints)), ct);
            proposition = MoteurCouverture.Calculer(jambe, sensClient, quantite, h1, h2, Etat, options.MargeVolPoints);
        }

        if (AnsiConsole.Confirm($"Traiter avec le client à {Rendu.Btc(proposition.PrixClientBtc)} BTC et exécuter la couverture ?", false))
        {
            var t = blotter.BookerSurMesure(proposition, Etat.Maintenant);
            AnsiConsole.MarkupLine($"[bold green]Trade {t.Id} booké[/] (option sur mesure + {proposition.Lignes.Count} instruments de couverture).");
        }
    }

    /// <summary>
    /// On suit les instruments du book, on règle les jambes échues lorsque leur prix officiel est
    /// disponible et on actualise la valorisation affichée.
    /// </summary>
    private async Task EcranBookAsync(CancellationToken ct)
    {
        await marche.SuivreAsync(book.InstrumentsSuivis(), ct);
        await AfficherEnContinuAsync(() =>
        {
            var regles = marche.ReglerEcheancesAsync(book, ct).GetAwaiter().GetResult();
            foreach (var r in regles) log.LogInformation("Règlement automatique : {Description}", r.Description);
            return Rendu.Book(book.Evaluer(Etat));
        }, ct);
    }

    /// <summary>
    /// On affiche les opérations puis on permet de déboucler une quantité d’une ouverture encore active
    /// aux prix courants.
    /// </summary>
    private async Task EcranBlotterAsync(CancellationToken ct)
    {
        AnsiConsole.Write(Rendu.Blotter(blotter.Trades));
        var ouverts = blotter.Ouvertures.Where(o => blotter.QuantiteOuverte(o.Id) > 0).ToList();
        if (ouverts.Count == 0 || !AnsiConsole.Confirm("Déboucler un trade ?", false)) return;

        var trade = AnsiConsole.Prompt(new SelectionPrompt<Trade>().Title("Trade à déboucler :")
            .UseConverter(t => $"{t.Id.ToString()[..8]}  {Markup.Escape(t.Description)}  ouvert {blotter.QuantiteOuverte(t.Id):0.0}/{t.Quantite:0.0}")
            .AddChoices(ouverts));
        double quantite = Saisie.Nombre("Quantité à déboucler :", blotter.QuantiteOuverte(trade.Id));
        await marche.SuivreAsync(trade.Jambes.Where(j => j.Nature != NatureJambe.OptionSurMesure).Select(j => j.Instrument), ct);
        var d = blotter.Deboucler(trade.Id, quantite, Etat);
        AnsiConsole.MarkupLine($"[bold green]Débouclage {d.Id} booké[/], lié au trade {trade.Id} — prime {Rendu.Btc(d.PrimeBtc)} BTC.");
    }

    /// <summary>
    /// On évalue une fois le book puis on exporte cet état et le blotter dans chacun des formats
    /// disponibles.
    /// </summary>
    private void EcranExports()
    {
        var etat = book.Evaluer(Etat);
        foreach (var exportateur in FabriqueExportateurs.Tous())
            foreach (var fichier in exportateur.Exporter(blotter.Trades, etat, options.DossierExports))
                AnsiConsole.MarkupLine($"[green]{exportateur.Format}[/] → {Markup.Escape(Path.GetFullPath(fichier))}");
    }

    /// <summary>
    /// On actualise l'écran toutes les 500 ms jusqu'à une touche de l'utilisateur ou une demande
    /// d'annulation. On conserve le dernier affichage en quittant le suivi.
    /// </summary>
    private static async Task AfficherEnContinuAsync(Func<IRenderable> construire, CancellationToken ct)
    {
        // On reconstruit l’écran et on affiche une erreur métier dans la zone de suivi si le calcul échoue.
        IRenderable Ecran()
        {
            try { return new Rows(construire(), new Markup("[grey]Mise à jour en continu — appuyez sur une touche pour continuer.[/]")); }
            catch (PricerException ex) { return new Markup($"[red]{Markup.Escape(ex.Message)}[/]"); }
        }

        await AnsiConsole.Live(Ecran()).AutoClear(false).StartAsync(async ctx =>
        {
            while (!ct.IsCancellationRequested)
            {
                if (!Console.IsInputRedirected && Console.KeyAvailable) { Console.ReadKey(true); break; }
                if (Console.IsInputRedirected) break;
                ctx.UpdateTarget(Ecran());
                await Task.Delay(500, ct);
            }
        });
    }
}
