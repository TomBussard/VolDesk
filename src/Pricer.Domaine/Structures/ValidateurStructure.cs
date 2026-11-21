using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Structures;

/// <summary>
/// The input controls are grouped together to refuse an inconsistent request with a specific
/// message before rating it.
/// </summary>
public static class ValidateurStructure
{
    public const double ToleranceSymetrie = 1e-9;

    /// <summary>
    /// We check the number of strikes expected by the recipe before accessing their positions
    /// in the list.
    /// </summary>
    public static void ValiderNombreStrikes(TypeStructure type, IReadOnlyList<double> strikes, int attendu)
    {
        if (strikes.Count != attendu)
            throw new SaisieInvalideException(CodeErreurSaisie.NombreStrikes,
                $"Un {type} demande {attendu} strike(s), {strikes.Count} saisi(s).");
    }

    /// <summary>
    /// Positive, finite and strictly growing strikes are required to keep the recipes and
    /// their signs consistent.
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
    /// The two wings of the butterfly are compared with a numerical tolerance to refuse
    /// asymmetrical construction.
    /// </summary>
    public static void ValiderSymetrie(IReadOnlyList<double> strikes)
    {
        double aile1 = strikes[1] - strikes[0], aile2 = strikes[2] - strikes[1];
        if (Math.Abs(aile1 - aile2) > ToleranceSymetrie)
            throw new SaisieInvalideException(CodeErreurSaisie.ButterflyNonSymetrique,
                $"Butterfly non symétrique : K2 − K1 = {aile1:N0} mais K3 − K2 = {aile2:N0}. Les deux ailes doivent être égales.");
    }

    /// <summary>
    /// The requested date and contract is checked; in case of absence, the neighbouring
    /// strikes are indicated to facilitate the correction.
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
    /// A positive and multiple step is required; the direction of the trade bears the sign of
    /// exposure separately.
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
    /// Several dates are refused to be mixed into the same structure, the payoff of which
    /// assumes a common expiry.
    /// </summary>
    public static void ValiderEcheanceUnique(IEnumerable<Jambe> jambes)
    {
        if (jambes.Select(j => j.Echeance).Distinct().Count() > 1)
            throw new SaisieInvalideException(CodeErreurSaisie.EcheancesDifferentes, "Toutes les jambes d'une structure doivent avoir la même échéance.");
    }
}
