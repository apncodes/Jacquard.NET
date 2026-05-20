using Jacquard.Core;

// Namespace matches the project root namespace so the generated
// ResearchTool_Search_Tool class is accessible in Research.razor without extra usings.
namespace BlazorResearch;

/// <summary>
/// Simulates a web search API used by the research portal agents.
/// In production this would call Bing, Tavily, or another search provider.
/// </summary>
public sealed class ResearchTool
{
    private static readonly (string[] Keywords, string Result)[] _index =
    [
        (["quantum", "qubit", "superposition", "entanglement", "quantum computing"],
            "Quantum computing uses quantum mechanical phenomena to process information. " +
            "As of 2025, IBM and Google both operate systems exceeding 1,000 physical qubits. " +
            "Error correction remains the central engineering challenge — logical qubit demonstrations " +
            "have achieved 10x error-rate reductions versus physical qubits. Practical quantum advantage " +
            "for chemistry simulation and optimisation is expected in the 2027–2030 window. " +
            "The global quantum computing market is projected to reach $450B by 2030. " +
            "Key players: IBM, Google, IonQ, Quantinuum, PsiQuantum, Microsoft."),

        (["artificial intelligence", "machine learning", "ai", "llm", "neural", "deep learning"],
            "Large language models surpassed 1 trillion parameters in 2024. Reasoning capabilities " +
            "have improved dramatically via chain-of-thought prompting and RL from human feedback. " +
            "Enterprise AI adoption: 67% of Fortune 500 run production AI workloads (up from 35% in 2022). " +
            "The AI software market is expected to exceed $1T by 2027. " +
            "Key research frontiers: multimodal reasoning, long-context retrieval, agentic systems, " +
            "on-device inference, and alignment/safety. GPU supply constraints remain a bottleneck. " +
            "Key players: OpenAI, Anthropic, Google DeepMind, Meta AI, Mistral, Cohere."),

        (["climate", "carbon", "emissions", "renewable", "net zero", "sustainability"],
            "Global temperatures have risen 1.2°C above pre-industrial levels. The IPCC warns 1.5°C " +
            "will likely be reached by the early 2030s without rapid emissions cuts. " +
            "Solar capacity additions hit 350 GW globally in 2024 — a new record. " +
            "Battery storage costs fell below $90/kWh, approaching parity with combustion engines. " +
            "The clean energy transition market is worth $1.8T annually. " +
            "Carbon capture deployment is accelerating, with 50+ large-scale CCUS plants planned by 2030. " +
            "Key players: NextEra, Ørsted, Enel, BYD, Vestas, First Solar."),

        (["space", "rocket", "satellite", "orbit", "moon", "mars", "aerospace"],
            "The commercial space sector reached $570B in revenue in 2024. SpaceX Starship achieved " +
            "full-stack orbital flight and booster catch with Mechazilla arms. " +
            "NASA Artemis targets a crewed lunar surface landing in 2026. " +
            "Low-Earth orbit satellite internet (Starlink, OneWeb, Project Kuiper) now serves " +
            "4M+ subscribers globally. Mars sample return remains on the 2030 roadmap. " +
            "New entrants (Rocket Lab, ABL Space, Relativity) are growing the launch market. " +
            "Key players: SpaceX, NASA, ESA, Blue Origin, Rocket Lab, Airbus Defence."),

        (["biotech", "genomics", "gene editing", "crispr", "drug discovery", "mRNA", "pharma"],
            "mRNA platform technology proven at scale by COVID vaccines is now being applied to cancer, " +
            "HIV, and rare diseases. CRISPR-based therapies received FDA approval in 2023 for sickle cell " +
            "disease — the first approved in-vivo gene-editing treatment. AI-assisted drug discovery has " +
            "cut early-stage timelines by 40-60%. The global biotech market is $1.4T with 15% CAGR. " +
            "Personalised medicine and cell therapies are the fastest-growing segments. " +
            "Key players: Moderna, BioNTech, Illumina, Genentech, CRISPR Therapeutics, Recursion."),
    ];

    /// <summary>
    /// Searches for research information on a topic.
    /// The source generator emits <c>ResearchTool_Search_Tool</c> at compile time.
    /// </summary>
    [Tool("Search for up-to-date research information on a topic. Returns a detailed research summary " +
          "covering current state, market data, key players, and outlook.")]
    public string Search(string query)
    {
        var lower = query.ToLowerInvariant();
        foreach (var (keywords, result) in _index)
        {
            if (keywords.Any(k => lower.Contains(k)))
                return result;
        }
        return $"No research data found for '{query}'. " +
               "Covered topics: quantum computing, artificial intelligence, climate/renewables, " +
               "space/aerospace, biotech/genomics. Please rephrase or choose a related topic.";
    }
}
