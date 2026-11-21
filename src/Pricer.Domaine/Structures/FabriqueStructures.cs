using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Structures;

/// <summary>On décrit une jambe de recette par son type, le numéro du strike saisi et son ratio signé.</summary>
public readonly record struct GabaritJambe(TypeOption Type, int IndiceStrike, double Ratio);

/// <summary>On rassemble les règles de construction d'une stratégie pour vérifier ses strikes avant de créer les jambes.</summary>
public sealed record RecetteStructure(string Nom, int NombreStrikes, bool Symetrique, IReadOnlyList<GabaritJambe> Jambes);

/// <summary>
/// On a choisi une Factory de recettes pour centraliser la création et la validation des structures. On
/// peut ainsi ajouter une recette au catalogue sans réécrire le pricer ni le book.
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
    /// On vérifie le nombre, l'ordre et, si nécessaire, la symétrie des strikes avant de construire une
    /// structure cotée.
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

    /// <summary>On crée un call ou un put coté seul en vérifiant que le strike existe au référentiel.</summary>
    public static Structure CreerOptionSimple(TypeOption typeOption, DateTime echeance, double strike, ReferentielOptions referentiel) =>
        new(TypeStructure.OptionSimple, [new JambeCotee(ValidateurStructure.ValiderStrikeCote(referentiel, echeance, strike, typeOption), +1)]);

    /// <summary>
    /// On crée une option sur mesure et on vérifie immédiatement que ses paramètres peuvent être interpolés
    /// dans la plage cotée.
    /// </summary>
    public static Structure CreerSurMesure(TypeOption typeOption, DateTime echeance, double strike, IContexteMarche contexte)
    {
        var jambe = new JambeSurMesure(typeOption, strike, echeance, +1);
        jambe.Interpoler(contexte); // On refuse immédiatement une demande en dehors de la plage cotée.
        return new Structure(TypeStructure.OptionSurMesure, [jambe]);
    }

    /// <summary>
    /// On reconstitue une structure à partir de jambes connues tout en vérifiant qu'elles partagent la même
    /// échéance.
    /// </summary>
    public static Structure Reconstruire(TypeStructure type, IEnumerable<Jambe> jambes)
    {
        var liste = jambes.ToList();
        ValidateurStructure.ValiderEcheanceUnique(liste);
        return new Structure(type, liste);
    }
}
