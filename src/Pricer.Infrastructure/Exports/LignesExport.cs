using Pricer.Domaine.Booking;
using Pricer.Domaine.Modeles;

namespace Pricer.Infrastructure.Exports;

/// <summary>
/// On présente chaque jambe du blotter sur une ligne pour conserver ses paramètres dans un export
/// facilement relisible.
/// </summary>
public sealed record LigneBlotter
{
    public required string IdTrade { get; init; }
    public required string Horodatage { get; init; }
    public required string Nature { get; init; }
    public string? IdTradeOrigine { get; init; }
    public required string Structure { get; init; }
    public required string Description { get; init; }
    public required string Sens { get; init; }
    public required double Quantite { get; init; }
    public required string Instrument { get; init; }
    public required string NatureJambe { get; init; }
    public required double QuantiteSignee { get; init; }
    public required double PrixExecution { get; init; }
    public required double FutureAuTrade { get; init; }
    public double? VolAuTrade { get; init; }
    public required double PrimeTradeBtc { get; init; }
    public Guid IdJambe { get; init; }
    public Guid? IdJambeOrigine { get; init; }
    public double? NotionnelUsd { get; init; }
    public string Devise { get; init; } = "BTC";
}

/// <summary>
/// On regroupe sur une ligne les valeurs et le résultat d'une structure d'origine pour lire l'état du book
/// dans les exports.
/// </summary>
public sealed record LigneBook
{
    public required string IdTrade { get; init; }
    public required string Description { get; init; }
    public required string Echeance { get; init; }
    public required double QuantiteOuverte { get; init; }
    public required double? ValeurMarkBtc { get; init; }
    public required double? ValeurTheoriqueBtc { get; init; }
    public required double PrimeOuverteBtc { get; init; }
    public required double? PnlLatentBtc { get; init; }
    public required double PnlRealiseBtc { get; init; }
    public required double? PnlTotalBtc { get; init; }
    public required double? PnlTotalUsd { get; init; }
    public required double Delta { get; init; }
    public required double Gamma { get; init; }
    public required double Vega { get; init; }
    public required double Theta { get; init; }
    public string Statut { get; init; } = "";
    public string SourceMark { get; init; } = "";
    public bool RisqueComplet { get; init; }
    public string Devise { get; init; } = "BTC";
}

/// <summary>
/// On rassemble les grecques d'une échéance pour distinguer les risques portés par les différentes
/// maturités.
/// </summary>
public sealed record LigneGrecques(string Echeance, double Delta, double Gamma, double Vega, double Theta, string Statut = "Complet");
/// <summary>On détaille le solde d’une jambe ouverte, son notionnel éventuel et son statut dans le book.</summary>
public sealed record LignePositionJambe(string IdTrade, Guid IdJambe, string Instrument, string Nature,
    string Echeance, double QuantiteRestante, double? NotionnelRestantUsd, double PrixEntree, string Statut);
/// <summary>On associe chaque mesure globale à son unité et à sa disponibilité dans l’export de synthèse.</summary>
public sealed record LigneSynthese(string Mesure, string Unite, double? Valeur, string Statut);

/// <summary>
/// On prépare les lignes des exports dans des méthodes d'extension pour partager la même présentation
/// entre les formats.
/// </summary>
public static class ExtensionsExport
{
    private const string FormatDate = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// On produit une ligne par jambe d’une opération en conservant les références qui relient les
    /// fermetures à leur ouverture.
    /// </summary>
    public static IEnumerable<LigneBlotter> VersLignes(this Trade t) => t.Jambes.Select(j => new LigneBlotter
    {
        IdTrade = t.Id.ToString(),
        Horodatage = t.Horodatage.ToString(FormatDate, System.Globalization.CultureInfo.InvariantCulture),
        Nature = t.Nature.ToString(),
        IdTradeOrigine = t.IdTradeOrigine?.ToString(),
        Structure = t.TypeStructure.ToString(),
        Description = t.Description,
        Sens = t.Sens.ToString(),
        Quantite = t.Quantite,
        Instrument = j.Instrument,
        NatureJambe = j.Nature.ToString(),
        QuantiteSignee = j.QuantiteSignee,
        PrixExecution = j.PrixExecution,
        FutureAuTrade = j.Future,
        VolAuTrade = j.Vol,
        PrimeTradeBtc = t.PrimeBtc,
        IdJambe = j.Id,
        IdJambeOrigine = j.IdJambeOrigine,
        NotionnelUsd = j.Nature == NatureJambe.Future ? j.NotionnelUsd : null,
        Devise = t.Devise
    });

