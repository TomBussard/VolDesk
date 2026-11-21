using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Pricing;

/// <summary>
/// On garde le détail des deux échéances et des volatilités utilisées pour pouvoir expliquer
/// l'interpolation à l'écran.
/// </summary>
public sealed record ResultatInterpolation(
    double Future,
    double Sigma,
    double Maturite,
    double LogMoneyness,
    SmileEcheance Echeance1,
    SmileEcheance? Echeance2,
    double Sigma1,
    double? Sigma2)
{
    public bool EcheanceCotee => Echeance2 is null;
}

/// <summary>
/// On interpole le future linéairement en temps, puis on compare les échéances à même log-moneyness :
/// interpolation du smile sur chaque échéance, puis interpolation de la variance totale. On retrouve enfin
/// la volatilité avec sigma = sqrt(w/T).
/// </summary>
public static class Interpolateur
{
    /// <summary>
    /// On encadre l'option dans les smiles cotés, puis on interpole le future et la variance totale à log-
    /// moneyness égale.
    /// </summary>
    /// <param name="strike">Le strike OTC en USD.</param>
    /// <param name="echeance">La date et l'heure UTC de l'option demandée.</param>
    /// <param name="maintenant">L'instant de marché utilisé pour mesurer les maturités.</param>
    /// <param name="smiles">Les smiles exploitables, avec leurs futures et volatilités décimales.</param>
    /// <returns>Le future en USD, la volatilité décimale, la maturité en années et le détail des smiles utilisés.</returns>
    public static ResultatInterpolation Interpoler(double strike, DateTime echeance, DateTime maintenant, IReadOnlyList<SmileEcheance> smiles)
    {
        if (!double.IsFinite(strike) || !(strike > 0)) throw new HorsPlageException("Le strike doit être strictement positif.");
        double t = Maturite.EnAnnees(echeance, maintenant);
        if (t <= 0) throw new HorsPlageException("L'échéance demandée est déjà passée.");

        var tries = smiles.Where(s => s.Maturite > 0 && double.IsFinite(s.Future) && s.Future > 0).OrderBy(s => s.Echeance).ToList();
        if (tries.Count == 0) throw new HorsPlageException("Aucune échéance cotée exploitable.");

        // On interpole seulement le smile si la date demandée est déjà une échéance cotée.
        var exacte = tries.FirstOrDefault(s => s.Echeance == echeance);
        if (exacte is not null)
        {
            double kExact = Math.Log(strike / exacte.Future);
            double sigma = VolSurEcheance(exacte, kExact);
            return new ResultatInterpolation(exacte.Future, sigma, t, kExact, exacte, null, sigma, null);
        }

        var s1 = tries.LastOrDefault(s => s.Echeance < echeance);
        var s2 = tries.FirstOrDefault(s => s.Echeance > echeance);
        if (s1 is null || s2 is null)
            throw new HorsPlageException(
                $"Échéance {echeance:dd/MM/yyyy} hors de la plage cotée [{tries[0].Echeance:dd/MM/yyyy} ; {tries[^1].Echeance:dd/MM/yyyy}].");

        double t1 = Maturite.EnAnnees(s1.Echeance, maintenant);
        double t2 = Maturite.EnAnnees(s2.Echeance, maintenant);
        double poids = (t - t1) / (t2 - t1);

        // On interpole d'abord le future en temps pour situer le strike OTC par rapport à son propre sous-jacent.
        double f = s1.Future + poids * (s2.Future - s1.Future);
        // On compare ensuite les deux échéances à même log-moneyness, et non au même strike nominal.
        double k = Math.Log(strike / f);
        // On obtient la volatilité de chaque smile au strike équivalent de cette log-moneyness.
        double sigma1 = VolSurEcheance(s1, k);
        double sigma2 = VolSurEcheance(s2, k);
        // On interpole la variance totale sigma²T, puis on revient à la volatilité de la date OTC.
        double w1 = sigma1 * sigma1 * t1, w2 = sigma2 * sigma2 * t2;
        // On refuse une variance totale décroissante plutôt que de présenter une surface avec arbitrage calendaire.
        if (w2 < w1 - 1e-12)
            throw new HorsPlageException("Variance totale décroissante : interpolation refusée (arbitrage calendaire).");
        double w = w1 + poids * (w2 - w1);
        double sigmaFinale = Math.Sqrt(w / t);

        return new ResultatInterpolation(f, sigmaFinale, t, k, s1, s2, sigma1, sigma2);
    }

    /// <summary>
    /// On calcule le strike équivalent Ki = Fi exp(k), puis on interpole la volatilité en log-moneyness entre
    /// les deux strikes qui l'encadrent.
    /// </summary>
    public static double VolSurEcheance(SmileEcheance smile, double logMoneyness)
    {
        double ki = smile.Future * Math.Exp(logMoneyness);
        var points = smile.Points;
        if (points.Count == 0 || ki < points[0].Strike || ki > points[^1].Strike)
            throw new HorsPlageException(
                $"Strike équivalent {ki:N0} hors des strikes cotés de l'échéance {smile.Echeance:dd/MM/yyyy}" +
                (points.Count > 0 ? $" [{points[0].Strike:N0} ; {points[^1].Strike:N0}]." : "."));

        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].Strike == ki) return points[i].Vol;
            if (points[i].Strike > ki)
            {
                var a = points[i - 1];
                var b = points[i];
                double ka = Math.Log(a.Strike / smile.Future);
                double kb = Math.Log(b.Strike / smile.Future);
                return a.Vol + (logMoneyness - ka) / (kb - ka) * (b.Vol - a.Vol);
            }
        }
        return points[^1].Vol;
    }
}
