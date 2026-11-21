using Pricer.Infrastructure.Transport;

namespace Pricer.Integration.Tests.Infrastructure;

/// <summary>Tests unitaires du seau à jetons, avec une horloge manuelle (aucune attente réelle).</summary>
[TestClass]
public class LimiteurDebitTests
{
    private TimeSpan _maintenant = TimeSpan.Zero;

    /// <summary>
    /// On vérifie qu'une rafale autorisée ne permet pas de dépasser les jetons disponibles.
    /// </summary>
    [TestMethod]
    public void TenterPrendre_RafaleConsommee_PuisRefus()
    {
        var limiteur = new LimiteurDebit(5, 3, () => _maintenant);

        Assert.IsTrue(limiteur.TenterPrendre(out _));
        Assert.IsTrue(limiteur.TenterPrendre(out _));
        Assert.IsTrue(limiteur.TenterPrendre(out _));
        Assert.IsFalse(limiteur.TenterPrendre(out var attente));
        Assert.AreEqual(0.2, attente.TotalSeconds, 1e-9);
    }

    /// <summary>
    /// On vérifie que le temps écoulé reconstitue progressivement la capacité d'appel.
    /// </summary>
    [TestMethod]
    public void TenterPrendre_ApresAttente_JetonsReconstitues()
    {
        var limiteur = new LimiteurDebit(5, 3, () => _maintenant);
        for (int i = 0; i < 3; i++) limiteur.TenterPrendre(out _);

        _maintenant += TimeSpan.FromSeconds(0.4);

        Assert.AreEqual(2, limiteur.JetonsDisponibles, 1e-9);
        Assert.IsTrue(limiteur.TenterPrendre(out _));
    }

    /// <summary>
    /// On vérifie qu'une attente prolongée ne permet pas d'accumuler une rafale sans limite.
    /// </summary>
    [TestMethod]
    public void JetonsDisponibles_NeDepassentJamaisLaRafale()
    {
        var limiteur = new LimiteurDebit(5, 3, () => _maintenant);
        _maintenant += TimeSpan.FromHours(1);
        Assert.AreEqual(3, limiteur.JetonsDisponibles, 1e-9);
    }

    /// <summary>
    /// On vérifie qu'un limiteur configuré sans débit est rejeté dès sa création.
    /// </summary>
    [TestMethod]
    public void Constructeur_DebitNul_LeveArgumentOutOfRange()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new LimiteurDebit(0, 1));
    }
}
