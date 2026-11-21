using System.Globalization;
using Pricer.Domaine.Booking;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Utilitaires;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pricer.App.Ecrans;

/// <summary>
/// On présente les résultats du domaine avec Spectre.Console. On garde les calculs dans les services
/// métier pour que l'affichage ne change pas les résultats.
/// </summary>
public static class Rendu
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// On formate un nombre avec la précision demandée et on affiche un tiret si sa valeur est
    /// absente ou non finie.
    /// </summary>
    /// <param name="x">On reçoit la valeur à afficher ; son unité est indiquée dans le tableau appelant.</param>
    /// <param name="format">On utilise ce format numérique pour choisir la précision affichée.</param>
    public static string Btc(double? x, string format = "0.000000") => x is double v && double.IsFinite(v) ? v.ToString(format, Fr) : "[grey]—[/]";
    /// <summary>
    /// On affiche une valeur USD avec deux décimales et des séparateurs de milliers.
    /// </summary>
    public static string Usd(double? x) => Btc(x, "#,##0.00");
    /// <summary>
    /// On réutilise le formatage commun pour les quantités et les sensibilités de la console.
    /// </summary>
    public static string Nombre(double x, string format) => Btc(x, format);
    /// <summary>
    /// On garde le même format de date et d’heure pour identifier les échéances dans tous les tableaux.
    /// </summary>
    public static string Echeance(DateTime e) => e.ToString("ddMMMyy HH:mm", CultureInfo.InvariantCulture).ToUpperInvariant();

    /// <summary>
    /// On arrondit le nombre avant de l'afficher avec son signe pour éviter un zéro négatif qui pourrait
    /// être mal interprété.
    /// </summary>
    public static string AvecSigne(double v, int decimales)
    {
        if (!double.IsFinite(v)) return "[grey]—[/]";
        double arrondi = Math.Round(v, decimales);
        if (arrondi == 0) arrondi = 0; // On évite d'afficher un signe négatif sur une valeur arrondie à zéro.
        string f = "0." + new string('0', decimales);
        return arrondi.ToString("+" + f + ";-" + f + ";" + f, Fr);
    }

    /// <summary>
    /// On affiche un écart signé et on le met en évidence lorsque le contrôle a déclenché une alerte.
    /// </summary>
    public static string Signe(double? x, string format, bool alerte)
    {
        if (x is not double v) return "[grey]—[/]";
        string texte = AvecSigne(v, format.Length - 2);
        return alerte ? $"[bold red]{texte} ⚠[/]" : $"[green]{texte}[/]";
    }

    /// <summary>
    /// On rapproche notre volatilité implicite de celle de Deribit et on signale leur écart, ou la
    /// raison d’un calcul indisponible.
    /// </summary>
    public static string Vol(ComparaisonVol? c)
    {
        if (c is null) return "[grey]—[/]";
        string notre = c.Calculee.EstCalculee ? (c.Calculee.Sigma!.Value * 100).ToString("0.00", Fr) : $"[yellow]{c.Calculee.Libelle}[/]";
        string deribit = c.Deribit is double d ? d.ToString("0.00", Fr) : "—";
        string ecart = c.EcartPoints is double e ? (c.Signale ? $"[bold red]{AvecSigne(e, 2)}⚠[/]" : $"[green]{AvecSigne(e, 2)}[/]") : "";
        return $"{notre} [grey]/ {deribit}[/] {ecart}";
    }

    /// <summary>
    /// On rassemble les cotations call et put d’une échéance par strike et on repère les strikes proches
    /// du future.
    /// </summary>
    public static Table Referentiel(EtatMarche etat, DateTime echeance)
    {
        double? f = etat.Future(echeance);
        var t = new Table().Border(TableBorder.Rounded).Expand()
            .Title($"[bold]{etat.Referentiel.Devise} — échéance {Echeance(echeance)}[/]  future {Usd(f)}  T = {Nombre(Maturite.EnAnnees(echeance, etat.Maintenant) * 365, "0.0")} j")
            .AddColumns("[blue]Bid C[/]", "[blue]Ask C[/]", "[blue]Mark C[/]", "[blue]IV C[/]", "[bold]Strike[/]", "[purple]Bid P[/]", "[purple]Ask P[/]", "[purple]Mark P[/]", "[purple]IV P[/]");
        foreach (var p in etat.Referentiel.Strikes(echeance))
        {
            var c = p.Call is null ? null : etat.Cotation(p.Call.Nom);
            var q = p.Put is null ? null : etat.Cotation(p.Put.Nom);
            bool atm = f is double ff && Math.Abs(p.Strike - ff) < 0.02 * ff;
            t.AddRow(Btc(c?.Bid, "0.0000"), Btc(c?.Ask, "0.0000"), Btc(c?.Mark, "0.0000"), c?.MarkIv?.ToString("0.0", Fr) ?? "—",
                atm ? $"[bold yellow]{Nombre(p.Strike, "#,##0")}[/]" : Nombre(p.Strike, "#,##0"),
                Btc(q?.Bid, "0.0000"), Btc(q?.Ask, "0.0000"), Btc(q?.Mark, "0.0000"), q?.MarkIv?.ToString("0.0", Fr) ?? "—");
        }
        return t;
    }

    /// <summary>
    /// On assemble les tableaux de jambes, de prix et de grecques sans refaire les calculs métier.
    /// </summary>
    public static IRenderable Cotation(CotationStructure c)
    {
        var jambes = new Table().Border(TableBorder.Rounded).Expand()
            .Title($"[bold]{Markup.Escape(c.Structure.Description)}[/] — {(c.Sens == Sens.Achat ? "[green]ACHAT[/]" : "[red]VENTE[/]")} × {Nombre(c.Quantite, "0.0")}   [grey]{c.Horodatage:HH:mm:ss.fff} UTC[/]")
            .AddColumns("Jambe", "Qté", "F", "σ mark", "Théo USD", "Théo BTC", "Mark BTC", "Écart BTC", "Vol bid (nous / Deribit)", "Vol mark", "Vol ask", "Bid", "Ask");
        foreach (var l in c.Lignes)
            jambes.AddRow(
                Markup.Escape(l.Jambe.Libelle), Nombre(l.QuantiteSignee, "+0.0;-0.0"), Usd(l.Parametres.Future), (l.Parametres.Sigma * 100).ToString("0.00", Fr),
                Usd(l.Theorique.PrixUsd), Btc(l.Theorique.PrixBtc), Btc(l.Cotation?.Mark), Signe(l.EcartMarkBtc, "0.000000", l.EcartMarkSignale),
                Vol(l.VolBid), Vol(l.VolMark), Vol(l.VolAsk), Btc(l.Cotation?.Bid, "0.0000"), Btc(l.Cotation?.Ask, "0.0000"));

        var prix = new Table().Border(TableBorder.Simple).AddColumns("Prix (par unité)", "BTC", "USD");
        double f = c.Future;
        prix.AddRow("Théorique (Black-76)", Btc(c.TheoriqueUnitaireBtc), Usd(c.TheoriqueUnitaireUsd));
        prix.AddRow("Mark Deribit", Btc(c.MarkUnitaireBtc), Usd(c.MarkUnitaireBtc * f));
        if (c.Traitable)
        {
            prix.AddRow("[green]Exécutable à l'achat[/]", Btc(c.PrixExecutableAchatBtc), Usd(c.PrixExecutableAchatBtc * f));
            prix.AddRow("[red]Exécutable à la vente[/]", Btc(c.PrixExecutableVenteBtc), Usd(c.PrixExecutableVenteBtc * f));
            prix.AddRow("Fourchette", Btc(c.FourchetteBtc), Usd(c.FourchetteBtc * f));
            prix.AddRow($"[bold]Prime totale ({c.Sens})[/]", $"[bold]{Btc(c.PrimeTotaleBtc)}[/]", $"[bold]{Usd(c.PrimeTotaleBtc * f)}[/]");
        }
        else
        {
            prix.AddRow("[bold red]NON TRAITABLE[/]", "[red]une jambe sans bid ou ask[/]", "");
        }

        var g = c.Grecques;
        var gd = c.GrecquesDeribit;
        var grecques = new Table().Border(TableBorder.Simple).AddColumns("Grecques", "Black-76 (nous)", "Deribit", "Écart");
        // On conserve un format commun pour chaque ligne du tableau de sensibilités.
        void Ligne(string nom, double nous, double? deribit, string format) =>
            grecques.AddRow(nom, Nombre(nous, format), deribit is double d ? Nombre(d, format) : "—", deribit is double dd ? Nombre(nous - dd, format) : "—");
        Ligne("Delta", g.Delta, gd?.Delta, "0.0000");
        Ligne("Gamma", g.Gamma, gd?.Gamma, "0.000000");
        Ligne("Vega $/pt", g.Vega, gd?.Vega, "0.00");
        Ligne("Theta $/j", g.Theta, gd?.Theta, "0.00");

        return new Rows(jambes, new Columns(prix, grecques));
    }

    /// <summary>
    /// On présente les scénarios de résultat dans un tableau et un graphique, puis on ajoute les bornes
    /// et les points morts calculés.
    /// </summary>
    public static IRenderable Profil(ProfilStructure p)
    {
        var t = new Table().Border(TableBorder.Rounded).Title("[bold]Profil de P&L[/]")
            .AddColumns("Var.", "Sous-jacent", "Échéance USD", "Échéance BTC", "Aujourd'hui USD", "Aujourd'hui BTC");
        foreach (var x in p.Points)
            t.AddRow(x.Variation.EnPourcentage(), Usd(x.SousJacent), Couleur(x.PnlEcheanceUsd, Usd(x.PnlEcheanceUsd)), Couleur(x.PnlEcheanceBtc, Btc(x.PnlEcheanceBtc)),
                Couleur(x.PnlAujourdhuiUsd, Usd(x.PnlAujourdhuiUsd)), Couleur(x.PnlAujourdhuiBtc, Btc(x.PnlAujourdhuiBtc)));

        // On trace la valeur absolue du résultat car BarChart attend une longueur positive ; la couleur et l'étiquette conservent son signe.
        var graphe = new BarChart().Width(70).Label("[bold]P&L à l'échéance (USD) — vert gain, rouge perte[/]");
        foreach (var x in p.Points)
            graphe.AddItem($"{x.Variation.EnPourcentage()} {(x.PnlEcheanceUsd >= 0 ? "+" : "−")}", Math.Round(Math.Abs(x.PnlEcheanceUsd), 0), x.PnlEcheanceUsd >= 0 ? Color.Green : Color.Red);

        var resume = new Markup(
            $"Prime : {Btc(p.PrimeBtc)} BTC ({Usd(p.PrimeUsd)} $)   " +
            $"Gain max : {(p.GainMaxUsd is double gm ? Usd(gm) + " $" : "[green]illimité[/]")}   " +
            $"Perte max : {(p.PerteMaxUsd is double pm ? Usd(pm) + " $" : "[red]illimitée[/]")}   " +
            $"Point(s) mort(s) : {(p.PointsMorts.Count == 0 ? "aucun" : string.Join(" ; ", p.PointsMorts.Select(x => Usd(x))))}");
        return new Rows(t, graphe, resume);
    }

    /// <summary>
    /// On affiche les paramètres interpolés, le prix client, les couvertures arrondies et les risques
    /// globaux de la proposition OTC.
    /// </summary>
    public static IRenderable Couverture(PropositionCouverture p)
    {
        var i = p.Interpolation;
        var interp = new Table().Border(TableBorder.Simple).Title($"[bold]{Markup.Escape(p.Option.Libelle)}[/] — client {(p.SensClient == Sens.Achat ? "acheteur" : "vendeur")} de {Nombre(p.Quantite, "0.0")}")
            .AddColumns("Interpolation", "Valeur");
        interp.AddRow("Échéances encadrantes", i.EcheanceCotee ? Echeance(i.Echeance1.Echeance) + " (cotée)" : $"{Echeance(i.Echeance1.Echeance)} → {Echeance(i.Echeance2!.Echeance)}");
        interp.AddRow("Future interpolé", Usd(i.Future));
        interp.AddRow("Log-moneyness k", Nombre(i.LogMoneyness, "0.0000"));
        interp.AddRow("σ échéance 1 / 2", $"{Nombre(i.Sigma1 * 100, "0.00")} %" + (i.Sigma2 is double s2 ? $" / {Nombre(s2 * 100, "0.00")} %" : ""));
        interp.AddRow("[bold]σ interpolée (variance totale)[/]", $"[bold]{Nombre(i.Sigma * 100, "0.00")} %[/]");
        interp.AddRow("Prix théorique", $"{Btc(p.ValeurOption.PrixBtc)} BTC = {Usd(p.ValeurOption.PrixUsd)} $");
        interp.AddRow("[bold yellow]Prix proposé au client[/]", $"[bold yellow]{Btc(p.PrixClientBtc)} BTC = {Usd(p.PrixClientUsd)} $[/]");

        var lignes = new Table().Border(TableBorder.Rounded).Title($"[bold]Couverture[/]  det. normalisé {Nombre(p.DeterminantNormalise, "0.000")}")
            .AddColumns("Instrument", "Qté brute", "Qté arrondie", "Exécutable", "Mid", "Coût BTC");
        foreach (var l in p.Lignes)
            lignes.AddRow(Markup.Escape(l.Nom), Nombre(l.QuantiteAvantArrondi, "+0.000;-0.000"), Nombre(l.Quantite, "+0.000;-0.000"),
                l.Nature == NatureJambe.Future ? Usd(l.PrixExecutable) + " $" : Btc(l.PrixExecutable, "0.0000"),
                l.Nature == NatureJambe.Future ? Usd(l.Mid) + " $" : Btc(l.Mid, "0.00000"), Btc(l.CoutBtc));
        lignes.AddRow("[bold]Total coût de couverture[/]", "", "", "", "", $"[bold]{Btc(p.CoutCouvertureBtc)}[/]");

        var g = new Table().Border(TableBorder.Simple).AddColumns("Grecques", "Delta", "Gamma", "Vega $/pt", "Theta $/j");
        // On conserve un format commun pour chaque ligne du tableau de sensibilités.
        void Ligne(string nom, Grecques x) => g.AddRow(nom, Nombre(x.Delta, "0.0000"), Nombre(x.Gamma, "0.000000"), Nombre(x.Vega, "0.00"), Nombre(x.Theta, "0.00"));
        Ligne("Option sur mesure (desk)", p.GrecquesOption);
        Ligne("Couverture", p.GrecquesCouverture);
        Ligne("[bold]Position globale[/]", p.GrecquesGlobales);
        Ligne("[grey]Globale avant arrondi[/]", p.GrecquesGlobalesAvantArrondi);
        return new Rows(interp, lignes, g);
    }

    /// <summary>
    /// On montre la valorisation des positions et la répartition des sensibilités par échéance, avec les
    /// statuts de disponibilité.
    /// </summary>
    public static IRenderable Book(EtatBook e)
    {
        var t = new Table().Border(TableBorder.Rounded).Expand()
            .Title($"[bold]Book[/] — {e.Horodatage:dd/MM HH:mm:ss} UTC — indice {Usd(e.Indice)} $ — {Markup.Escape(e.Statut)}{(!e.RisqueComplet ? " — risque partiel" : "")}")
            .AddColumns("Id", "Structure", "Qté ouv.", "Valeur mark", "Valeur théo", "Prime", "P&L latent", "P&L réalisé", "P&L total $", "Delta", "Vega");
        foreach (var p in e.Positions)
            t.AddRow(p.Origine.Id.ToString()[..8], Markup.Escape(p.Origine.Description), Nombre(p.QuantiteOuverte, "0.0"),
                Btc(p.ValeurMarkBtc), Btc(p.ValeurTheoriqueBtc), Btc(p.PrimeOuverteBtc), Couleur(p.PnlLatentBtc, Btc(p.PnlLatentBtc)),
                Couleur(p.PnlRealiseBtc, Btc(p.PnlRealiseBtc)), Couleur(p.PnlTotalBtc, Usd(p.PnlTotalBtc * e.Indice)),
                Nombre(p.Grecques.Delta, "0.000"), Nombre(p.Grecques.Vega, "0.0"));
        t.AddRow("", "[bold]TOTAL[/]", "", $"[bold]{Btc(e.ValeurMarkBtc)}[/]", $"[bold]{Btc(e.ValeurTheoriqueBtc)}[/]", "",
            Couleur(e.PnlLatentBtc, Btc(e.PnlLatentBtc)), Couleur(e.PnlRealiseBtc, Btc(e.PnlRealiseBtc)), $"[bold]{Couleur(e.PnlTotalUsd, Usd(e.PnlTotalUsd))}[/]",
            Nombre(e.GrecquesTotales.Delta, "0.000"), Nombre(e.GrecquesTotales.Vega, "0.0"));

        var g = new Table().Border(TableBorder.Simple).Title("Grecques par échéance").AddColumns("Échéance", "Delta", "Gamma", "Vega $/pt", "Theta $/j");
        foreach (var (ech, x) in e.GrecquesParEcheance)
            g.AddRow(Echeance(ech), Nombre(x.Delta, "0.0000"), Nombre(x.Gamma, "0.000000"), Nombre(x.Vega, "0.00"), Nombre(x.Theta, "0.00"));
        var tot = e.GrecquesTotales;
        g.AddRow("[bold]Total[/]", Nombre(tot.Delta, "0.0000"), Nombre(tot.Gamma, "0.000000"), Nombre(tot.Vega, "0.00"), Nombre(tot.Theta, "0.00"));
        return new Rows(t, g);
    }

    /// <summary>
    /// On conserve une ligne par opération dans l’affichage et on détaille ses jambes pour retrouver les
    /// ouvertures et leurs fermetures.
    /// </summary>
    public static Table Blotter(IReadOnlyList<Trade> trades)
    {
        var t = new Table().Border(TableBorder.Rounded).Expand().Title("[bold]Blotter[/]")
            .AddColumns("Id", "Heure (UTC)", "Nature", "Origine", "Description", "Sens", "Qté", "Jambes", "Prime BTC");
        foreach (var x in trades)
            t.AddRow(x.Id.ToString()[..8], x.Horodatage.ToString("dd/MM HH:mm:ss", Fr), x.Nature.ToString(), x.IdTradeOrigine?.ToString()[..8] ?? "",
                Markup.Escape(x.Description), x.Sens.ToString(), Nombre(x.Quantite, "0.0"),
                Markup.Escape(string.Join("\n", x.Jambes.Select(j => $"{j.QuantiteSignee.ToString("+0.000;-0.000", Fr)} {j.Instrument} @ {j.PrixExecution.ToString(j.Nature == NatureJambe.Future ? "0.0" : "0.0000", Fr)}")))
                , Btc(x.PrimeBtc));
        return t;
    }

    /// <summary>
    /// On utilise une couleur différente pour distinguer visuellement les gains et les pertes tout en
    /// conservant le texte fourni.
    /// </summary>
    private static string Couleur(double x, string texte) => x >= 0 ? $"[green]{texte}[/]" : $"[red]{texte}[/]";
}
