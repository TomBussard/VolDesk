using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Pricing;

/// <summary>
/// On conserve les quantités avant et après arrondi d'un instrument de couverture pour montrer l'effet du
/// pas de négociation.
/// </summary>
public sealed record LigneCouverture
{
    public required string Nom { get; init; }
    public required NatureJambe Nature { get; init; }
    public required double QuantiteAvantArrondi { get; init; }
    /// <summary>
    /// On exprime les quantités affichées en unités de sous-jacent, y compris l'équivalent crypto du montant
    /// USD du future.
    /// </summary>
    public required double Quantite { get; init; }
    /// <summary>On conserve un prix exécutable en crypto pour une option et en USD pour le future.</summary>
    public required double PrixExecutable { get; init; }
    public required double Mid { get; init; }
    public required Grecques GrecquesUnitaires { get; init; }
    /// <summary>On mesure le coût de fourchette par l'écart au mid, pondéré par la quantité ; on l'exprime en crypto.</summary>
    public required double CoutBtc { get; init; }
    public double? Strike { get; init; }
    public DateTime Echeance { get; init; }
    public TypeOption? Type { get; init; }
    public double Future { get; init; }
    public double Sigma { get; init; }
    public double PasQuantite { get; init; } = 0.1;
    public double PasNotionnelUsd { get; init; } = 10;
}

/// <summary>
/// On rassemble la cotation OTC, les instruments retenus et les risques résiduels pour vérifier toute la
/// proposition avant de traiter.
/// </summary>
public sealed record PropositionCouverture
{
    public required JambeSurMesure Option { get; init; }
    public required Sens SensClient { get; init; }
    public required double Quantite { get; init; }
    /// <summary>On prend le point de vue du desk : quand le client achète, la quantité OTC du desk est négative.</summary>
    public required double QuantiteDesk { get; init; }
    public required ResultatInterpolation Interpolation { get; init; }
    public required ResultatBlack76 ValeurOption { get; init; }
    public required IReadOnlyList<LigneCouverture> Lignes { get; init; }
    public required double Determinant { get; init; }
    public required double DeterminantNormalise { get; init; }
    public required Grecques GrecquesGlobalesAvantArrondi { get; init; }
    public required double MargeBtc { get; init; }
    public required double Indice { get; init; }

    public Grecques GrecquesOption => QuantiteDesk * ValeurOption.Grecques;
    public Grecques GrecquesCouverture => Lignes.Aggregate(Grecques.Zero, (a, l) => a + l.Quantite * l.GrecquesUnitaires);
    public Grecques GrecquesGlobales => GrecquesOption + GrecquesCouverture;
    public double CoutCouvertureBtc => Lignes.Sum(l => l.CoutBtc);

    /// <summary>
    /// On propose au client le prix théorique, ajusté du coût de couverture par unité et de la marge selon le
    /// sens demandé.
    /// </summary>
    public double PrixClientBtc => ValeurOption.PrixBtc + (int)SensClient * (CoutCouvertureBtc / Quantite + MargeBtc);
    public double PrixClientUsd => PrixClientBtc * Interpolation.Future;
}

/// <summary>
/// On couvre d'abord gamma et vega avec deux options cotées, puis le delta avec un future. On affiche les
/// risques résiduels car les pas de négociation empêchent parfois une neutralité exacte.
/// </summary>
public static class MoteurCouverture
{
    /// <summary>
    /// On refuse un déterminant normalisé trop faible pour éviter des quantités énormes sur une paire presque
    /// singulière.
    /// </summary>
    public const double DeterminantNormaliseMinimal = 1e-6;
    public const double DeterminantNormaliseConfortable = 0.05;

