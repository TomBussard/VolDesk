using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Structures;

/// <summary>
/// On regroupe les contrôles de saisie pour refuser une demande incohérente avec un message précis avant de
/// la coter.
/// </summary>
public static class ValidateurStructure
{
    public const double ToleranceSymetrie = 1e-9;

    /// <summary>
    /// On vérifie le nombre de strikes attendu par la recette avant d'accéder à leurs positions dans la
    /// liste.
    /// </summary>
    public static void ValiderNombreStrikes(TypeStructure type, IReadOnlyList<double> strikes, int attendu)
    {
        if (strikes.Count != attendu)
            throw new SaisieInvalideException(CodeErreurSaisie.NombreStrikes,
                $"Un {type} demande {attendu} strike(s), {strikes.Count} saisi(s).");
    }

    /// <summary>
    /// On exige des strikes positifs, finis et strictement croissants pour garder les recettes et leurs
    /// signes cohérents.
    /// </summary>
    public static void ValiderOrdre(IReadOnlyList<double> strikes)
    {
        if (strikes.Any(k => !double.IsFinite(k) || k <= 0))
            throw new SaisieInvalideException("Les strikes doivent être positifs et finis.");
        for (int i = 1; i < strikes.Count; i++)
            if (strikes[i] <= strikes[i - 1])
                throw new SaisieInvalideException(CodeErreurSaisie.OrdreStrikes,
                    $"Strikes dans le mauvais ordre : {string.Join(" / ", strikes.Select(k => k.ToString("N0")))}. Ils doivent être strictement croissants.");
    }

    /// <summary>
    /// On compare les deux ailes du butterfly avec une tolérance numérique pour refuser une construction
    /// asymétrique.
    /// </summary>
    public static void ValiderSymetrie(IReadOnlyList<double> strikes)
    {
        double aile1 = strikes[1] - strikes[0], aile2 = strikes[2] - strikes[1];
        if (Math.Abs(aile1 - aile2) > ToleranceSymetrie)
            throw new SaisieInvalideException(CodeErreurSaisie.ButterflyNonSymetrique,
                $"Butterfly non symétrique : K2 − K1 = {aile1:N0} mais K3 − K2 = {aile2:N0}. Les deux ailes doivent être égales.");
    }

    /// <summary>
    /// On vérifie la date et le contrat demandé ; en cas d'absence, on indique les strikes voisins pour
    /// faciliter la correction.
    /// </summary>
    /// <returns>L'instrument coté correspondant exactement à la demande.</returns>
    public static Instrument ValiderStrikeCote(ReferentielOptions referentiel, DateTime echeance, double strike, TypeOption type)
    {
        if (!referentiel.EcheanceCotee(echeance))
            throw new SaisieInvalideException(CodeErreurSaisie.EcheanceInconnue, $"L'échéance {echeance:dd/MM/yyyy} n'est pas cotée sur Deribit.");
        return referentiel.Trouver(echeance, strike, type)
            ?? throw new SaisieInvalideException(CodeErreurSaisie.StrikeNonCote,
                $"Le strike {strike:N0} ({type}) n'est pas coté pour l'échéance {echeance:dd/MM/yyyy}. " +
                $"Strikes cotés les plus proches : {string.Join(", ", referentiel.StrikesLesPlusProches(echeance, strike).Select(k => k.ToString("N0")))}.");
    }

    /// <summary>
    /// On exige une quantité positive et multiple du pas ; le sens du trade porte séparément le signe de
    /// l'exposition.
    /// </summary>
    public static void ValiderQuantite(double quantite, double pas)
    {
        if (!double.IsFinite(quantite) || !(quantite > 0))
            throw new SaisieInvalideException(CodeErreurSaisie.QuantiteNonPositive, "La quantité doit être strictement positive (le sens porte le signe).");
        if (!quantite.EstMultipleDe(pas))
            throw new SaisieInvalideException(CodeErreurSaisie.QuantiteHorsPas,
                $"Quantité {quantite} hors du pas de cotation : elle doit être un multiple de {pas} (par exemple {quantite.ArrondirAuPas(pas)}).");
    }

    /// <summary>
    /// On refuse de mélanger plusieurs dates dans une même structure, dont le payoff suppose une échéance
    /// commune.
    /// </summary>
    public static void ValiderEcheanceUnique(IEnumerable<Jambe> jambes)
    {
        if (jambes.Select(j => j.Echeance).Distinct().Count() > 1)
            throw new SaisieInvalideException(CodeErreurSaisie.EcheancesDifferentes, "Toutes les jambes d'une structure doivent avoir la même échéance.");
    }
}
