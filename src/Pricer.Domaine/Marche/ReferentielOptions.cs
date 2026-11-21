using Pricer.Domaine.Exceptions;
using Pricer.Domaine.Modeles;

namespace Pricer.Domaine.Marche;

/// <summary>
/// On rassemble le call et le put d'un même strike pour faciliter l'affichage et le choix d'une option de
/// remplacement.
/// </summary>
public sealed record PaireStrike(double Strike, Instrument? Call, Instrument? Put)
{
    /// <summary>
    /// On choisit le call ou le put du strike pour laisser le même parcours traiter les deux types
    /// d'options.
    /// </summary>
    public Instrument? Option(TypeOption type) => type == TypeOption.Call ? Call : Put;
}

/// <summary>
/// On classe les options par échéance puis par strike. On a choisi un dictionnaire pour les recherches par
/// nom et des dictionnaires triés pour parcourir les strikes dans l'ordre.
/// </summary>
public sealed class ReferentielOptions
{
    private readonly SortedDictionary<DateTime, SortedDictionary<double, PaireStrike>> _parEcheance = new();
    private readonly Dictionary<string, Instrument> _parNom = new();
    private readonly List<InstrumentFuture> _futures;

    public string Devise { get; }

    /// <summary>
    /// On indexe les options par nom, échéance et strike, puis on trie les futures pour faciliter les
    /// recherches.
    /// </summary>
    public ReferentielOptions(string devise, IEnumerable<Instrument> options, IEnumerable<InstrumentFuture> futures)
    {
        Devise = devise;
        foreach (var option in options)
        {
            _parNom[option.Nom] = option;
            if (!_parEcheance.TryGetValue(option.Echeance, out var strikes))
            {
                strikes = new SortedDictionary<double, PaireStrike>();
                _parEcheance[option.Echeance] = strikes;
            }
            strikes.TryGetValue(option.Strike, out var paire);
            paire ??= new PaireStrike(option.Strike, null, null);
            strikes[option.Strike] = option.Type == TypeOption.Call ? paire with { Call = option } : paire with { Put = option };
        }
        _futures = futures.OrderBy(f => f.Echeance).ToList();
    }

    public IReadOnlyList<DateTime> Echeances => _parEcheance.Keys.ToList();
    public IReadOnlyList<InstrumentFuture> Futures => _futures;
    public IEnumerable<Instrument> Options => _parNom.Values;
    public int NombreOptions => _parNom.Count;

    /// <summary>
    /// On parcourt les strikes d'une échéance dans l'ordre croissant et on refuse une date absente du
    /// référentiel.
    /// </summary>
    public IReadOnlyList<PaireStrike> Strikes(DateTime echeance) =>
        _parEcheance.TryGetValue(echeance, out var strikes)
            ? strikes.Values.ToList()
            : throw new SaisieInvalideException(CodeErreurSaisie.EcheanceInconnue, $"L'échéance {echeance:dd/MM/yyyy HH:mm} n'est pas cotée.");

    /// <summary>
    /// On vérifie qu'une date appartient au référentiel avant de rechercher ses strikes.
    /// </summary>
    public bool EcheanceCotee(DateTime echeance) => _parEcheance.ContainsKey(echeance);

    /// <summary>
    /// On recherche une option par date, strike et type sans substituer un contrat voisin.
    /// </summary>
    public Instrument? Trouver(DateTime echeance, double strike, TypeOption type) =>
        _parEcheance.TryGetValue(echeance, out var strikes) && strikes.TryGetValue(strike, out var paire) ? paire.Option(type) : null;

    /// <summary>
    /// On retrouve l'instrument par son nom ou on signale qu'il n'appartient pas au référentiel chargé.
    /// </summary>
    public Instrument Obtenir(string nom) =>
        _parNom.TryGetValue(nom, out var instrument) ? instrument : throw new DonneesIndisponiblesException($"Instrument inconnu : {nom}.");

    /// <summary>
    /// On vérifie la présence d'un nom d'option sans déclencher une exception de recherche.
    /// </summary>
    public bool Contient(string nom) => _parNom.ContainsKey(nom);

    /// <summary>
    /// On cherche d'abord le future de même échéance, puis le future daté le plus proche ; le perpétuel ne sert
    /// qu'en dernier recours.
    /// </summary>
    public InstrumentFuture FuturePourEcheance(DateTime echeance)
    {
        var dates = _futures.Where(f => !f.EstPerpetuel).ToList();
        var exact = dates.FirstOrDefault(f => f.Echeance == echeance);
        if (exact is not null) return exact;
        var proche = dates.OrderBy(f => Math.Abs((f.Echeance - echeance).TotalSeconds)).FirstOrDefault();
        return proche ?? _futures.FirstOrDefault() ?? throw new DonneesIndisponiblesException("Aucun future dans le référentiel.");
    }

    /// <summary>On propose les strikes voisins dans le message de refus pour aider l'utilisateur à corriger sa saisie.</summary>
    public IReadOnlyList<double> StrikesLesPlusProches(DateTime echeance, double strike, int nombre = 3) =>
        _parEcheance.TryGetValue(echeance, out var strikes)
            ? strikes.Keys.OrderBy(k => Math.Abs(k - strike)).Take(nombre).OrderBy(k => k).ToList()
            : [];
}
