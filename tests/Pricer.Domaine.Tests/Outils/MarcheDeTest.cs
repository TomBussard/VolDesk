using System.Globalization;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Tests.Outils;

/// <summary>
/// Deterministic market lining: three maturities, 60,000 to 90,000 strikes per 2000 step,
/// parametric smile, ±2% bid/ask around the mark. No network dependencies or system clocks.
/// </summary>
public static class MarcheDeTest
{
    public static readonly DateTime Maintenant = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime E1 = Maturite.EcheanceDeribit(new DateOnly(2026, 11, 20));
    public static readonly DateTime E2 = Maturite.EcheanceDeribit(new DateOnly(2026, 12, 25));
    public static readonly DateTime E3 = Maturite.EcheanceDeribit(new DateOnly(2027, 3, 26));
    public const double Indice = 73900;

    public static readonly IReadOnlyDictionary<DateTime, double> Futures = new Dictionary<DateTime, double>
    {
        [E1] = 74000,
        [E2] = 74500,
        [E3] = 75500
    };

    /// <summary>
    /// A deterministic smile is defined to know the expected volatility of each test option.
    /// </summary>
    public static double Vol(DateTime echeance, double strike)
    {
        double k = Math.Log(strike / Futures[echeance]);
        double t = Maturite.EnAnnees(echeance, Maintenant);
        return 0.45 + 0.30 * k * k - 0.05 * k + 0.02 * t;
    }

    /// <summary>
    /// An instrument name conforming to the dates and strikes of the test frame is
    /// constructed.
    /// </summary>
    public static string Nom(DateTime e, double k, TypeOption t) =>
        string.Create(CultureInfo.InvariantCulture, $"BTC-{e:dMMMyy}-{k:0}-{(t == TypeOption.Call ? "C" : "P")}").ToUpperInvariant();

    /// <summary>
    /// Several expiries and strikes are created to test recipes and interpolation without
    /// network calls.
    /// </summary>
    public static ReferentielOptions Referentiel()
    {
        var options = new List<Instrument>();
        foreach (var e in Futures.Keys)
            for (double k = 60000; k <= 90000; k += 2000)
                foreach (var t in new[] { TypeOption.Call, TypeOption.Put })
                    options.Add(new Instrument { Nom = Nom(e, k, t), Devise = "BTC", Strike = k, Echeance = e, Type = t });

        var futures = Futures.Keys.Select(e => new InstrumentFuture { Nom = string.Create(CultureInfo.InvariantCulture, $"BTC-{e:dMMMyy}").ToUpperInvariant(), Devise = "BTC", Echeance = e })
            .Append(new InstrumentFuture { Nom = "BTC-PERPETUAL", Devise = "BTC", Echeance = DateTime.MaxValue });
        return new ReferentielOptions("BTC", options, futures);
    }

    /// <summary>
    /// A consistent contract is completed and the test is allowed to change only the case it
    /// wants to verify.
    /// </summary>
    public static EtatMarche Etat(Action<EtatMarche>? personnaliser = null)
    {
        var referentiel = Referentiel();
        var etat = new EtatMarche(referentiel, new HorlogeFixe(Maintenant));
        foreach (var o in referentiel.Options) etat.MettreAJour(CotationOption(o));
        foreach (var f in referentiel.Futures)
        {
            double prix = f.EstPerpetuel ? 73950 : Futures[f.Echeance];
            etat.MettreAJour(new Cotation { NomInstrument = f.Nom, Horodatage = Maintenant, Bid = prix - 2.5, Ask = prix + 2.5, Mark = prix, Indice = Indice });
        }
        personnaliser?.Invoke(etat);
        return etat;
    }

    /// <summary>
    /// We produce bid, ask, mark and Greeks consistent with the known volatility of the test
    /// instrument.
    /// </summary>
    public static Cotation CotationOption(Instrument o)
    {
        double f = Futures[o.Echeance];
        double t = Maturite.EnAnnees(o.Echeance, Maintenant);
        double s = Vol(o.Echeance, o.Strike);
        var r = Black76.Calculer(f, o.Strike, t, s, o.Type);
        double bid = r.PrixBtc * 0.98, ask = r.PrixBtc * 1.02 + 0.0001;
        return new Cotation
        {
            NomInstrument = o.Nom,
            Horodatage = Maintenant,
            Mark = r.PrixBtc,
            Bid = bid,
            Ask = ask,
            MarkIv = s * 100,
            BidIv = VolImplicite.CalculerDepuisBtc(bid, f, o.Strike, t, o.Type).Sigma * 100,
            AskIv = VolImplicite.CalculerDepuisBtc(ask, f, o.Strike, t, o.Type).Sigma * 100,
            Future = f,
            Indice = Indice,
            GrecquesDeribit = r.Grecques
        };
    }
}
