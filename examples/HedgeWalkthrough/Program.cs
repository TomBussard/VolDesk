using System.Globalization;
using Pricer.Domaine.Modeles;
using Pricer.Domaine.Pricing;
using Pricer.Domaine.Structures;
using Pricer.Domaine.Tests.Outils;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// Use the same frozen quotes as the hedge tests so the trade can be replayed offline.
var market = MarcheDeTest.Etat();
var expiry = new DateTime(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc);
var contract = new JambeSurMesure(TypeOption.Call, 77_000, expiry, 1);
var decemberCall = market.Referentiel.Trouver(MarcheDeTest.E2, 76_000, TypeOption.Call)!;
var marchCall = market.Referentiel.Trouver(MarcheDeTest.E3, 78_000, TypeOption.Call)!;
var quote = MoteurCouverture.Calculer(contract, Sens.Achat, 25, decemberCall, marchCall, market);

Console.WriteLine($"Forward {quote.Interpolation.Future:F2} USD, volatility {quote.Interpolation.Sigma:P4}");
Console.WriteLine($"Bracketing volatilities {quote.Interpolation.Sigma1:P4} and {quote.Interpolation.Sigma2:P4}");
Console.WriteLine($"Model premium {quote.ValeurOption.PrixBtc:F8} BTC, client premium {quote.PrixClientBtc:F8} BTC per option");
Console.WriteLine($"Total hedge cost {quote.CoutCouvertureBtc:F8} BTC, margin {quote.MargeBtc:F8} BTC per option");
Console.WriteLine("Hedge instrument                  Raw quantity    Executable quantity");
foreach (var leg in quote.Lignes)
    Console.WriteLine($"{leg.Nom,-31} {leg.QuantiteAvantArrondi,12:F6} {leg.Quantite,22:F6}");

Console.WriteLine("Desk exposure                       Delta          Gamma     Vega per point");
PrintRisk("Before hedging", quote.GrecquesOption);
foreach (var leg in quote.Lignes)
    PrintRisk(leg.Nom, leg.Quantite * leg.GrecquesUnitaires);
PrintRisk("Before rounding", quote.GrecquesGlobalesAvantArrondi);
PrintRisk("After rounding", quote.GrecquesGlobales);

static void PrintRisk(string label, Grecques risk) =>
    Console.WriteLine($"{label,-26} {risk.Delta,15:F7} {risk.Gamma,14:F9} {risk.Vega,18:F4}");
