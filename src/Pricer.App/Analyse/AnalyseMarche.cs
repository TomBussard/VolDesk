using System.Globalization;
using System.Text;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using ParametresMarche = Pricer.Domaine.Structures.ParametresMarche;
using Pricer.Infrastructure.Exports;

namespace Pricer.App.Analyse;

/// <summary>
/// On rassemble dans cette ligne les écarts entre nos calculs et les champs publiés par Deribit pour une
/// même option.
/// </summary>
public sealed record LigneValidation
{
    public required string Instrument { get; init; }
    public required bool DansLaMonnaie { get; init; }
    /// <summary>
    /// On mesure ici l'écart, en USD, entre le future qui reproduit le mark et le champ underlying_price
    /// reçu de Deribit.
    /// </summary>
    public double? EcartFutureImplicite { get; init; }
    public required double Strike { get; init; }
    public required double JoursRestants { get; init; }
    public required double Moneyness { get; init; }
    public required double EcartPrixBtc { get; init; }
    public double? EcartVolBid { get; init; }
    public double? EcartVolMark { get; init; }
    public double? EcartVolAsk { get; init; }
    public required string MethodeMark { get; init; }
    public required int IterationsMark { get; init; }
    public double? EcartDelta { get; init; }
    public double? EcartGamma { get; init; }
    public double? EcartVega { get; init; }
    public double? EcartTheta { get; init; }
}

