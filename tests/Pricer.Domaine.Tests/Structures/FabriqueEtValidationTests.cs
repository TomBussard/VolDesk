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
    /// On vérifie le sens des deux jambes qui définit un call spread acheté.
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
    /// On vérifie que le put spread retient la bonne combinaison de strikes et de signes.
    /// </summary>
    [TestMethod]
    public void Creer_PutSpread_AchatDuPutHautVenteDuPutBas()
    {
        var s = FabriqueStructures.Creer(TypeStructure.PutSpread, MarcheDeTest.E1, [66000, 72000], _ref);
        Assert.AreEqual(1.0, s.Jambes.Single(j => j.Strike == 72000).Ratio);
        Assert.AreEqual(-1.0, s.Jambes.Single(j => j.Strike == 66000).Ratio);
    }

    /// <summary>
    /// On vérifie le poids double de la jambe centrale dans la recette du butterfly.
    /// </summary>
    [TestMethod]
    public void Creer_ButterflyPut_Ratios1Moins2Plus1()
    {
        var s = FabriqueStructures.Creer(TypeStructure.ButterflyPut, MarcheDeTest.E2, [70000, 74000, 78000], _ref);
        CollectionAssert.AreEqual(new[] { 1.0, -2.0, 1.0 }, s.Jambes.Select(j => j.Ratio).ToArray());
        Assert.IsTrue(s.Jambes.All(j => j.Type == TypeOption.Put));
    }

    /// <summary>
    /// On vérifie qu'un butterfly asymétrique est refusé avec le code d'erreur attendu.
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
    /// On vérifie qu'une recette ne peut pas être construite avec des strikes inversés.
    /// </summary>
    [TestMethod]
    public void Creer_StrikesDansLeMauvaisOrdre_LeveSaisieInvalide()
    {
        var ex = Assert.ThrowsException<SaisieInvalideException>(() =>
            FabriqueStructures.Creer(TypeStructure.CallSpread, MarcheDeTest.E1, [80000, 72000], _ref));
        Assert.AreEqual(CodeErreurSaisie.OrdreStrikes, ex.CodeErreur);
    }

    /// <summary>
    /// On vérifie que le refus d'un strike absent fournit une indication utile pour corriger la saisie.
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
    /// On vérifie qu'une structure cotée doit utiliser une maturité du référentiel.
    /// </summary>
    [TestMethod]
    public void Creer_EcheanceNonCotee_LeveSaisieInvalide()
    {
        var ex = Assert.ThrowsException<SaisieInvalideException>(() =>
            FabriqueStructures.Creer(TypeStructure.CallSpread, new DateTime(2026, 11, 21, 8, 0, 0, DateTimeKind.Utc), [72000, 80000], _ref));
        Assert.AreEqual(CodeErreurSaisie.EcheanceInconnue, ex.CodeErreur);
    }

    /// <summary>
    /// On vérifie que la recette reçoit exactement les strikes nécessaires à ses jambes.
    /// </summary>
    [TestMethod]
    public void Creer_MauvaisNombreDeStrikes_LeveSaisieInvalide()
    {
        var ex = Assert.ThrowsException<SaisieInvalideException>(() =>
            FabriqueStructures.Creer(TypeStructure.ButterflyCall, MarcheDeTest.E1, [70000, 74000], _ref));
        Assert.AreEqual(CodeErreurSaisie.NombreStrikes, ex.CodeErreur);
    }

    /// <summary>
    /// On vérifie que le pas de négociation s'applique avant tout traitement de la quantité.
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
    /// On vérifie que le sens du trade ne peut pas être contourné par une quantité nulle ou négative.
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
    /// On vérifie qu'une quantité conforme est acceptée malgré les petites erreurs d'arrondi binaire.
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
    /// On vérifie que les recettes supplémentaires passent par la même Factory et les mêmes contrôles.
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
    /// On vérifie qu'une demande OTC impossible à interpoler est refusée dès sa construction.
    /// </summary>
    [TestMethod]
    public void CreerSurMesure_EcheanceHorsPlage_LeveHorsPlageException()
    {
        var etat = MarcheDeTest.Etat();
        Assert.ThrowsException<HorsPlageException>(() =>
            FabriqueStructures.CreerSurMesure(TypeOption.Call, new DateTime(2027, 6, 1, 8, 0, 0, DateTimeKind.Utc), 75000, etat));
    }
}