    /// <summary>
    /// On résout le système gamma/vega par la règle de Cramer, comme un système de deux équations à deux
    /// inconnues. On refuse la paire lorsque le déterminant est trop faible.
    /// </summary>
    /// <param name="a">La matrice 2 × 2 : gammas sur la première ligne, vegas sur la seconde.</param>
    /// <param name="b">Les deux risques à compenser, dans l'ordre gamma puis vega.</param>
    /// <param name="determinant">Le déterminant signé du système, conservé pour expliquer le calcul.</param>
    /// <param name="determinantNormalise">Le déterminant absolu divisé par la somme absolue des deux produits croisés.</param>
    /// <returns>Les quantités signées des deux options, avant arrondi à leurs pas de négociation.</returns>
    public static double[] ResoudreSysteme2x2(double[,] a, double[] b, out double determinant, out double determinantNormalise)
    {
        determinant = a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0];
        double echelle = Math.Abs(a[0, 0] * a[1, 1]) + Math.Abs(a[0, 1] * a[1, 0]);
        determinantNormalise = echelle > 0 ? Math.Abs(determinant) / echelle : 0;
        if (determinantNormalise < DeterminantNormaliseMinimal || !double.IsFinite(determinantNormalise))
            throw new CouvertureImpossibleException(
                "Cette paire d'options ne permet pas de neutraliser à la fois le gamma et le vega : " +
                "leurs ratios vega/gamma sont (quasi) identiques, le système est singulier. Choisissez deux options d'échéances différentes.",
                determinant);

