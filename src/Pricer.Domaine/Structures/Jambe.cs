using System.Globalization;
using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Structures;

/// <summary>
/// The parameters that describe a leg at the moment of its evaluation are grouped.
/// </summary>
public readonly record struct ParametresMarche(double Future, double Sigma, double Maturite, DateTime Horodatage);

/// <summary>
/// Each structure is represented by legs and ratios signed from the buyer's point of view: +1
/// buys an option, -2 sells two. Bid/ask is kept in an interface reserved for rated legs, as an
/// OTC does not have a Deribit notebook.
/// </summary>
public abstract class Jambe
{
    /// <summary>
    /// The common characteristics of the leg and its signed ratio to separate the contract from
    /// the amount of structure are retained.
    /// </summary>
    protected Jambe(TypeOption type, double strike, DateTime echeance, double ratio)
    {
        Type = type;
        Strike = strike;
        Echeance = echeance;
        Ratio = ratio;
    }

    public TypeOption Type { get; }
    public double Strike { get; }
    public DateTime Echeance { get; }
    public double Ratio { get; }

    public abstract string Libelle { get; }

    /// <summary>
    /// Each type of leg is allowed to obtain its future and its volatility according to its own
    /// rule.
    /// </summary>
    public abstract ParametresMarche ParametresMarche(IContexteMarche contexte);

    /// <summary>
    /// The same Black-76 is applied after having obtained the parameters of the leg, whether it
    /// is rated or tailored.
    /// </summary>
    public virtual ResultatBlack76 Evaluer(IContexteMarche contexte)
    {
        var p = ParametresMarche(contexte);
        return Black76.Calculer(p.Future, Strike, p.Maturite, p.Sigma, Type);
    }

    /// <summary>
    /// We re-evaluate the leg to a future shock, keeping its volatility and duration for
    /// today's profile.
    /// </summary>
    public double PrixUsdAvecFuture(IContexteMarche contexte, double futureChoque)
    {
        var p = ParametresMarche(contexte);
        return Black76.Prix(futureChoque, Strike, p.Maturite, p.Sigma, Type);
    }

    /// <summary>
    /// The USD payoff is calculated at maturity for the price of the underlying chosen in the
    /// scenario.
    /// </summary>
    public double PayoffUsd(double sousJacent) =>
        Type == TypeOption.Call ? Math.Max(sousJacent - Strike, 0) : Math.Max(Strike - sousJacent, 0);

    /// <summary>
    /// The signed-to-text ratio is displayed to make the purchases and sales that make up the
    /// structure readable.
    /// </summary>
    public override string ToString() => $"{(Ratio > 0 ? "+" : "")}{Ratio} {Libelle}";
}

/// <summary>
/// Access to the bid, ask and mark is reserved for the legs actually listed on Deribit.
/// </summary>
public interface ICotableSurMarche
{
    Instrument Instrument { get; }
    /// <summary>
    /// A really negotiated leg is asked to be rated to access its bid, its Ask and its mark.
    /// </summary>
    Cotation ObtenirCotation(IContexteMarche contexte);
}

/// <summary>
/// We read underlying_price and mark_iv for a rated option, then we use them in our Black-76
/// calculation.
/// </summary>
public sealed class JambeCotee : Jambe, ICotableSurMarche
{
    /// <summary>
    /// The leg is attached to an instrument in the repository while maintaining its ratio in
    /// the structure.
    /// </summary>
    public JambeCotee(Instrument instrument, double ratio)
        : base(instrument.Type, instrument.Strike, instrument.Echeance, ratio)
    {
        Instrument = instrument;
    }

    public Instrument Instrument { get; }
    public override string Libelle => Instrument.Nom;

    /// <summary>
    /// The rating of the instrument is read and its absence is reported before any calculation
    /// or choice of executable price.
    /// </summary>
    public Cotation ObtenirCotation(IContexteMarche contexte) =>
        contexte.Cotation(Instrument.Nom) ?? throw new DonneesIndisponiblesException($"Pas de cotation reçue pour {Instrument.Nom}.");

    /// <summary>
    /// We control the freshness, the future and mark_iv of the rated contract, and then we
    /// measure its maturity since the time stamping of this rating.
    /// </summary>
    public override ParametresMarche ParametresMarche(IContexteMarche contexte)
    {
        var cotation = ObtenirCotation(contexte);
        cotation.VerifierFraicheur(contexte.Maintenant);
        double future = cotation.Future is double f && double.IsFinite(f) && f > 0 ? f
            : throw new DonneesIndisponiblesException($"Future absent ou invalide pour {Instrument.Nom}.");
        double sigma = (cotation.MarkIv ?? throw new DonneesIndisponiblesException($"mark_iv absent pour {Instrument.Nom}.")) / 100;
        if (!double.IsFinite(sigma) || sigma < 0 || sigma > 5)
            throw new DonneesIndisponiblesException($"Volatilité du mark invalide pour {Instrument.Nom}.");
        return new ParametresMarche(future, sigma, Maturite.EnAnnees(Echeance, cotation.Horodatage), cotation.Horodatage);
    }
}

/// <summary>
/// The parameters of an over-the-counter option are obtained by interpolation, as its strike
/// and date do not necessarily correspond to a sided instrument.
/// </summary>
public sealed class JambeSurMesure : Jambe
{
    /// <summary>
    /// The OTC option is described by its type, strike and date without assigning a Deribit
    /// notebook.
    /// </summary>
    public JambeSurMesure(TypeOption type, double strike, DateTime echeance, double ratio)
        : base(type, strike, echeance, ratio) { }

    public override string Libelle =>
        string.Create(CultureInfo.InvariantCulture, $"OTC-{Echeance:dMMMyy}-{Strike:0}-{(Type == TypeOption.Call ? "C" : "P")}").ToUpperInvariant();

    /// <summary>
    /// The OTC parameters are asked to the market smiles by keeping the details necessary to
    /// explain the interpolation.
    /// </summary>
    public ResultatInterpolation Interpoler(IContexteMarche contexte) =>
        Interpolateur.Interpoler(Strike, Echeance, contexte.Maintenant, contexte.Smiles());

    /// <summary>
    /// The interpolated result is converted into Black-76 parameters, dated from the current
    /// market moment.
    /// </summary>
    public override ParametresMarche ParametresMarche(IContexteMarche contexte)
    {
        var r = Interpoler(contexte);
        return new ParametresMarche(r.Future, r.Sigma, r.Maturite, contexte.Maintenant);
    }
}
