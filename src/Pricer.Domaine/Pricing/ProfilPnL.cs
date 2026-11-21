using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Structures;

namespace Pricer.Domaine.Pricing;

/// <summary>
/// The result and sensitivities of a scenario are retained to feed the curves and their table.
/// </summary>
/// <param name="Variation">La variation décimale par rapport au niveau de référence.</param>
/// <param name="SousJacent">Le prix du sous-jacent du scénario en USD.</param>
/// <param name="PnlEcheanceUsd">Le P&amp;L terminal en USD, après prise en compte de la prime crypto.</param>
/// <param name="PnlEcheanceBtc">Le P&amp;L terminal dans la crypto active.</param>
/// <param name="PnlAujourdhuiUsd">Le P&amp;L de réévaluation aujourd'hui en USD.</param>
/// <param name="PnlAujourdhuiBtc">Le P&amp;L de réévaluation aujourd'hui dans la crypto active.</param>
/// <param name="Delta">Le delta du prix théorique USD, en unités de sous-jacent.</param>
/// <param name="Vega">Le vega du prix théorique, en USD par point de volatilité.</param>
public readonly record struct PointProfil(
    double Variation, double SousJacent,
    double PnlEcheanceUsd, double PnlEcheanceBtc,
    double PnlAujourdhuiUsd, double PnlAujourdhuiBtc,
    double Delta = 0, double Vega = 0);

/// <summary>
/// Scenarios, bonus and global boundaries are grouped to explain the structure profile.
/// </summary>
public sealed record ProfilStructure
{
    public required IReadOnlyList<PointProfil> Points { get; init; }
    public required double PrimeBtc { get; init; }
    public required double PrimeUsd { get; init; }
    // The historical name for the components is retained: the scenario marker is the current index.
    public required double Future { get; init; }
    public double? GainMaxUsd { get; init; }
    public double? PerteMaxUsd { get; init; }
    public double? GainMaxBtc { get; init; }
    public double? PerteMaxBtc { get; init; }
    public required IReadOnlyList<double> PointsMorts { get; init; }

    /// <summary>
    /// The points are gathered in a matrix to reuse the profile without depending on a
    /// graphical component.
    /// </summary>
    /// <returns>Les colonnes contiennent le sous-jacent USD, le P&amp;L terminal USD et le P&amp;L aujourd'hui USD.</returns>
    public double[,] EnMatrice()
    {
        var m = new double[Points.Count, 3];
        for (int i = 0; i < Points.Count; i++)
        {
            m[i, 0] = Points[i].SousJacent;
            m[i, 1] = Points[i].PnlEcheanceUsd;
            m[i, 2] = Points[i].PnlAujourdhuiUsd;
        }
        return m;
    }
}

