using System.Globalization;
using System.Text;
using Pricer.Domaine.Booking;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;

namespace Pricer.App.Analyse;

/// <summary>
/// The LaTeX tables are produced from the executed scenario, so that the examples in the report
/// correspond to the calculation of the application.
/// </summary>
public static class ExportLatex
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    /// <summary>
    /// We format a number in French and replace the bundling spaces with LaTeX-compatible
    /// spaces.
    /// </summary>
    private static string N(double x, string f) => x.ToString(f, Fr).Replace("\u202f", "\\,").Replace("\u00a0", "\\,");
    /// <summary>
    /// Text characters that have a particular role in LaTeX are protected.
    /// </summary>
    private static string Echapper(string s) => s.Replace("_", "\\_").Replace("&", "\\&").Replace("%", "\\%");

    /// <summary>
    /// We turn a quote into LaTeX paintings with its legs, prices and sensitivities.
    /// </summary>
    /// <returns>On renvoie le bloc LaTeX prêt à être inséré dans un document.</returns>
    public static string Cotation(CotationStructure c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\\begin{table}[H]\\centering\\footnotesize");
        sb.AppendLine("\\resizebox{\\linewidth}{!}{\\begin{tabular}{lrrrrrrrr}\\toprule");
        sb.AppendLine("Jambe & Qté & $F$ & $\\sigma$ mark & Théo BTC & Mark BTC & Écart & Vol mark (nous / Deribit) & Bid / Ask \\\\\\midrule");
        foreach (var l in c.Lignes)
        {
            string vol = l.VolMark is { Calculee.EstCalculee: true } v ? $"{N(v.Calculee.Sigma!.Value * 100, "0.00")} / {N(v.Deribit ?? 0, "0.00")}" : "n/c";
            sb.AppendLine($"\\code{{{Echapper(l.Jambe.Libelle)}}} & {N(l.QuantiteSignee, "+0.0;-0.0")} & {N(l.Parametres.Future, "#,##0.0")} & {N(l.Parametres.Sigma * 100, "0.00")} & {N(l.Theorique.PrixBtc, "0.000000")} & {N(l.Cotation?.Mark ?? 0, "0.0000")} & {N(l.EcartMarkBtc ?? 0, "+0.000000;-0.000000")}{(l.EcartMarkSignale ? "$^\\star$" : "")} & {vol} & {N(l.Cotation?.Bid ?? 0, "0.0000")} / {N(l.Cotation?.Ask ?? 0, "0.0000")} \\\\");
        }
        sb.AppendLine("\\bottomrule\\end{tabular}}\\\\[0.3cm]");
        sb.AppendLine("\\begin{tabular}{lrr@{\\qquad}lrr}\\toprule");
        sb.AppendLine("Prix par unité & BTC & USD & Grecques & nous & Deribit \\\\\\midrule");
        double f = c.Future;
        var g = c.Grecques; var gd = c.GrecquesDeribit;
        // The Greek published by Deribit is displayed, or a dash when this detail is missing.
        string D(Func<Grecques, double> champ, string format) => gd is Grecques x ? N(champ(x), format) : "---";
        sb.AppendLine($"Théorique & {N(c.TheoriqueUnitaireBtc, "0.000000")} & {N(c.TheoriqueUnitaireUsd, "#,##0.00")} & Delta & {N(g.Delta, "0.0000")} & {D(x => x.Delta, "0.0000")} \\\\");
        sb.AppendLine($"Exécutable achat & {N(c.PrixExecutableAchatBtc ?? 0, "0.000000")} & {N((c.PrixExecutableAchatBtc ?? 0) * f, "#,##0.00")} & Gamma & {N(g.Gamma, "0.000000")} & {D(x => x.Gamma, "0.000000")} \\\\");
        sb.AppendLine($"Exécutable vente & {N(c.PrixExecutableVenteBtc ?? 0, "0.000000")} & {N((c.PrixExecutableVenteBtc ?? 0) * f, "#,##0.00")} & Vega \\$/pt & {N(g.Vega, "0.00")} & {D(x => x.Vega, "0.00")} \\\\");
        sb.AppendLine($"Fourchette & {N(c.FourchetteBtc ?? 0, "0.000000")} & {N((c.FourchetteBtc ?? 0) * f, "#,##0.00")} & Theta \\$/j & {N(g.Theta, "0.00")} & {D(x => x.Theta, "0.00")} \\\\");
        sb.AppendLine("\\bottomrule\\end{tabular}");
        sb.AppendLine($"\\caption{{Cotation live : {Echapper(c.Structure.Description)}, {c.Sens} $\\times$ {N(c.Quantite, "0.0")} ($^\\star$ : écart au mark signalé).}}");
        sb.AppendLine("\\end{table}");
        return sb.ToString();
    }

    /// <summary>
    /// The LaTeX table of the results scenarios is constructed today and at maturity, in BTC
    /// and USD.
    /// </summary>
    public static string Profil(ProfilStructure p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\\begin{table}[H]\\centering\\footnotesize\\begin{tabular}{rrrrrr}\\toprule");
        sb.AppendLine("Var. & Sous-jacent & Échéance USD & Échéance BTC & Aujourd'hui USD & Aujourd'hui BTC \\\\\\midrule");
        foreach (var x in p.Points)
            sb.AppendLine($"{N(x.Variation * 100, "+0;-0;0")}\\,\\% & {N(x.SousJacent, "#,##0")} & {N(x.PnlEcheanceUsd, "#,##0.00")} & {N(x.PnlEcheanceBtc, "0.000000")} & {N(x.PnlAujourdhuiUsd, "#,##0.00")} & {N(x.PnlAujourdhuiBtc, "0.000000")} \\\\");
        sb.AppendLine("\\bottomrule\\end{tabular}");
        string gain = p.GainMaxUsd is double g ? N(g, "#,##0.00") + "~USD" : "illimité";
        string perte = p.PerteMaxUsd is double l ? N(l, "#,##0.00") + "~USD" : "illimitée";
        sb.AppendLine($"\\caption{{Profil du call spread : prime {N(p.PrimeBtc, "0.000000")}~BTC, gain maximal {gain}, perte maximale {perte}, point mort {string.Join(" ; ", p.PointsMorts.Select(x => N(x, "#,##0.00")))}.}}");
        sb.AppendLine("\\end{table}");
        return sb.ToString();
    }

    /// <summary>
    /// Interpolation, hedging instruments and residual risks of the TBT proposal are presented
    /// in LaTeX.
    /// </summary>
    public static string Couverture(PropositionCouverture p)
    {
        var i = p.Interpolation;
        var sb = new StringBuilder();
        sb.AppendLine("\\begin{table}[H]\\centering\\footnotesize");
        sb.AppendLine("\\begin{tabular}{lr}\\toprule");
        sb.AppendLine($"Option & \\code{{{Echapper(p.Option.Libelle)}}}, client {(p.SensClient == Sens.Achat ? "acheteur" : "vendeur")} de {N(p.Quantite, "0.0")} \\\\");
        sb.AppendLine($"Échéances encadrantes & {(i.EcheanceCotee ? i.Echeance1.Echeance.ToString("dd/MM HH:mm", Fr) : $"{i.Echeance1.Echeance:dd/MM} et {i.Echeance2!.Echeance:dd/MM}")} \\\\");
        sb.AppendLine($"Future interpolé, $k$ & {N(i.Future, "#,##0.00")}, {N(i.LogMoneyness, "0.0000")} \\\\");
        sb.AppendLine($"$\\sigma_1$, $\\sigma_2$ $\\rightarrow$ $\\sigma$ & {N(i.Sigma1 * 100, "0.00")}\\,\\%, {N((i.Sigma2 ?? i.Sigma1) * 100, "0.00")}\\,\\% $\\rightarrow$ \\textbf{{{N(i.Sigma * 100, "0.00")}\\,\\%}} \\\\");
        sb.AppendLine($"Prix théorique & {N(p.ValeurOption.PrixBtc, "0.000000")}~BTC ({N(p.ValeurOption.PrixUsd, "#,##0.00")}~USD) \\\\");
        sb.AppendLine($"Coût de couverture, marge & {N(p.CoutCouvertureBtc, "0.000000")}~BTC, {N(p.MargeBtc, "0.000000")}~BTC/unité \\\\");
        sb.AppendLine($"\\textbf{{Prix client}} & \\textbf{{{N(p.PrixClientBtc, "0.000000")}~BTC ({N(p.PrixClientUsd, "#,##0.00")}~USD)}} \\\\");
        sb.AppendLine("\\bottomrule\\end{tabular}\\\\[0.3cm]");
        sb.AppendLine("\\begin{tabular}{lrrrr}\\toprule Instrument & Qté brute & Qté arrondie & Exécutable & Coût BTC \\\\\\midrule");
        foreach (var l in p.Lignes)
            sb.AppendLine($"\\code{{{Echapper(l.Nom)}}} & {N(l.QuantiteAvantArrondi, "+0.000;-0.000")} & {N(l.Quantite, "+0.000;-0.000")} & {(l.Nature == NatureJambe.Future ? N(l.PrixExecutable, "#,##0.0") + " USD" : N(l.PrixExecutable, "0.0000"))} & {N(l.CoutBtc, "0.000000")} \\\\");
        sb.AppendLine("\\bottomrule\\end{tabular}\\\\[0.3cm]");
        sb.AppendLine("\\begin{tabular}{lrrrr}\\toprule Grecques & Delta & Gamma & Vega \\$/pt & Theta \\$/j \\\\\\midrule");
        // We keep the same order and the same Greek units for each cover line.
        void L(string nom, Grecques x) => sb.AppendLine($"{nom} & {N(x.Delta, "0.0000")} & {N(x.Gamma, "0.000000")} & {N(x.Vega, "0.00")} & {N(x.Theta, "0.00")} \\\\");
        L("Option (desk)", p.GrecquesOption);
        L("Couverture", p.GrecquesCouverture);
        L("\\textbf{Position globale}", p.GrecquesGlobales);
        L("Globale avant arrondi", p.GrecquesGlobalesAvantArrondi);
        sb.AppendLine("\\bottomrule\\end{tabular}");
        sb.AppendLine($"\\caption{{Option sur mesure : interpolation, couverture choisie automatiquement (déterminant normalisé {N(p.DeterminantNormalise, "0.000")}) et grecques résiduelles.}}");
        sb.AppendLine("\\end{table}");
        return sb.ToString();
    }

    /// <summary>
    /// The LaTeX tables of the aggregate positions and Greeks in the book are prepared at a
    /// given moment.
    /// </summary>
    public static string Book(EtatBook e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\\begin{table}[H]\\centering\\footnotesize\\resizebox{\\linewidth}{!}{\\begin{tabular}{lrrrrrr}\\toprule");
        sb.AppendLine("Structure & Qté ouv. & Valeur mark & Valeur théo & P\\&L latent & P\\&L réalisé & Vega \\\\\\midrule");
        foreach (var p in e.Positions)
            sb.AppendLine($"{Echapper(p.Origine.Description)} & {N(p.QuantiteOuverte, "0.0")} & {N(p.ValeurMarkBtc, "0.000000")} & {N(p.ValeurTheoriqueBtc, "0.000000")} & {N(p.PnlLatentBtc, "0.000000")} & {N(p.PnlRealiseBtc, "0.000000")} & {N(p.Grecques.Vega, "0.00")} \\\\");
        sb.AppendLine($"\\midrule\\textbf{{Total}} & & {N(e.ValeurMarkBtc, "0.000000")} & {N(e.ValeurTheoriqueBtc, "0.000000")} & {N(e.PnlLatentBtc, "0.000000")} & {N(e.PnlRealiseBtc, "0.000000")} & {N(e.GrecquesTotales.Vega, "0.00")} \\\\");
        sb.AppendLine("\\bottomrule\\end{tabular}}");
        sb.AppendLine($"\\caption{{Book après les trades de démonstration et le débouclage partiel du call spread (P\\&L total {N(e.PnlTotalBtc, "0.000000")}~BTC, soit {N(e.PnlTotalUsd, "#,##0.00")}~USD à l'indice {N(e.Indice, "#,##0.00")}). Le P\\&L latent négatif des structures cotées est la demi-fourchette payée ; l'option sur mesure dégage la marge et le coût de couverture facturés au client.}}");
        sb.AppendLine("\\end{table}");
        return sb.ToString();
    }
}
