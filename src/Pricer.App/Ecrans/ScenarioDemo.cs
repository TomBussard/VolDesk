using Pricer.Domaine.Booking;
using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Infrastructure.Configuration;
using Pricer.Infrastructure.Exports;
using Pricer.Infrastructure.Services;
using Spectre.Console;

namespace Pricer.App.Ecrans;

/// <summary>
/// On conserve le nombre de trades, les exports et le book obtenus afin que le test de bout en bout puisse
/// vérifier le scénario.
/// </summary>
public sealed record ResultatDemo(int Trades, IReadOnlyList<string> Fichiers, int ErreursDeSaisieDetectees, EtatBook Book);

/// <summary>
/// On déroule un parcours complet sans saisie : référentiel, structures proposées, contrôles, profils,
/// OTC couvert, booking, débouclage et exports. On utilise le même scénario pour la démonstration et pour
/// vérifier l'application de bout en bout.
/// </summary>
/// <param name="marche">On alimente le scénario avec la source choisie pour la session.</param>
/// <param name="blotter">On conserve les opérations produites par la démonstration.</param>
/// <param name="book">On évalue les opérations et leurs risques au fil du scénario.</param>
/// <param name="options">On utilise les réglages de la session, notamment son dossier d’exports.</param>
public sealed class ScenarioDemo(ServiceMarche marche, Blotter blotter, Book book, OptionsPricer options)
{
    /// <summary>On peut renseigner ce dossier pour écrire les exemples chiffrés du scénario dans exemples.tex.</summary>
    public string? DossierLatex { get; set; }