/// <summary>
/// We keep the bonus in crypto, as in the float, and we convert the result at the price of the
/// scenario.
/// </summary>
public static class ProfilPnL
{
    /// <summary>
    /// The legs are re-evaluated with fixed volatility and duration, and the dead points and
    /// terminal terminals are searched beyond the grid.
    /// </summary>
    /// <param name="cotation">La structure cotée avec ses quantités signées du point de vue du desk.</param>
    /// <param name="contexte">Le marché courant, notamment l'indice qui sert de référence aux chocs.</param>
    /// <param name="variationMin">La variation minimale décimale, strictement supérieure à moins un.</param>
    /// <param name="variationMax">La variation maximale décimale du sous-jacent.</param>
    /// <param name="pas">L'écart décimal entre les scénarios ; la grille est limitée à 1001 points.</param>
    /// <param name="primeSigneeBtc">La prime totale en crypto, positive si payée ; si elle est absente, on utilise la prime exécutable.</param>
    /// <returns>Les scénarios de P&amp;L en crypto et USD, leurs delta et vega, ainsi que les bornes et points morts.</returns>
    public static ProfilStructure Calculer(CotationStructure cotation, IContexteMarche contexte,
        double variationMin = -0.30, double variationMax = 0.30, double pas = 0.05, double? primeSigneeBtc = null)
    {
        if (!double.IsFinite(variationMin) || !double.IsFinite(variationMax) || !double.IsFinite(pas) ||
            variationMin <= -1 || pas <= 0 || variationMax <= variationMin || (variationMax - variationMin) / pas > 1000)
            throw new ArgumentException("Grille invalide : minimum > -100 %, maximum > minimum, pas positif, 1001 points au maximum.");
        double reference = contexte.Indice ?? cotation.Future;
        if (!double.IsFinite(reference) || reference <= 0) throw new ArgumentException("Indice de scénario invalide.");
        double prime = primeSigneeBtc ?? cotation.PrimeTotaleBtc
            ?? throw new InvalidOperationException("Un prix exécutable ou client est nécessaire pour le profil de P&L.");
        var lignes = cotation.Lignes;
        double Payoff(double s) => lignes.Sum(l => l.QuantiteSignee * l.Jambe.PayoffUsd(s));
        // The premium is kept in crypto, so its USD value also depends on the price of the scenario.
        double PnlUsd(double s) => Payoff(s) - prime * s;
        var points = new List<PointProfil>();
        int n = (int)Math.Floor((variationMax - variationMin) / pas + 1e-9);
        for (int i = 0; i <= n; i++)
        {
            double x = variationMin + i * pas, s = reference * (1 + x), aujourdHui = 0;
            var grecques = Grecques.Zero;
            foreach (var l in lignes)
            {
                // On fige la vol et la durée de chaque jambe ; son future suit la variation de l'indice.
                var r = Black76.Calculer(l.Parametres.Future * s / reference, l.Jambe.Strike,
                    l.Parametres.Maturite, l.Parametres.Sigma, l.Jambe.Type);
                aujourdHui += l.QuantiteSignee * r.PrixBtc;
                grecques += l.QuantiteSignee * r.Grecques;
            }
            double terminal = Payoff(s) / s - prime;
            points.Add(new PointProfil(x, s, terminal * s, terminal,
                (aujourdHui - prime) * s, aujourdHui - prime, grecques.Delta, grecques.Vega));
        }

        // The P&L is resolved at all intervals, including beyond the screen grid.
        var strikes = lignes.Select(l => l.Jambe.Strike).Distinct().OrderBy(k => k).ToList();
        var noeuds = new List<double> { 0 };
        noeuds.AddRange(strikes);
        var valeursUsd = noeuds.Select(PnlUsd).ToList();
        double penteFinale = lignes.Where(l => l.Jambe.Type == TypeOption.Call).Sum(l => l.QuantiteSignee) - prime;
        var racines = new List<double>();
        var limites = noeuds.Append(double.PositiveInfinity).ToList();
        for (int i = 0; i < limites.Count - 1; i++)
        {
            double a = limites[i], b = limites[i + 1];
            double centre = double.IsPositiveInfinity(b) ? a + Math.Max(1, a) : (a + b) / 2;
            double pente = -prime, constante = 0;
            foreach (var l in lignes)
            {
                double k = l.Jambe.Strike, q = l.QuantiteSignee;
                if (l.Jambe.Type == TypeOption.Call && centre > k) { pente += q; constante -= q * k; }
                if (l.Jambe.Type == TypeOption.Put && centre < k) { pente -= q; constante += q * k; }
            }
            // On cherche une racine sur chaque portion linéaire ; une portion plate ne fournit pas de point mort isolé.
            if (Math.Abs(pente) < 1e-12) continue;
            double zero = -constante / pente;
            if (zero > 0 && zero >= a - 1e-7 && zero <= b + 1e-7 && !racines.Any(r => Math.Abs(r - zero) < 1e-5)) racines.Add(zero);
        }
        // The limits in zero and infinity are added to find the global terminals, beyond the displayed grid.
        var valeursCrypto = strikes.Select(k => Payoff(k) / k - prime).ToList();
        double putsConstante = lignes.Where(l => l.Jambe.Type == TypeOption.Put).Sum(l => l.QuantiteSignee * l.Jambe.Strike);
        double putsPente = -lignes.Where(l => l.Jambe.Type == TypeOption.Put).Sum(l => l.QuantiteSignee);
        valeursCrypto.Add(Math.Abs(putsConstante) < 1e-9 ? putsPente - prime : putsConstante > 0 ? double.PositiveInfinity : double.NegativeInfinity);
        valeursCrypto.Add(penteFinale);
        return new ProfilStructure
        {
            Points = points,
            PrimeBtc = prime,
            PrimeUsd = prime * reference,
            Future = reference,
            GainMaxUsd = penteFinale > 1e-12 ? null : valeursUsd.Max(),
            PerteMaxUsd = penteFinale < -1e-12 ? null : valeursUsd.Min(),
            GainMaxBtc = double.IsPositiveInfinity(valeursCrypto.Max()) ? null : valeursCrypto.Max(),
            PerteMaxBtc = double.IsNegativeInfinity(valeursCrypto.Min()) ? null : valeursCrypto.Min(),
            PointsMorts = racines.OrderBy(r => r).ToList()
        };
    }

    /// <summary>
    /// The CTA's only scenario is built from the desk's point of view, with its customer
    /// premium and without adding the cover instruments.
    /// </summary>
    public static ProfilStructure PourOptionSurMesure(PropositionCouverture p, IContexteMarche contexte,
        double minimum = -0.30, double maximum = 0.30, double pas = 0.05)
    {
        var structure = new Structure(TypeStructure.OptionSurMesure, [p.Option]);
        var sensDesk = (Sens)(-(int)p.SensClient);
        var cote = PricerStructure.Coter(structure, sensDesk, p.Quantite, contexte);
        return Calculer(cote, contexte, minimum, maximum, pas, p.QuantiteDesk * p.PrixClientBtc);
    }
}
