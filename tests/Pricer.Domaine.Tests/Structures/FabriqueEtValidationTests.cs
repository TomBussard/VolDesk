using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Tests.Outils;

namespace Pricer.Domaine.Tests.Structures;

[TestClass]
public class FabriqueEtValidationTests
{
    private readonly Pricer.Domaine.Marche.ReferentielOptions _ref = MarcheDeTest.Referentiel();

    /// <summary>
    /// We check the sense of both legs that defines a purchased call spread.
    /// </summary>
    [TestMethod]
    public void Creer_CallSpread_AchatDuStrikeBasVenteDuStrikeHaut()
    {
        var s = FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [72000, 80000], _ref);

        Assert.AreEqual(2, s.Jambes.Count);
        Assert.AreEqual((72000.0, 1.0), (s.Jambes[0].Strike, s.Jambes[0].Ratio));
        Assert.AreEqual((80000.0, -1.0), (s.Jambes[1].Strike, s.Jambes[1].Ratio));
        Assert.IsTrue(s.Jambes.All(j => j.Type == TypeOption.Call && j is JambeCotee));
    }

    /// <summary>
    /// We check that the put spread retains the right combination of strikes and signs.
    /// </summary>
    [TestMethod]
    public void Creer_PutSpread_AchatDuPutHautVenteDuPutBas()
    {
        var s = FabriqueStructures.Creer(TypeStructure.PutSpread, MarcheDeTest.E1, [66000, 72000], _ref);
        Assert.AreEqual(1.0, s.Jambes.Single(j => j.Strike == 72000).Ratio);
        Assert.AreEqual(-1.0, s.Jambes.Single(j => j.Strike == 66000).Ratio);
    }

    /// <summary>
    /// The double weight of the central leg is checked in the butterfly recipe.
    /// </summary>
    [TestMethod]
    public void Creer_ButterflyPut_Ratios1Moins2Plus1()
    {
        var s = FabriqueStructures.Creer(TypeStructure.ButterflyPut, MarcheDeTest.E2, [70000, 74000, 78000], _ref);
        CollectionAssert.AreEqual(new[] { 1.0, -2.0, 1.0 }, s.Jambes.Select(j => j.Ratio).ToArray());
        Assert.IsTrue(s.Jambes.All(j => j.Type == TypeOption.Put));
    }

    /// <summary>
    /// We check that an asymmetric butterfly is refused with the expected error code.
    /// </summary>
    [TestMethod]
    public void Creer_ButterflyNonSymetrique_LeveSaisieInvalideAvecCode()
    {
        var ex = Assert.ThrowsException<SaisieInvalideException>(() =>
            FabriqueStructures.Creer(TypeStructure.ButterflyCall, MarcheDeTest.E1, [70000, 74000, 80000], _ref));
        Assert.AreEqual(CodeErreurSaisie.ButterflyNonSymetrique, ex.CodeErreur);
        StringAssert.Contains(ex.Message, "non symétrique");
    }

    /// <summary>
    /// We check that a recipe can't be built with inverted strikes.
    /// </summary>
    [TestMethod]
    public void Creer_StrikesDansLeMauvaisOrdre_LeveSaisieInvalide()
    {
        var ex = Assert.ThrowsException<SaisieInvalideException>(() =>
            FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [80000, 72000], _ref));
        Assert.AreEqual(CodeErreurSaisie.OrdreStrikes, ex.CodeErreur);
    }

    /// <summary>
    /// It is verified that the refusal of an absent strike provides a useful indication to
    /// correct the seizure.
    /// </summary>
    [TestMethod]
    public void Creer_StrikeNonCote_MessageAvecLesStrikesLesPlusProches()
    {
        var ex = Assert.ThrowsException<SaisieInvalideException>(() =>
            FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [73000, 80000], _ref));
        Assert.AreEqual(CodeErreurSaisie.StrikeNonCote, ex.CodeErreur);
        StringAssert.Contains(ex.Message, "72");
    }

    /// <summary>
    /// It is verified that a rated structure must use a maturity of the repository.
    /// </summary>
    [TestMethod]
    public void Creer_EcheanceNonCotee_LeveSaisieInvalide()
    {
        var ex = Assert.ThrowsException<SaisieInvalideException>(() =>
            FabriqueStructures.Creer(TypeStructure.CallSpread, new DateTime(2026, 11, 21, 8, 0, 0, DateTimeKind.Utc), [72000, 80000], _ref));
        Assert.AreEqual(CodeErreurSaisie.EcheanceInconnue, ex.CodeErreur);
    }

    /// <summary>
    /// We check that the recipe receives exactly the necessary strikes for its legs.
    /// </summary>
    [TestMethod]
    public void Creer_MauvaisNombreDeStrikes_LeveSaisieInvalide()
    {
        var ex = Assert.ThrowsException<SaisieInvalideException>(() =>
            FabriqueStructures.Creer(TypeStructure.ButterflyCall, MarcheDeTest.E1, [70000, 74000], _ref));
        Assert.AreEqual(CodeErreurSaisie.NombreStrikes, ex.CodeErreur);
    }

    /// <summary>
    /// It is verified that the trading step applies before processing the quantity.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.15)]
    [DataRow(1.05)]
    public void ValiderQuantite_HorsPasDeCotation_LeveSaisieInvalide(double quantite)
    {
        var ex = Assert.ThrowsException<SaisieInvalideException>(() => ValidateurStructure.ValiderQuantite(quantite, 0.1));
        Assert.AreEqual(CodeErreurSaisie.QuantiteHorsPas, ex.CodeErreur);
    }

    /// <summary>
    /// It is verified that the direction of the trade cannot be circumvented by a zero or
    /// negative amount.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.0)]
    [DataRow(-1.0)]
    public void ValiderQuantite_NulleOuNegative_LeveSaisieInvalide(double quantite)
    {
        var ex = Assert.ThrowsException<SaisieInvalideException>(() => ValidateurStructure.ValiderQuantite(quantite, 0.1));
        Assert.AreEqual(CodeErreurSaisie.QuantiteNonPositive, ex.CodeErreur);
    }

    /// <summary>
    /// It is verified that a consistent quantity is accepted despite small binary rounding
    /// errors.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.1)]
    [DataRow(0.3)]
    [DataRow(12.7)]
    public void ValiderQuantite_MultipleDuPas_Accepte(double quantite)
    {
        ValidateurStructure.ValiderQuantite(quantite, 0.1);
    }

    /// <summary>
    /// It is verified that the additional revenues go through the same Factory and the same
    /// controls.
    /// </summary>
    [TestMethod]
    public void Catalogue_StructuresBonus_CreeesSansModifierLeCodeAppelant()
    {
        foreach (var (type, recette) in FabriqueStructures.Catalogue)
        {
            double[] strikes = recette.NombreStrikes switch { 1 => [74000], 2 => [70000, 78000], _ => [70000, 74000, 78000] };
            var s = FabriqueStructures.Creer(type, MarcheDeTest.E2, strikes, _ref);
            Assert.AreEqual(type, s.Type);
        }
    }

    /// <summary>
    /// It is verified that an OTC application that cannot be interpolated is refused as soon as
    /// it is constructed.
    /// </summary>
    [TestMethod]
    public void CreerSurMesure_EcheanceHorsPlage_LeveHorsPlageException()
    {
        var etat = MarcheDeTest.Etat();
        Assert.ThrowsException<HorsPlageException>(() =>
            FabriqueStructures.CreerSurMesure(TypeOption.Call, new DateTime(2027, 6, 1, 8, 0, 0, DateTimeKind.Utc), 75000, etat));
    }
}