    /// <summary>
    /// On exporte une ligne par position et on laisse les valorisations indisponibles vides au lieu
    /// d’écrire un nombre trompeur.
    /// </summary>
    public static IEnumerable<LigneBook> VersLignes(this EtatBook e) => e.Positions.Select(p => new LigneBook
    {
        IdTrade = p.Origine.Id.ToString(),
        Description = p.Origine.Description,
        Echeance = p.Origine.Echeance.ToString(FormatDate, System.Globalization.CultureInfo.InvariantCulture),
        QuantiteOuverte = p.QuantiteOuverte,
        ValeurMarkBtc = Disponible(p.ValeurMarkBtc),
        ValeurTheoriqueBtc = Disponible(p.ValeurTheoriqueBtc),
        PrimeOuverteBtc = p.PrimeOuverteBtc,
        PnlLatentBtc = Disponible(p.PnlLatentBtc),
        PnlRealiseBtc = p.PnlRealiseBtc,
        PnlTotalBtc = Disponible(p.PnlTotalBtc),
        PnlTotalUsd = Disponible(p.PnlTotalBtc * e.Indice),
        Delta = p.Grecques.Delta,
        Gamma = p.Grecques.Gamma,
        Vega = p.Grecques.Vega,
        Theta = p.Grecques.Theta,
        Statut = p.Statut,
        SourceMark = p.SourceMark,
        RisqueComplet = p.RisqueComplet,
        Devise = e.Devise
    });

    /// <summary>
    /// On exporte les sensibilités par échéance puis leur total, en précisant si le risque calculé est
    /// partiel.
    /// </summary>
    public static IEnumerable<LigneGrecques> VersLignesGrecques(this EtatBook e) =>
        e.GrecquesParEcheance.Select(kv => new LigneGrecques(kv.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), kv.Value.Delta, kv.Value.Gamma, kv.Value.Vega, kv.Value.Theta, e.RisqueComplet ? "Complet" : "Partiel"))
         .Append(new LigneGrecques("TOTAL", e.GrecquesTotales.Delta, e.GrecquesTotales.Gamma, e.GrecquesTotales.Vega, e.GrecquesTotales.Theta, e.RisqueComplet ? "Complet" : "Partiel"));

    /// <summary>
    /// On détaille les soldes encore ouverts et le notionnel restant des futures pour suivre les
    /// couvertures résiduelles.
    /// </summary>
    public static IEnumerable<LignePositionJambe> VersJambesOuvertes(this EtatBook e) => e.Positions.SelectMany(p =>
        p.JambesOuvertes.Select(x => new LignePositionJambe(p.Origine.Id.ToString(), x.Jambe.Id, x.Jambe.Instrument,
            x.Jambe.Nature.ToString(), x.Jambe.Echeance.ToString(FormatDate), x.QuantiteOuverte,
            x.Jambe.Nature == NatureJambe.Future ? x.Jambe.NotionnelUsd * x.QuantiteOuverte / x.Jambe.QuantiteSignee : null,
            x.Jambe.PrixExecution, p.Statut)));

    /// <summary>
    /// On produit les mesures globales avec leur unité et leur statut pour garder un export lisible même
    /// si une donnée manque.
    /// </summary>
    public static IEnumerable<LigneSynthese> VersSynthese(this EtatBook e)
    {
        yield return new("Horodatage", "UTC", null, e.Horodatage.ToString(FormatDate));
        yield return new("Indice", "USD", Disponible(e.Indice), "");
        yield return new("Valeur mark", e.Devise, Disponible(e.ValeurMarkBtc), e.Statut);
        yield return new("Valeur théorique", e.Devise, Disponible(e.ValeurTheoriqueBtc), e.Statut);
        yield return new("P&L latent", e.Devise, Disponible(e.PnlLatentBtc), e.Statut);
        yield return new("P&L réalisé", e.Devise, e.PnlRealiseBtc, "");
        yield return new("P&L total", e.Devise, Disponible(e.PnlTotalBtc), e.Statut);
        yield return new("P&L total", "USD", Disponible(e.PnlTotalUsd), e.Statut);
        string risque = e.RisqueComplet ? "Complet" : "Partiel";
        yield return new("Delta", "sous-jacent", e.GrecquesTotales.Delta, risque);
        yield return new("Gamma", "par USD", e.GrecquesTotales.Gamma, risque);
        yield return new("Vega", "USD / point", e.GrecquesTotales.Vega, risque);
        yield return new("Theta", "USD / jour", e.GrecquesTotales.Theta, risque);
    }
    /// <summary>
    /// On remplace une valeur non finie par une valeur absente, afin que les exports ne présentent pas
    /// une valorisation invalide comme un résultat.
    /// </summary>
    private static double? Disponible(double x) => double.IsFinite(x) ? x : null;
}