/// <summary>
/// On compare notre prix Black-76, nos volatilités inversées et nos grecques aux tickers complets d'un
/// enregistrement réel. On conserve les résultats en CSV et en LaTeX pour pouvoir vérifier les chiffres du
/// rapport.
/// </summary>
public static class AnalyseMarche
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// On recalcule les prix, les volatilités et les grecques des options disposant d’un ticker complet
    /// pour mesurer leurs écarts avec Deribit.
    /// </summary>
    /// <returns>On renvoie une ligne de comparaison par option exploitable, sans compléter les données manquantes.</returns>
    public static IReadOnlyList<LigneValidation> Valider(EtatMarche etat)
    {
        var lignes = new List<LigneValidation>();
        foreach (var instrument in etat.Referentiel.Options)
        {
            var c = etat.Cotation(instrument.Nom);
            if (c is null || c.GrecquesDeribit is null || c.MarkIv is null || c.Future is null || instrument.Echeance <= c.Horodatage) continue;
            var ligne = PricerStructure.CoterJambe(new JambeCotee(instrument, 1), 1, etat);
            var g = ligne.Theorique.Grecques;
            var d = c.GrecquesDeribit.Value;
            double f = ligne.Parametres.Future;
            bool itm = instrument.Type == Pricer.Domaine.Modeles.TypeOption.Call ? instrument.Strike < f : instrument.Strike > f;
            lignes.Add(new LigneValidation
            {
                Instrument = instrument.Nom,
                DansLaMonnaie = itm,
                EcartFutureImplicite = c.Mark is double mark ? FutureImplicite(mark, instrument.Strike, ligne.Parametres, instrument.Type) - f : null,
                Strike = instrument.Strike,
                JoursRestants = ligne.Parametres.Maturite * 365,
                Moneyness = Math.Log(instrument.Strike / ligne.Parametres.Future),
                EcartPrixBtc = ligne.EcartMarkBtc ?? double.NaN,
                EcartVolBid = ligne.VolBid?.EcartPoints,
                EcartVolMark = ligne.VolMark?.EcartPoints,
                EcartVolAsk = ligne.VolAsk?.EcartPoints,
                MethodeMark = ligne.VolMark?.Calculee.Statut == StatutVol.Calculee ? ligne.VolMark.Calculee.Methode.ToString() : ligne.VolMark?.Calculee.Statut.ToString() ?? "",
                IterationsMark = ligne.VolMark?.Calculee.Iterations ?? 0,
                EcartDelta = g.Delta - d.Delta,
                EcartGamma = g.Gamma - d.Gamma,
                EcartVega = g.Vega - d.Vega,
                EcartTheta = g.Theta - d.Theta
            });
        }
        return lignes;
    }

    /// <summary>
    /// On recherche par dichotomie le future F' qui reproduit le mark en crypto. On borne la recherche à
    /// plus ou moins 2 % du future publié pour garder une comparaison locale.
    /// </summary>
    private static double? FutureImplicite(double mark, double strike, ParametresMarche p, Pricer.Domaine.Modeles.TypeOption type)
    {
        // On cherche le future qui ramène le mark crypto observé au prix Black-76 pour cette même volatilité.
        double Ecart(double f) => Black76.Calculer(f, strike, p.Maturite, p.Sigma, type).PrixBtc - mark;
        double bas = p.Future * 0.98, haut = p.Future * 1.02;
        if (Ecart(bas) * Ecart(haut) > 0) return null;
        for (int i = 0; i < 100; i++)
        {
            double m = 0.5 * (bas + haut);
            if (Ecart(bas) * Ecart(m) <= 0) haut = m; else bas = m;
        }
        return 0.5 * (bas + haut);
    }

    /// <summary>
    /// On exporte les comparaisons en CSV et les statistiques en LaTeX, avec une séparation entre
    /// options dans et hors de la monnaie.
    /// </summary>
    /// <param name="dossier">On crée les fichiers de résultats dans ce dossier.</param>
    public static void Ecrire(IReadOnlyList<LigneValidation> lignes, string dossier)
    {
        Directory.CreateDirectory(dossier);
        ExportCsv.Ecrire(Path.Combine(dossier, "validation.csv"), lignes);

        var prix = lignes.Select(l => l.EcartPrixBtc).Where(x => !double.IsNaN(x)).ToList();
        var volMark = lignes.Select(l => l.EcartVolMark).OfType<double>().ToList();
        var volMarkOtm = lignes.Where(l => !l.DansLaMonnaie).Select(l => l.EcartVolMark).OfType<double>().ToList();
        var volBid = lignes.Select(l => l.EcartVolBid).OfType<double>().ToList();
        var volAsk = lignes.Select(l => l.EcartVolAsk).OfType<double>().ToList();

        var tex = new StringBuilder();
        // On centralise l’écriture des commandes LaTeX utilisées par les tableaux de statistiques.
        void Cmd(string nom, string valeur) => tex.AppendLine($"\\newcommand{{\\{nom}}}{{{valeur}}}");
        // On garde le point décimal dans les valeurs techniques écrites pour LaTeX.
        string F(double x, string format = "0.0000") => x.ToString(format, Inv);
        // On trie les écarts absolus et on retient un rang simple ; une série vide donne zéro.
        double Quantile(List<double> v, double q) { var s = v.Select(Math.Abs).OrderBy(x => x).ToList(); return s.Count == 0 ? 0 : s[(int)Math.Min(s.Count - 1, Math.Floor(q * s.Count))]; }

        Cmd("StatNbOptions", lignes.Count.ToString(Inv));
        Cmd("StatPrixMaxAbs", F(prix.Select(Math.Abs).DefaultIfEmpty().Max(), "0.000000"));
        Cmd("StatPrixMoyAbs", F(prix.Select(Math.Abs).DefaultIfEmpty().Average(), "0.0000000"));
        Cmd("StatPrixSignales", prix.Count(x => Math.Abs(x) > PricerStructure.SeuilEcartPrixBtc).ToString(Inv));
        Cmd("StatVolMarkNb", volMark.Count.ToString(Inv));
        Cmd("StatVolMarkSignales", volMark.Count(x => Math.Abs(x) > PricerStructure.SeuilEcartVolPoints).ToString(Inv));
        Cmd("StatVolMarkMed", F(Quantile(volMark, 0.5), "0.000"));
        Cmd("StatVolMarkQnf", F(Quantile(volMark, 0.95), "0.000"));
        Cmd("StatVolBidNb", volBid.Count.ToString(Inv));
        Cmd("StatVolBidSignales", volBid.Count(x => Math.Abs(x) > PricerStructure.SeuilEcartVolPoints).ToString(Inv));
        Cmd("StatVolAskNb", volAsk.Count.ToString(Inv));
        Cmd("StatVolAskSignales", volAsk.Count(x => Math.Abs(x) > PricerStructure.SeuilEcartVolPoints).ToString(Inv));
        Cmd("StatNewton", lignes.Count(l => l.MethodeMark == "Newton").ToString(Inv));
        Cmd("StatDichotomie", lignes.Count(l => l.MethodeMark == "Dichotomie").ToString(Inv));
        Cmd("StatNonCalculables", lignes.Count(l => l.MethodeMark is not "Newton" and not "Dichotomie").ToString(Inv));
        Cmd("StatIterMoy", F(lignes.Where(l => l.MethodeMark == "Newton").Select(l => (double)l.IterationsMark).DefaultIfEmpty().Average(), "0.0"));
        Cmd("StatDeltaMax", F(lignes.Select(l => Math.Abs(l.EcartDelta ?? 0)).DefaultIfEmpty().Max(), "0.00000"));
        Cmd("StatGammaMax", F(lignes.Select(l => Math.Abs(l.EcartGamma ?? 0)).DefaultIfEmpty().Max(), "0.0000000"));
        Cmd("StatVegaMax", F(lignes.Select(l => Math.Abs(l.EcartVega ?? 0)).DefaultIfEmpty().Max(), "0.0000"));
        Cmd("StatThetaMoyAbs", F(lignes.Select(l => Math.Abs(l.EcartTheta ?? 0)).DefaultIfEmpty().Average(), "0.000"));
        Cmd("StatThetaMax", F(lignes.Select(l => Math.Abs(l.EcartTheta ?? 0)).DefaultIfEmpty().Max(), "0.000"));
        Cmd("StatThetaConformes", lignes.Count(l => Math.Abs(l.EcartTheta ?? 0) < 0.5).ToString(Inv));

        // On sépare les options dans et hors de la monnaie pour ne pas mélanger des inversions de sensibilité très différente.
        var otm = lignes.Where(l => !l.DansLaMonnaie).ToList();
        var itm = lignes.Where(l => l.DansLaMonnaie).ToList();
        var volOtm = otm.Select(l => l.EcartVolMark).OfType<double>().ToList();
        Cmd("StatNbOtm", otm.Count.ToString(Inv));
        Cmd("StatNbItm", itm.Count.ToString(Inv));
        Cmd("StatPrixSignalesOtm", otm.Count(l => Math.Abs(l.EcartPrixBtc) > PricerStructure.SeuilEcartPrixBtc).ToString(Inv));
        Cmd("StatPrixSignalesItm", itm.Count(l => Math.Abs(l.EcartPrixBtc) > PricerStructure.SeuilEcartPrixBtc).ToString(Inv));
        Cmd("StatPrixMaxAbsOtm", F(otm.Select(l => Math.Abs(l.EcartPrixBtc)).DefaultIfEmpty().Max(), "0.000000"));
        Cmd("StatVolMarkMedOtm", F(Quantile(volOtm, 0.5), "0.000"));
        Cmd("StatVolMarkQnfOtm", F(Quantile(volOtm, 0.95), "0.000"));
        Cmd("StatVolMarkSignalesOtm", volOtm.Count(x => Math.Abs(x) > PricerStructure.SeuilEcartVolPoints).ToString(Inv));
        Cmd("StatVolMarkNbOtm", volOtm.Count.ToString(Inv));
        var fImp = itm.Select(l => l.EcartFutureImplicite).OfType<double>().OrderBy(x => x).ToList();
        Cmd("StatFutureImpliciteMed", F(fImp.Count == 0 ? 0 : fImp[fImp.Count / 2], "0.0"));
        Cmd("StatFutureImpliciteMin", F(fImp.DefaultIfEmpty().Min(), "0.0"));
        Cmd("StatFutureImpliciteMax", F(fImp.DefaultIfEmpty().Max(), "0.0"));
        Cmd("StatEcheancesValidees", lignes.Select(l => l.Instrument.Split('-')[1]).Distinct().Count().ToString(Inv));

        // On regroupe les écarts de volatilité en classes pour visualiser leur fréquence dans le rapport.
        double[] bornes = [-0.5, -0.2, -0.1, -0.05, -0.02, 0.02, 0.05, 0.1, 0.2, 0.5];
        var classes = new List<(string Etiquette, int N)>();
        classes.Add(("<-0,5", volMarkOtm.Count(x => x < bornes[0])));
        for (int i = 0; i < bornes.Length - 1; i++)
            classes.Add(($"{bornes[i].ToString("0.##", Inv)}", volMarkOtm.Count(x => x >= bornes[i] && x < bornes[i + 1])));
        classes.Add((">0,5", volMarkOtm.Count(x => x >= bornes[^1])));
        int max = Math.Max(1, classes.Max(c => c.N));
        var h = new StringBuilder();
        h.AppendLine("\\begin{tikzpicture}[x=1.15cm,y=4cm/" + max.ToString(Inv) + "]");
        for (int i = 0; i < classes.Count; i++)
        {
            bool alerte = i == 0 || i >= classes.Count - 3 || i <= 2;
            h.AppendLine($"  \\fill[{(alerte ? "red!55" : "blue!55")}] ({i.ToString(Inv)},0) rectangle ({(i + 0.85).ToString(Inv)},{classes[i].N.ToString(Inv)});");
            h.AppendLine($"  \\node[above,font=\\scriptsize] at ({(i + 0.425).ToString(Inv)},{classes[i].N.ToString(Inv)}) {{{classes[i].N}}};");
            h.AppendLine($"  \\node[below,font=\\scriptsize,rotate=0] at ({(i + 0.425).ToString(Inv)},0) {{{classes[i].Etiquette.Replace(".", ",")}}};");
        }
        h.AppendLine($"  \\draw ({0},0) -- ({classes.Count.ToString(Inv)},0);");
        h.AppendLine("\\end{tikzpicture}");

        // On présente aussi les écarts par échéance pour examiner l'effet de la maturité et du vega sur les inversions.
        var parEcheance = new StringBuilder();
        parEcheance.AppendLine("\\begin{table}[H]\\centering\\resizebox{\\linewidth}{!}{\\begin{tabular}{lrrrrrr}\\toprule");
        parEcheance.AppendLine("Échéance & Jours & Options & Prix signalés & Vols du mark signalées (OTM) & Écart de vol médian (pt) & Theta conformes \\\\\\midrule");
        foreach (var g in lignes.GroupBy(l => l.Instrument.Split('-')[1]).OrderBy(g => g.Min(l => l.JoursRestants)))
        {
            var horsMonnaie = g.Where(l => !l.DansLaMonnaie).Select(l => l.EcartVolMark).OfType<double>().ToList();
            // On protège la virgule française pour qu’elle reste correctement espacée dans les nombres LaTeX.
            string V(double x, string format) => F(x, format).Replace(".", "{,}");
            parEcheance.AppendLine($"{g.Key} & {V(g.Average(l => l.JoursRestants), "0.0")} & {g.Count()} & {g.Count(l => Math.Abs(l.EcartPrixBtc) > PricerStructure.SeuilEcartPrixBtc)} & " +
                $"{horsMonnaie.Count(x => Math.Abs(x) > PricerStructure.SeuilEcartVolPoints)} / {horsMonnaie.Count} & {V(Quantile(horsMonnaie, 0.5), "0.000")} & {g.Count(l => Math.Abs(l.EcartTheta ?? 0) < 0.5)} / {g.Count()} \\\\");
        }
        parEcheance.AppendLine("\\bottomrule\\end{tabular}}\\caption{Validation par échéance : les écarts se concentrent sur les maturités courtes.}\\end{table}");
        File.WriteAllText(Path.Combine(dossier, "par_echeance.tex"), parEcheance.ToString());

        File.WriteAllText(Path.Combine(dossier, "resultats.tex"), tex.ToString());
        File.WriteAllText(Path.Combine(dossier, "histogramme_vol.tex"), h.ToString());
    }
}
