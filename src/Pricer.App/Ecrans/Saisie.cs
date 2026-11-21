using System.Globalization;
using Spectre.Console;

namespace Pricer.App.Ecrans;

/// <summary>
/// On vérifie les saisies avec TryParse et on redemande une valeur si nécessaire. On accepte la virgule et
/// le point pour les nombres, afin de faciliter l'utilisation en français.
/// </summary>
public static class Saisie
{
    /// <summary>
    /// On accepte le point ou la virgule décimale et on retire les espaces avant d’essayer de convertir
    /// le texte.
    /// </summary>
    /// <param name="valeur">On fournit le nombre lu lorsque la conversion réussit.</param>
    /// <returns>On renvoie vrai si le texte a pu être converti en nombre.</returns>
    public static bool EssayerLireNombre(string? texte, out double valeur) =>
        double.TryParse((texte ?? "").Trim().Replace(',', '.').Replace(" ", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out valeur);

    /// <summary>
    /// On redemande la saisie jusqu’à obtenir un nombre et on propose une valeur par défaut si elle est
    /// fournie.
    /// </summary>
    public static double Nombre(string question, double? defaut = null)
    {
        double valeur;
        bool ok;
        do
        {
            var prompt = new TextPrompt<string>(question);
            if (defaut is double d) prompt.DefaultValue(d.ToString(CultureInfo.InvariantCulture));
            ok = EssayerLireNombre(AnsiConsole.Prompt(prompt), out valeur);
            if (!ok) AnsiConsole.MarkupLine("[red]Nombre invalide, recommencez (exemple : 1.5).[/]");
        } while (!ok);
        return valeur;
    }

    /// <summary>
    /// On lit une liste de strikes séparés par des espaces, des points-virgules ou des barres obliques,
    /// puis on redemande toute la liste si une valeur est illisible.
    /// </summary>
    /// <returns>On conserve l’ordre saisi ; la validation de la structure est faite ensuite par le domaine.</returns>
    public static List<double> Strikes(string question)
    {
        List<double>? strikes = null;
        do
        {
            string texte = AnsiConsole.Ask<string>(question);
            var morceaux = texte.Split([' ', ';', '/'], StringSplitOptions.RemoveEmptyEntries);
            var lus = new List<double>();
            foreach (var m in morceaux)
            {
                if (!EssayerLireNombre(m, out double k)) { lus = null; break; }
                lus.Add(k);
            }
            if (lus is null || lus.Count == 0) AnsiConsole.MarkupLine("[red]Saisie invalide : des nombres séparés par des espaces (exemple : 70000 75000).[/]");
            else strikes = lus;
        } while (strikes is null);
        return strikes;
    }

    /// <summary>
    /// On accepte une date française ou ISO, avec une heure facultative. On retient 08:00 UTC lorsque
    /// seule la date est saisie.
    /// </summary>
    /// <returns>On renvoie la date d’échéance interprétée en UTC.</returns>
    public static DateTime DateEcheance(string question)
    {
        DateTime resultat;
        bool ok;
        do
        {
            string texte = AnsiConsole.Ask<string>(question);
            ok = DateTime.TryParseExact(texte.Trim(), ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "dd/MM/yyyy", "dd/MM/yyyy HH:mm"],
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out resultat);
            if (ok && resultat.TimeOfDay == TimeSpan.Zero && !texte.Contains(':')) resultat = resultat.AddHours(8); // On retient 08:00 UTC si seule la date est saisie, conformément au règlement quotidien utilisé dans le book.
            if (!ok) AnsiConsole.MarkupLine("[red]Date invalide (formats : 2027-01-15 ou 15/01/2027 08:00).[/]");
        } while (!ok);
        return resultat;
    }
}