        return
        [
            (b[0] * a[1, 1] - a[0, 1] * b[1]) / determinant,
            (a[0, 0] * b[1] - a[1, 0] * b[0]) / determinant
        ];
    }

    /// <summary>
    /// On calcule la couverture sur la paire précise choisie par l'utilisateur, avec les mêmes contrôles que le
    /// choix automatique.
    /// </summary>
    /// <param name="option">Le contrat OTC dont les paramètres seront interpolés sur les smiles courants.</param>
    /// <param name="sensClient">Le sens du client ; le desk porte la position opposée sur l'OTC.</param>
    /// <param name="quantite">La quantité positive demandée par le client, conforme au pas de la devise.</param>
    /// <param name="couverture1">La première option cotée pour compenser gamma et vega.</param>
    /// <param name="couverture2">La seconde option cotée, distincte de la première.</param>
    /// <param name="contexte">Le marché qui fournit les cotations et l'instant du calcul.</param>
    /// <param name="margeVolPoints">La marge par unité exprimée en points de volatilité, convertie avec le vega.</param>
    /// <returns>Les instruments négociables, les coûts et les risques résiduels avant et après arrondi.</returns>
    public static PropositionCouverture Calculer(
        JambeSurMesure option, Sens sensClient, double quantite,
        Instrument couverture1, Instrument couverture2,
        IContexteMarche contexte, double margeVolPoints = 0.5)
    {
        if (couverture1.Nom == couverture2.Nom)
            throw new CouvertureImpossibleException("Les deux options de couverture doivent être différentes.");

        ValidateurStructure.ValiderQuantite(quantite, contexte.Referentiel.Devise == "ETH" ? 1 : 0.1);
        if (!double.IsFinite(margeVolPoints) || margeVolPoints < 0)
            throw new CouvertureImpossibleException("La marge doit être positive ou nulle et finie.");
        var interpolation = option.Interpoler(contexte);
        var valeur = Black76.Calculer(interpolation.Future, option.Strike, interpolation.Maturite, interpolation.Sigma, option.Type);
        double q0 = -(int)sensClient * quantite;
        double indice = contexte.Indice ?? interpolation.Future;

        var h1 = Unitaire(couverture1, contexte);
        var h2 = Unitaire(couverture2, contexte);

        // On résout gamma et vega ensemble : un seul instrument ne suffit généralement pas à annuler les deux.
        double[,] a = { { h1.Valeur.Grecques.Gamma, h2.Valeur.Grecques.Gamma }, { h1.Valeur.Grecques.Vega, h2.Valeur.Grecques.Vega } };
        double[] b = { -q0 * valeur.Grecques.Gamma, -q0 * valeur.Grecques.Vega };
        double[] q = ResoudreSysteme2x2(a, b, out double det, out double detNorm);

        // On calcule aussi le delta théorique avant arrondi pour le comparer aux résidus réellement négociables.
        double qF = -(q0 * valeur.Grecques.Delta + q[0] * h1.Valeur.Grecques.Delta + q[1] * h2.Valeur.Grecques.Delta);
        var avantArrondi = q0 * valeur.Grecques + q[0] * h1.Valeur.Grecques + q[1] * h2.Valeur.Grecques + new Grecques(qF, 0, 0, 0);

        var lignes = new List<LigneCouverture>
        {
            LigneOption(couverture1, h1, q[0]),
            LigneOption(couverture2, h2, q[1])
        };
        // On neutralise le delta après l'arrondi des deux options, puis on arrondit le notionnel du future.
        double deltaOptions = q0 * valeur.Grecques.Delta + lignes.Sum(l => l.Quantite * l.GrecquesUnitaires.Delta);
        lignes.Add(LigneFuture(option.Echeance, -deltaOptions, contexte, indice));
        double prixClient = valeur.PrixBtc + (int)sensClient * (lignes.Sum(l => l.CoutBtc) / quantite + margeVolPoints * valeur.Grecques.Vega / interpolation.Future);
        if (!double.IsFinite(prixClient) || prixClient <= 0)
            throw new CouvertureImpossibleException("Le coût de couverture ne permet pas un prix client positif.");

        return new PropositionCouverture
        {
            Option = option,
            SensClient = sensClient,
            Quantite = quantite,
            QuantiteDesk = q0,
            Interpolation = interpolation,
            ValeurOption = valeur,
            Lignes = lignes,
            Determinant = det,
            DeterminantNormalise = detNorm,
            GrecquesGlobalesAvantArrondi = avantArrondi,
            MargeBtc = margeVolPoints * valeur.Grecques.Vega / interpolation.Future,
            Indice = indice
        };
    }

    /// <summary>
    /// On cherche des options hors de la monnaie proches du strike équivalent sur des échéances distinctes. Le
    /// rapport vega/gamma dépend notamment de la maturité ; on privilégie une paire bien conditionnée, puis le
    /// coût parmi les propositions acceptables.
    /// </summary>
    public static PropositionCouverture CalculerMeilleure(
        JambeSurMesure option, Sens sensClient, double quantite, IContexteMarche contexte,
        double margeVolPoints = 0.5, int candidatsParEcheance = 4)
    {
        var interpolation = option.Interpoler(contexte);
        var echeances = new List<DateTime> { interpolation.Echeance1.Echeance };
        if (interpolation.Echeance2 is not null) echeances.Add(interpolation.Echeance2.Echeance);
        else
        {
            var suivante = contexte.Smiles().Select(s => s.Echeance).Where(e => e > interpolation.Echeance1.Echeance).OrderBy(e => e).FirstOrDefault();
            var precedente = contexte.Smiles().Select(s => s.Echeance).Where(e => e < interpolation.Echeance1.Echeance && e > contexte.Maintenant).OrderBy(e => e).LastOrDefault();
            if (suivante != default) echeances.Add(suivante);
            else if (precedente != default) echeances.Add(precedente);
        }

        var candidats = echeances.SelectMany(e => Candidats(e, interpolation.LogMoneyness, contexte, candidatsParEcheance)).Distinct().ToList();
        var propositions = new List<PropositionCouverture>();
        for (int i = 0; i < candidats.Count; i++)
            for (int j = i + 1; j < candidats.Count; j++)
            {
                try { propositions.Add(Calculer(option, sensClient, quantite, candidats[i], candidats[j], contexte, margeVolPoints)); }
                catch (PricerException) { /* On écarte une paire infaisable et on poursuit la recherche parmi les autres candidats. */ }
            }

        if (propositions.Count == 0)
            throw new CouvertureImpossibleException("Aucune paire d'options cotées (avec bid et ask) ne permet de couvrir cette option.");

        var confortables = propositions.Where(p => p.DeterminantNormalise >= DeterminantNormaliseConfortable).ToList();
        return confortables.Count > 0
            ? confortables.OrderBy(p => p.CoutCouvertureBtc + Math.Abs(p.GrecquesGlobales.Vega) / p.Interpolation.Future).First()
            : propositions.OrderByDescending(p => p.DeterminantNormalise).First();
    }

    /// <summary>
    /// On cherche les options hors de la monnaie les plus proches du strike équivalent, avec bid, ask et IV
    /// disponibles.
    /// </summary>
    private static IEnumerable<Instrument> Candidats(DateTime echeance, double logMoneyness, IContexteMarche contexte, int nombre)
    {
        double f = contexte.Future(echeance) ?? 0;
        if (f <= 0) return [];
        double kEquivalent = f * Math.Exp(logMoneyness);
        return contexte.Referentiel.Strikes(echeance)
            .Select(p => p.Strike >= f ? p.Call ?? p.Put : p.Put ?? p.Call)
            .Where(i => i is not null && contexte.Cotation(i.Nom) is { Bid: not null, Ask: not null, MarkIv: not null })
            .Select(i => i!)
            .OrderBy(i => Math.Abs(i.Strike - kEquivalent))
            .Take(nombre);
    }

    /// <summary>On conserve ensemble le carnet et les paramètres du calcul pour construire une ligne de couverture cohérente.</summary>
    private sealed record ValeurUnitaire(Cotation Cotation, ResultatBlack76 Valeur, double Future, double Sigma);

    /// <summary>
    /// On contrôle le carnet et la fraîcheur de l'option de couverture avant de calculer ses grecques
    /// unitaires.
    /// </summary>
    private static ValeurUnitaire Unitaire(Instrument instrument, IContexteMarche contexte)
    {
        var jambe = new JambeCotee(instrument, 1);
        var cotation = jambe.ObtenirCotation(contexte);
        if (!instrument.Actif || instrument.Etat != "open" || !cotation.FourchetteValide)
            throw new CouvertureImpossibleException($"{instrument.Nom} n'a pas de bid et d'ask : il ne peut pas servir de couverture.");
        var p = jambe.ParametresMarche(contexte);
        return new ValeurUnitaire(cotation, Black76.Calculer(p.Future, instrument.Strike, p.Maturite, p.Sigma, instrument.Type), p.Future, p.Sigma);
    }

    /// <summary>
    /// On arrondit la quantité d'option, on choisit le côté exécutable et on mesure le coût de fourchette en
    /// crypto.
    /// </summary>
    private static LigneCouverture LigneOption(Instrument instrument, ValeurUnitaire h, double quantiteBrute)
    {
        double q = quantiteBrute.ArrondirAuPas(instrument.PasQuantite);
        double executable = q >= 0 ? h.Cotation.Ask!.Value : h.Cotation.Bid!.Value;
        double mid = h.Cotation.Mid!.Value;
        return new LigneCouverture
        {
            Nom = instrument.Nom,
            Nature = NatureJambe.OptionCotee,
            QuantiteAvantArrondi = quantiteBrute,
            Quantite = q,
            PrixExecutable = executable,
            Mid = mid,
            GrecquesUnitaires = h.Valeur.Grecques,
            CoutBtc = Math.Abs(q) * Math.Abs(executable - mid),
            Strike = instrument.Strike,
            Echeance = instrument.Echeance,
            Type = instrument.Type,
            Future = h.Future,
            Sigma = h.Sigma,
            PasQuantite = instrument.PasQuantite
        };
    }

    /// <summary>
    /// On choisit le premier future daté négociable après l'OTC, puis on arrondit son montant USD et on
    /// calcule le coût inverse.
    /// </summary>
    /// <returns>Une ligne dont la quantité est l'équivalent crypto au prix d'exécution ; son delta unitaire de proposition vaut un.</returns>
    private static LigneCouverture LigneFuture(DateTime echeanceOption, double quantiteBrute, IContexteMarche contexte, double indice)
    {
        var future = contexte.Referentiel.Futures.Where(f => !f.EstPerpetuel && f.Echeance >= echeanceOption &&
            contexte.Cotation(f.Nom) is { } c && c.FourchetteValide && c.EstFraiche(contexte.Maintenant))
            .OrderBy(f => f.Echeance).FirstOrDefault()
            ?? throw new CouvertureImpossibleException("Aucun future daté avec bid/ask frais n'arrive après l'OTC.");
        var cotation = contexte.Cotation(future.Nom)!;
        double mid = cotation.Mid!.Value;
        double bid = cotation.Bid!.Value, ask = cotation.Ask!.Value;
        double executable = quantiteBrute >= 0 ? ask : bid;

        // On arrondit le montant USD au pas du future, puis on affiche son équivalent en unités de crypto.
        double montantUsd = (quantiteBrute * executable).ArrondirAuPas(future.QuantiteMinimaleUsd);
        double q = montantUsd / executable;
        return new LigneCouverture
        {
            Nom = future.Nom,
            Nature = NatureJambe.Future,
            QuantiteAvantArrondi = quantiteBrute,
            Quantite = q,
            PrixExecutable = executable,
            Mid = mid,
            GrecquesUnitaires = new Grecques(1, 0, 0, 0),
            CoutBtc = Math.Abs(montantUsd) * Math.Abs(1 / executable - 1 / mid),
            Echeance = future.Echeance,
            Future = mid,
            PasNotionnelUsd = future.QuantiteMinimaleUsd
        };
    }
}
