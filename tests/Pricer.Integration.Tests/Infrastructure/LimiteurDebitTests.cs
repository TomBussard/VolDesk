using Pricer.Infrastructure.Transport;

namespace Pricer.Integration.Tests.Infrastructure;

/// <summary>
/// Unit tests of the token bucket, with a manual clock (no actual expectations).
/// </summary>
[TestClass]
public class LimiteurDebitTests
{
    private TimeSpan _maintenant = TimeSpan.Zero;

    /// <summary>
    /// It is verified that an authorized burst does not exceed the available tokens.
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
    /// It is verified that the time elapsed gradually reconstitutes the call capacity.
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
    /// It is verified that a prolonged wait does not accumulate a boundless gust.
    /// </summary>
    [TestMethod]
    public void JetonsDisponibles_NeDepassentJamaisLaRafale()
    {
        var limiteur = new LimiteurDebit(5, 3, () => _maintenant);
        _maintenant += TimeSpan.FromHours(1);
        Assert.AreEqual(3, limiteur.JetonsDisponibles, 1e-9);
    }

    /// <summary>
    /// We check that a flow-free configured limiter is rejected as soon as it is created.
    /// </summary>
    [TestMethod]
    public void Constructeur_DebitNul_LeveArgumentOutOfRange()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new LimiteurDebit(0, 1));
    }
}