    /// <summary>
    /// On déroule la démonstration avec le même moteur que la console : cotations, contrôles, booking,
    /// débouclage et exports.
    /// </summary>
    /// <returns>On renvoie les opérations, les fichiers générés, les contrôles de saisie et l’état final du book.</returns>
    public async Task<ResultatDemo> ExecuterAsync(CancellationToken ct)
    {
        var latex = new System.Text.StringBuilder();
        var etat = marche.EtatCourant;
        // On attend quelques cotations pour commencer le scénario avec un marché déjà alimenté.
        for (int i = 0; i < 20 && marche.MessagesRecus < 50 && !marche.FluxTermine; i++) await Task.Delay(100, ct);

        var echeances = etat.Referentiel.Echeances.Where(e => (e - etat.Maintenant).TotalDays > 1.5).ToList();
        // On privilégie une échéance avec des tickers complets pour pouvoir montrer la comparaison des grecques avec Deribit.
        bool TickersComplets(DateTime e) => etat.Referentiel.Strikes(e).Any(p => p.Call is not null && etat.Cotation(p.Call.Nom)?.GrecquesDeribit is not null);
        var echeance = echeances.FirstOrDefault(e => etat.Smiles().Any(s => s.Echeance == e) && TickersComplets(e));
        if (echeance == default) echeance = echeances.First(e => etat.Smiles().Any(s => s.Echeance == e));
        double f = etat.Future(echeance) ?? throw new DonneesIndisponiblesException("Pas de future pour l'échéance de démonstration.");
        var strikes = etat.Referentiel.Strikes(echeance).Select(p => p.Strike).ToList();
        // On choisit l’instrument disponible le plus proche du strike recherché.
        double Proche(double cible) => strikes.OrderBy(k => Math.Abs(k - cible)).First();

        Titre("1. Référentiel");
        AnsiConsole.Write(Rendu.Referentiel(etat, echeance));

        // On choisit des strikes cotés autour du future et des ailes symétriques pour construire un butterfly cohérent.
        double centre = Proche(f);
        double aile = strikes.Where(k => k > centre).Select(k => k - centre).Where(d => strikes.Contains(centre - d)).OrderBy(d => d).Skip(1).FirstOrDefault();
        if (aile == 0) aile = strikes.Where(k => k > centre).Select(k => k - centre).Where(d => strikes.Contains(centre - d)).Min();

        var demandes = new (TypeStructure Type, double[] Strikes, Sens Sens)[]
        {
            (TypeStructure.CallSpread, [centre, centre + aile], Sens.Achat),
            (TypeStructure.PutSpread, [centre - aile, centre], Sens.Achat),
            (TypeStructure.ButterflyCall, [centre - aile, centre, centre + aile], Sens.Achat),
            (TypeStructure.ButterflyPut, [centre - aile, centre, centre + aile], Sens.Vente)
        };

        Titre("2. Saisies incohérentes refusées");
        int erreurs = 0;
        foreach (var essai in new Func<object>[]
        {
            () => FabriqueStructures.Creer(TypeStructure.CallSpread, echeance, [centre + aile, centre], etat.Referentiel),
            () => FabriqueStructures.Creer(TypeStructure.ButterflyCall, echeance, [centre - aile, centre, centre + 2 * aile], etat.Referentiel),
            () => FabriqueStructures.Creer(TypeStructure.CallSpread, echeance, [centre + 1, centre + aile], etat.Referentiel),
            () => { ValidateurStructure.ValiderQuantite(0.15, Blotter.PasQuantite); return 0; },
            () => FabriqueStructures.CreerSurMesure(TypeOption.Call, etat.Referentiel.Echeances[^1].AddDays(30), f, etat)
        })
        {
            try { essai(); }
            catch (PricerException ex)
            {
                erreurs++;
                AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.GetType().Name)}[/] : {Markup.Escape(ex.Message)}");
            }
        }

        Titre("3. Pricer : structures obligatoires");
        var trades = new List<Trade>();
        foreach (var (type, k, sens) in demandes)
        {
            var structure = FabriqueStructures.Creer(type, echeance, k, etat.Referentiel);
            await marche.SuivreAsync(structure.Jambes.OfType<ICotableSurMarche>().Select(j => j.Instrument.Nom), ct);
            var cotation = PricerStructure.Coter(structure, sens, 1, etat);
            AnsiConsole.Write(Rendu.Cotation(cotation));
            if (type == TypeStructure.CallSpread)
            {
                Titre("4. Profil de P&L du call spread");
                var profil = ProfilPnL.Calculer(cotation, etat);
                AnsiConsole.Write(Rendu.Profil(profil));
                latex.AppendLine(Analyse.ExportLatex.Cotation(cotation)).AppendLine(Analyse.ExportLatex.Profil(profil));
            }
            if (cotation.Traitable) trades.Add(blotter.Booker(cotation));
            else AnsiConsole.MarkupLine("[yellow]Structure non traitable : non bookée.[/]");
        }

        Titre("5. Option sur mesure et couverture");
        try
        {
            // On place l'OTC entre deux échéances suffisamment espacées pour illustrer l'interpolation en temps.
            var cotees = etat.Smiles().Select(s => s.Echeance).Where(e => e >= echeance).OrderBy(e => e).ToList();
            var paire = cotees.Zip(cotees.Skip(1)).FirstOrDefault(p => (p.Second - p.First).TotalDays >= 3);
            if (paire != default)
            {
                var echeanceOtc = (paire.First + (paire.Second - paire.First) / 2).Date.AddHours(8);
                var jambe = (JambeSurMesure)FabriqueStructures.CreerSurMesure(TypeOption.Call, echeanceOtc, Math.Round(f * 1.05 / 100) * 100, etat).Jambes[0];
                var proposition = MoteurCouverture.CalculerMeilleure(jambe, Sens.Achat, 10, etat, options.MargeVolPoints);
                await marche.SuivreAsync(proposition.Lignes.Select(l => l.Nom), ct);
                AnsiConsole.Write(Rendu.Couverture(proposition));
                latex.AppendLine(Analyse.ExportLatex.Couverture(proposition));
                trades.Add(blotter.BookerSurMesure(proposition, etat.Maintenant));
            }
        }
        catch (PricerException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Option sur mesure non traitée : {Markup.Escape(ex.Message)}[/]");
        }

        Titre("6. Débouclage partiel");
        if (trades.Count > 0)
        {
            var d = blotter.Deboucler(trades[0].Id, 0.5, etat);
            AnsiConsole.MarkupLine($"Débouclage {d.Id.ToString()[..8]} lié à {trades[0].Id.ToString()[..8]}, prime {Rendu.Btc(d.PrimeBtc)} BTC.");
        }
        AnsiConsole.Write(Rendu.Blotter(blotter.Trades));

        Titre("7. Book");
        // On laisse arriver de nouvelles données avant de valoriser le book pour montrer son suivi dans le temps.
        for (int i = 0; i < 10 && !marche.FluxTermine; i++) await Task.Delay(200, ct);
        var etatBook = book.Evaluer(etat);
        AnsiConsole.Write(Rendu.Book(etatBook));

        latex.AppendLine(Analyse.ExportLatex.Book(etatBook));
        if (DossierLatex is not null)
        {
            Directory.CreateDirectory(DossierLatex);
            File.WriteAllText(Path.Combine(DossierLatex, "exemples.tex"), latex.ToString());
        }

        Titre("8. Exports");
        var fichiers = new List<string>();
        foreach (var exportateur in FabriqueExportateurs.Tous())
            foreach (var fichier in exportateur.Exporter(blotter.Trades, etatBook, options.DossierExports))
            {
                fichiers.Add(fichier);
                AnsiConsole.MarkupLine($"[green]{exportateur.Format}[/] → {Markup.Escape(fichier)}");
            }

        return new ResultatDemo(blotter.Trades.Count, fichiers, erreurs, etatBook);
    }

    /// <summary>
    /// On sépare les étapes du scénario par un titre pour rendre la sortie de démonstration plus facile
    /// à parcourir.
    /// </summary>
    private static void Titre(string texte) => AnsiConsole.Write(new Rule($"[bold yellow]{texte}[/]").LeftJustified());
}
