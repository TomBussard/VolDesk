using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;

namespace Pricer.Infrastructure.Transport;

/// <summary>
/// A reproducible fictitious market is constructed, with parameters specific to BTC or ETH.
/// </summary>
public static class GenerateurDemonstration
{
    /// <summary>
    /// We write a compressed JSONL session containing a repository, REST responses and
    /// replicable fictitious quotes.
    /// </summary>
    /// <param name="fichier">On crée ou remplace ce fichier gzip avec les données de démonstration.</param>
    /// <param name="devise">On accepte BTC ou ETH et on adapte les prix, les volatilités et les pas de quantité.</param>
    public static void Creer(string fichier, string devise)
    {
        if (devise is not "BTC" and not "ETH") throw new ArgumentException("Devise BTC ou ETH attendue.");
        double spot = devise == "BTC" ? 100000 : 3500;
        double pas = devise == "BTC" ? 0.1 : 1;
        double lotFuture = devise == "BTC" ? 10 : 1;
        var debut = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        DateTime[] dates = [new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc), new(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc), new(2026, 12, 25, 8, 0, 0, DateTimeKind.Utc), new(2027, 3, 26, 8, 0, 0, DateTimeKind.Utc)];
        var options = new List<(string Nom, double Strike, DateTime Date, TypeOption Type)>();
        foreach (var date in dates)
            for (int k = 6; k <= 14; k++)
                foreach (var type in new[] { TypeOption.Call, TypeOption.Put })
                    options.Add(($"{devise}-{date.ToString("dMMMyy", CultureInfo.InvariantCulture).ToUpperInvariant()}-{spot * k / 10:0}-{(type == TypeOption.Call ? "C" : "P")}", spot * k / 10, date, type));
        // Future fictitious names are built with the same date format as the instruments.
        string NomFuture(DateTime date) => $"{devise}-{date.ToString("dMMMyy", CultureInfo.InvariantCulture).ToUpperInvariant()}";
        // The fictitious future is slightly changed and a portage linked to its maturity is added.
        double Future(DateTime date, int n) => spot * (1 + 0.004 * Math.Sin(n / 7.0)) * (1 + 0.03 * (date - debut).TotalDays / 365);
        // Consistent prices and sensitivities are calculated before adding a fictitious range.
        Dictionary<string, object?> Ticker(string nom, DateTime date, double strike, TypeOption? type, int n)
        {
            DateTime maintenant = debut.AddSeconds(3 * n);
            double f = Future(date, n);
            double sigma = (devise == "BTC" ? 0.5 : 0.62) + 0.3 * Math.Pow(Math.Log(Math.Max(1, strike) / f), 2);
            var r = type is null ? default : Black76.Calculer(f, strike, (date - maintenant).TotalDays / 365, sigma, type.Value);
            double mark = type is null ? f : r.PrixBtc;
            double ecart = type is null ? (devise == "BTC" ? 2.5 : 0.1) : Math.Max(0.00002, mark * 0.02);
            return new()
            {
                ["instrument_name"] = nom,
                ["timestamp"] = new DateTimeOffset(maintenant).ToUnixTimeMilliseconds(),
                ["best_bid_price"] = Math.Max(1e-10, mark - ecart),
                ["best_ask_price"] = mark + ecart,
                ["best_bid_amount"] = 100,
                ["best_ask_amount"] = 100,
                ["mark_price"] = mark,
                ["underlying_price"] = f,
                ["index_price"] = spot * (1 + 0.004 * Math.Sin(n / 7.0)),
                ["state"] = "open",
                ["mark_iv"] = type is null ? null : sigma * 100,
                ["bid_iv"] = type is null ? null : VolImplicite.CalculerDepuisBtc(Math.Max(1e-10, mark - ecart), f, strike, (date - maintenant).TotalDays / 365, type.Value).Sigma * 100,
                ["ask_iv"] = type is null ? null : VolImplicite.CalculerDepuisBtc(mark + ecart, f, strike, (date - maintenant).TotalDays / 365, type.Value).Sigma * 100,
                ["greeks"] = type is null ? null : new { delta = r.Grecques.Delta, gamma = r.Grecques.Gamma, vega = r.Grecques.Vega, theta = r.Grecques.Theta }
            };
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fichier))!);
        using var brut = File.Create(fichier);
        using var zip = new GZipStream(brut, CompressionLevel.Fastest);
        using var writer = new StreamWriter(zip);
        // A timed data per JSON line is written to keep the format expected by the replay.
        void Ecrire(DateTime t, string type, string cle, object donnees) => writer.WriteLine(JsonSerializer.Serialize(new { t, type, cle, donnees }));
        // Each fictional REST response is associated with the same key that was built during calls.
        void Rest(string methode, Dictionary<string, string> p, object d) => Ecrire(debut, "rest", CleRequete.Construire(methode, p), d);
        Rest("public/get_instruments", new() { { "currency", devise }, { "kind", "option" }, { "expired", "false" } }, options.Select(o => new { instrument_name = o.Nom, base_currency = devise, strike = o.Strike, expiration_timestamp = new DateTimeOffset(o.Date).ToUnixTimeMilliseconds(), option_type = o.Type == TypeOption.Call ? "call" : "put", contract_size = 1, min_trade_amount = pas, tick_size = 0.0001, is_active = true }));
        Rest("public/get_instruments", new() { { "currency", devise }, { "kind", "future" }, { "expired", "false" } }, dates.Select(d => new { instrument_name = NomFuture(d), base_currency = devise, expiration_timestamp = new DateTimeOffset(d).ToUnixTimeMilliseconds(), settlement_period = "month", contract_size = lotFuture, min_trade_amount = lotFuture, is_active = true }));
        // The fictitious ticker is repeated by also giving it the field names expected in the REST summary.
        Dictionary<string, object?> Resume(Dictionary<string, object?> t)
        {
            t["bid_price"] = t["best_bid_price"]; t["ask_price"] = t["best_ask_price"]; t["creation_timestamp"] = t["timestamp"]; return t;
        }
        Rest("public/get_book_summary_by_currency", new() { { "currency", devise }, { "kind", "option" } }, options.Select(o => Resume(Ticker(o.Nom, o.Date, o.Strike, o.Type, 0))));
        Rest("public/get_book_summary_by_currency", new() { { "currency", devise }, { "kind", "future" } }, dates.Select(d => Resume(Ticker(NomFuture(d), d, 0, null, 0))));
        Rest("public/get_index_price", new() { { "index_name", devise.ToLowerInvariant() + "_usd" } }, new { index_price = spot });
        Rest("public/get_delivery_prices", new() { { "index_name", devise.ToLowerInvariant() + "_usd" }, { "count", "30" } }, new { data = Array.Empty<object>() });
        foreach (var o in options) Rest("public/ticker", new() { { "instrument_name", o.Nom } }, Ticker(o.Nom, o.Date, o.Strike, o.Type, 0));
        foreach (var d in dates) Rest("public/ticker", new() { { "instrument_name", NomFuture(d) } }, Ticker(NomFuture(d), d, 0, null, 0));
        for (int n = 1; n <= 60; n++)
        {
            foreach (var o in options) Ecrire(debut.AddSeconds(3 * n), "ws", $"ticker.{o.Nom}.100ms", Ticker(o.Nom, o.Date, o.Strike, o.Type, n));
            foreach (var d in dates) Ecrire(debut.AddSeconds(3 * n), "ws", $"ticker.{NomFuture(d)}.100ms", Ticker(NomFuture(d), d, 0, null, n));
        }
    }
}
