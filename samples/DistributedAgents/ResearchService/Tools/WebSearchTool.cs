using StrandsAgents.Core;

// Namespace matches the project root namespace so the generated
// WebSearchTool_Search_Tool class is accessible in Program.cs without extra usings.
namespace ResearchService;

/// <summary>
/// Simulates a web search engine API.
/// In production this would call Bing, Tavily, or another search provider.
/// </summary>
public sealed class WebSearchTool
{
    private static readonly (string[] Keywords, string Result)[] _results =
    [
        (["climate", "global warming", "carbon", "emissions", "temperature"],
            "Recent studies show global average temperatures have risen 1.2°C above pre-industrial levels. " +
            "The IPCC Sixth Assessment Report warns that 1.5°C will likely be reached by the early 2030s " +
            "without drastic emissions reductions. Renewable energy deployment is accelerating — solar " +
            "capacity additions hit 350 GW globally in 2024, a record. Major economies are updating NDCs " +
            "ahead of COP30, with focus on methane reduction and carbon capture."),

        (["artificial intelligence", "ai", "machine learning", "llm", "language model"],
            "Large language models surpassed 1 trillion parameter scale in 2024, with reasoning capabilities " +
            "improving dramatically via chain-of-thought and reinforcement learning techniques. " +
            "Enterprise AI adoption is accelerating: 67% of Fortune 500 companies now run production AI " +
            "workloads (up from 35% in 2022). Key research frontiers include multimodal reasoning, " +
            "long-context retrieval, and agentic systems capable of multi-step task execution."),

        (["space", "nasa", "rocket", "mars", "moon", "satellite", "spacex"],
            "The Artemis programme returned humans to lunar orbit in 2024 and a crewed lunar surface landing " +
            "is targeted for 2026. SpaceX Starship reached orbit and successfully executed booster catch " +
            "with the mechazilla arms. Commercial space station initiatives from Axiom and Blue Origin are " +
            "progressing. Mars sample return mission faces budget scrutiny but remains on the 2030 roadmap."),

        (["quantum", "qubit", "computing", "superposition", "entanglement"],
            "Google and IBM both reported quantum systems exceeding 1,000 physical qubits in 2024. " +
            "Error correction remains the central challenge — logical qubit demonstrations have reduced " +
            "error rates by 10x versus physical qubits. Practical quantum advantage for chemistry and " +
            "optimisation workloads is expected in the 2027–2030 timeframe. Several startups are pursuing " +
            "photonic and neutral-atom approaches as alternatives to superconducting architectures."),

        (["renewable", "solar", "wind", "energy", "battery", "electric", "ev"],
            "Global EV sales topped 17 million units in 2024, representing 20% of new car sales. " +
            "Battery costs fell below $90/kWh, approaching the $80 threshold widely considered parity with " +
            "combustion engines. Offshore wind capacity additions slowed due to supply chain and " +
            "interest-rate pressures but long-term project pipelines remain healthy. " +
            "Grid-scale battery storage deployments doubled for the second consecutive year."),
    ];

    /// <summary>
    /// Searches for information on the given topic.
    /// The source generator emits <c>WebSearchTool_Search_Tool</c> at compile time.
    /// </summary>
    [Tool("Search the web for up-to-date information on any topic. Returns a concise research summary.")]
    public string Search(string query)
    {
        var lower = query.ToLowerInvariant();
        foreach (var (keywords, result) in _results)
        {
            if (keywords.Any(k => lower.Contains(k)))
                return result;
        }
        return $"No specific results found for '{query}'. " +
               "Try more specific keywords or rephrase your query.";
    }
}
