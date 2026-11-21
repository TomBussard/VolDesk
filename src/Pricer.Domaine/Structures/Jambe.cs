using System.Globalization;
using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Marche;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Utilitaires;

namespace Pricer.Domaine.Structures;

/// <summary>On regroupe les paramètres qui décrivent une jambe à l'instant de son évaluation.</summary>
public readonly record struct ParametresMarche(double Future, double Sigma, double Maturite, DateTime Horodatage);

/// <summary>
/// On représente chaque structure par des jambes et des ratios signés du point de vue de son acheteur : +1
/// achète une option, -2 en vend deux. On garde le bid/ask dans une interface réservée aux jambes cotées,
/// car un OTC n'a pas de carnet Deribit.
/// </summary>
public abstract class Jambe
{
    /// <summary>
    /// On conserve les caractéristiques communes de la jambe et son ratio signé pour séparer le contrat de
    /// la quantité de structure.
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

    /// <summary>On laisse chaque type de jambe obtenir son future et sa volatilité selon sa propre règle.</summary>
    public abstract ParametresMarche ParametresMarche(IContexteMarche contexte);

    /// <summary>
    /// On applique le même Black-76 après avoir obtenu les paramètres de la jambe, qu'elle soit cotée ou sur
    /// mesure.
    /// </summary>
    public virtual ResultatBlack76 Evaluer(IContexteMarche contexte)
    {
        var p = ParametresMarche(contexte);
        return Black76.Calculer(p.Future, Strike, p.Maturite, p.Sigma, Type);
    }

    /// <summary>
    /// On réévalue la jambe à un future choqué, en conservant sa volatilité et sa durée pour le profil
    /// d'aujourd'hui.
    /// </summary>
    public double PrixUsdAvecFuture(IContexteMarche contexte, double futureChoque)
    {
        var p = ParametresMarche(contexte);
        return Black76.Prix(futureChoque, Strike, p.Maturite, p.Sigma, Type);
    }

    /// <summary>On calcule le payoff USD à l'échéance pour le prix du sous-jacent choisi dans le scénario.</summary>
    public double PayoffUsd(double sousJacent) =>
        Type == TypeOption.Call ? Math.Max(sousJacent - Strike, 0) : Math.Max(Strike - sousJacent, 0);

    /// <summary>
    /// On affiche le ratio signé avec le libellé pour rendre lisibles les achats et ventes qui composent la
    /// structure.
    /// </summary>
    public override string ToString() => $"{(Ratio > 0 ? "+" : "")}{Ratio} {Libelle}";
}

/// <summary>On réserve l'accès au bid, à l'ask et au mark aux jambes réellement cotées sur Deribit.</summary>
public interface ICotableSurMarche
{
    Instrument Instrument { get; }
    /// <summary>
    /// On demande la cotation d'une jambe réellement négociée pour accéder à son bid, son ask et son mark.
    /// </summary>
    Cotation ObtenirCotation(IContexteMarche contexte);
}

/// <summary>On lit underlying_price et mark_iv pour une option cotée, puis on les utilise dans notre calcul Black-76.</summary>
public sealed class JambeCotee : Jambe, ICotableSurMarche
{
    /// <summary>
    /// On rattache la jambe à un instrument du référentiel tout en conservant son ratio dans la structure.
    /// </summary>
    public JambeCotee(Instrument instrument, double ratio)
        : base(instrument.Type, instrument.Strike, instrument.Echeance, ratio)
    {
        Instrument = instrument;
    }

    public Instrument Instrument { get; }
    public override string Libelle => Instrument.Nom;

    /// <summary>
    /// On lit la cotation de l'instrument et on signale son absence avant tout calcul ou choix de prix
    /// exécutable.
    /// </summary>
    public Cotation ObtenirCotation(IContexteMarche contexte) =>
        contexte.Cotation(Instrument.Nom) ?? throw new DonneesIndisponiblesException($"Pas de cotation reçue pour {Instrument.Nom}.");

    /// <summary>
    /// On contrôle la fraîcheur, le future et mark_iv du contrat coté, puis on mesure sa maturité depuis
    /// l'horodatage de cette cotation.
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
/// On obtient les paramètres d'une option de gré à gré par interpolation, car son strike et sa date ne
/// correspondent pas nécessairement à un instrument coté.
/// </summary>
public sealed class JambeSurMesure : Jambe
{
    /// <summary>
    /// On décrit l'option OTC par son type, son strike et sa date sans lui attribuer de carnet Deribit.
    /// </summary>
    public JambeSurMesure(TypeOption type, double strike, DateTime echeance, double ratio)
        : base(type, strike, echeance, ratio) { }

    public override string Libelle =>
        string.Create(CultureInfo.InvariantCulture, $"OTC-{Echeance:dMMMyy}-{Strike:0}-{(Type == TypeOption.Call ? "C" : "P")}").ToUpperInvariant();

    /// <summary>
    /// On demande les paramètres OTC aux smiles du marché en conservant le détail nécessaire pour expliquer
    /// l'interpolation.
    /// </summary>
    public ResultatInterpolation Interpoler(IContexteMarche contexte) =>
        Interpolateur.Interpoler(Strike, Echeance, contexte.Maintenant, contexte.Smiles());

    /// <summary>
    /// On transforme le résultat interpolé en paramètres Black-76, datés de l'instant de marché courant.
    /// </summary>
    public override ParametresMarche ParametresMarche(IContexteMarche contexte)
    {
        var r = Interpoler(contexte);
        return new ParametresMarche(r.Future, r.Sigma, r.Maturite, contexte.Maintenant);
    }
}
