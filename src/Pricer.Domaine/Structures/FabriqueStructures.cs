using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Structures;

/// <summary>
/// A recipe leg is described by its type, the number of the strike entered, and its signed
/// ratio.
/// </summary>
public readonly record struct GabaritJambe(TypeOption Type, int IndiceStrike, double Ratio);

/// <summary>
/// The building rules of a strategy are assembled to check your strikes before you create your
/// legs.
/// </summary>
public sealed record RecetteStructure(string Nom, int NombreStrikes, bool Symetrique, IReadOnlyList<GabaritJambe> Jambes);

/// <summary>
/// A recipe factory has been chosen to centralize the creation and validation of the
/// structures, so you can add a recipe to the catalogue without rewriting the prize or the
/// book.
/// </summary>
public static class FabriqueStructures
{
    private static readonly Dictionary<TypeStructure, RecetteStructure> Recettes = new()
    {
        [TypeStructure.CallSpread] = new("Call spread", 2, false, [new(TypeOption.Call, 0, +1), new(TypeOption.Call, 1, -1)]),
        [TypeStructure.PutSpread] = new("Put spread", 2, false, [new(TypeOption.Put, 1, +1), new(TypeOption.Put, 0, -1)]),
        [TypeStructure.ButterflyCall] = new("Butterfly call", 3, true, [new(TypeOption.Call, 0, +1), new(TypeOption.Call, 1, -2), new(TypeOption.Call, 2, +1)]),
        [TypeStructure.ButterflyPut] = new("Butterfly put", 3, true, [new(TypeOption.Put, 0, +1), new(TypeOption.Put, 1, -2), new(TypeOption.Put, 2, +1)]),
        [TypeStructure.Straddle] = new("Straddle", 1, false, [new(TypeOption.Call, 0, +1), new(TypeOption.Put, 0, +1)]),
        [TypeStructure.Strangle] = new("Strangle", 2, false, [new(TypeOption.Put, 0, +1), new(TypeOption.Call, 1, +1)]),
        [TypeStructure.RiskReversal] = new("Risk reversal", 2, false, [new(TypeOption.Put, 0, -1), new(TypeOption.Call, 1, +1)]),
    };

    public static IReadOnlyDictionary<TypeStructure, RecetteStructure> Catalogue => Recettes;

    /// <summary>
    /// The number, order and, if necessary, symmetry of the strikes are checked before
    /// building a rated structure.
    /// </summary>
    public static Structure Creer(TypeStructure type, DateTime echeance, IReadOnlyList<double> strikes, ReferentielOptions referentiel)
    {
        if (!Recettes.TryGetValue(type, out var recette))
            throw new SaisieInvalideException($"Le type {type} ne se crée pas par strikes : utiliser CreerOptionSimple ou CreerSurMesure.");

        ValidateurStructure.ValiderNombreStrikes(type, strikes, recette.NombreStrikes);
        ValidateurStructure.ValiderOrdre(strikes);
        if (recette.Symetrique) ValidateurStructure.ValiderSymetrie(strikes);

        var jambes = recette.Jambes
            .Select(g => (Jambe)new JambeCotee(ValidateurStructure.ValiderStrikeCote(referentiel, echeance, strikes[g.IndiceStrike], g.Type), g.Ratio))
            .ToList();
        ValidateurStructure.ValiderEcheanceUnique(jambes);
        return new Structure(type, jambes);
    }

    /// <summary>
    /// We create a call or a put-side alone by checking that the strike exists at the
    /// repository.
    /// </summary>
    public static Structure CreerOptionSimple(TypeOption typeOption, DateTime echeance, double strike, ReferentielOptions referentiel) =>
        new(TypeStructure.OptionSimple, [new JambeCotee(ValidateurStructure.ValiderStrikeCote(referentiel, echeance, strike, typeOption), +1)]);

    /// <summary>
    /// We create a custom option and immediately check that its parameters can be interpolated
    /// in the sided range.
    /// </summary>
    public static Structure CreerSurMesure(TypeOption typeOption, DateTime echeance, double strike, IContexteMarche contexte)
    {
        var jambe = new JambeSurMesure(typeOption, strike, echeance, +1);
        jambe.Interpoler(contexte); // On refuse immédiatement une demande en dehors de la plage cotée.
        return new Structure(TypeStructure.OptionSurMesure, [jambe]);
    }

    /// <summary>
    /// A structure is reconstructed from known legs while checking that they share the same
    /// maturity.
    /// </summary>
    public static Structure Reconstruire(TypeStructure type, IEnumerable<Jambe> jambes)
    {
        var liste = jambes.ToList();
        ValidateurStructure.ValiderEcheanceUnique(liste);
        return new Structure(type, liste);
    }
}
