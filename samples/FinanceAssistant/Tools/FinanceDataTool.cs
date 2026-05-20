using Jacquard.Core;

// Namespace matches the project root so source-generated tool wrappers
// (FinanceDataTool_GetQuote_Tool, etc.) are accessible in Program.cs without extra usings.
namespace FinanceAssistant;

/// <summary>
/// Simulates a market data and financial research API.
/// In production this would call Bloomberg, Refinitiv, or a broker data feed.
/// </summary>
public sealed partial class FinanceDataTool
{
    private static readonly IReadOnlyDictionary<string, TickerData> _data =
        new Dictionary<string, TickerData>(StringComparer.OrdinalIgnoreCase)
        {
            ["NVDA"] = new(
                Quote:      "Price: $875.40 | Change: +2.3% | Volume: 45.2M | Market Cap: $2.16T",
                Financials: "P/E: 65x | Revenue (TTM): $60.9B | EPS: $11.93 | Net Margin: 55% | YoY Revenue Growth: +122%",
                Headlines:  "1. NVIDIA reports record data center revenue of $22.6B, up 427% YoY\n" +
                            "2. US export restrictions on H100/H200 chips to China create near-term headwind\n" +
                            "3. Blackwell GPU ramp on track; hyperscalers increasing AI capex guidance",
                Risk:       "Beta: 1.85 | 52-Week Range: $460–$974 | Annualized Volatility: 42% | Short Interest: 0.8%"),

            ["AAPL"] = new(
                Quote:      "Price: $195.80 | Change: -0.4% | Volume: 52.1M | Market Cap: $3.01T",
                Financials: "P/E: 29x | Revenue (TTM): $391.0B | EPS: $6.57 | Net Margin: 26% | YoY Revenue Growth: +2%",
                Headlines:  "1. Vision Pro international rollout begins; early adoption tracking below initial forecasts\n" +
                            "2. iPhone 16 cycle shows modest upgrade momentum; India manufacturing ramp ahead of schedule\n" +
                            "3. Services revenue hits all-time high at $23.1B — fastest-growing segment for fourth consecutive quarter",
                Risk:       "Beta: 1.25 | 52-Week Range: $164–$237 | Annualized Volatility: 22% | Short Interest: 0.6%"),

            ["MSFT"] = new(
                Quote:      "Price: $415.20 | Change: +0.8% | Volume: 18.4M | Market Cap: $3.09T",
                Financials: "P/E: 35x | Revenue (TTM): $236.0B | EPS: $11.45 | Net Margin: 36% | YoY Revenue Growth: +16%",
                Headlines:  "1. Azure cloud revenue grows 29% — AI services cited as primary demand driver\n" +
                            "2. Microsoft 365 Copilot reaches 1M paid enterprise seats ahead of internal roadmap\n" +
                            "3. Activision integration tracking to plan; gaming revenue up 51% post-acquisition",
                Risk:       "Beta: 0.90 | 52-Week Range: $362–$468 | Annualized Volatility: 20% | Short Interest: 0.4%"),

            ["AMZN"] = new(
                Quote:      "Price: $185.60 | Change: +1.1% | Volume: 38.7M | Market Cap: $1.93T",
                Financials: "P/E: 44x | Revenue (TTM): $620.1B | EPS: $4.06 | Net Margin: 8% | YoY Revenue Growth: +13%",
                Headlines:  "1. AWS revenue re-accelerates to 17% growth; generative AI workloads a key driver\n" +
                            "2. Advertising segment surpasses $50B annualised run rate, second consecutive beat\n" +
                            "3. Project Kuiper satellite internet starts beta testing with select enterprise customers",
                Risk:       "Beta: 1.40 | 52-Week Range: $152–$201 | Annualized Volatility: 28% | Short Interest: 0.5%"),
        };

    private const string NotFound =
        "Data unavailable for this ticker. " +
        "Covered tickers: NVDA, AAPL, MSFT, AMZN. Please verify the symbol.";

    /// <summary>
    /// Returns the current stock quote.
    /// The source generator emits <c>FinanceDataTool_GetQuote_Tool</c> at compile time.
    /// </summary>
    [Tool("Get the current stock quote including price, daily change percentage, volume, and market cap.")]
    public string GetQuote(string ticker) =>
        _data.TryGetValue(ticker, out var d) ? d.Quote : NotFound;

    /// <summary>
    /// Returns key financial metrics.
    /// The source generator emits <c>FinanceDataTool_GetFinancials_Tool</c> at compile time.
    /// </summary>
    [Tool("Get key financial metrics: trailing-twelve-month P/E ratio, revenue, EPS, net margin, and YoY revenue growth.")]
    public string GetFinancials(string ticker) =>
        _data.TryGetValue(ticker, out var d) ? d.Financials : NotFound;

    /// <summary>
    /// Returns recent news headlines.
    /// The source generator emits <c>FinanceDataTool_GetHeadlines_Tool</c> at compile time.
    /// </summary>
    [Tool("Get the three most recent news headlines and key developments relevant to this stock.")]
    public string GetHeadlines(string ticker) =>
        _data.TryGetValue(ticker, out var d) ? d.Headlines : NotFound;

    /// <summary>
    /// Returns risk metrics.
    /// The source generator emits <c>FinanceDataTool_GetRiskMetrics_Tool</c> at compile time.
    /// </summary>
    [Tool("Get risk metrics: beta, 52-week price range, annualized volatility, and short interest percentage.")]
    public string GetRiskMetrics(string ticker) =>
        _data.TryGetValue(ticker, out var d) ? d.Risk : NotFound;
}

/// <summary>Simulated market data for a single ticker.</summary>
internal record TickerData(string Quote, string Financials, string Headlines, string Risk);
