using Jacquard.Core;

namespace SwarmResearch;

/// <summary>
/// Simulated research and writing tools used by the swarm agents.
/// In production these would call real APIs (search engines, databases, etc.).
/// The [Tool] attribute causes the source generator to emit compile-time ITool wrappers.
/// </summary>
public sealed partial class ResearchTools
{
    private static readonly IReadOnlyDictionary<string, TopicData> _topics =
        new Dictionary<string, TopicData>(StringComparer.OrdinalIgnoreCase)
        {
            ["quantum computing"] = new(
                Facts: """
                    - Quantum computers use qubits that exploit superposition and entanglement
                    - IBM's Condor processor reached 1,121 qubits in 2023
                    - Google claimed quantum supremacy in 2019 with Sycamore (53 qubits)
                    - Current NISQ-era machines are error-prone; fault-tolerant QC is 5-15 years away
                    - Key applications: cryptography, drug discovery, optimisation, materials science
                    """,
                Sources: "IBM Research, Google AI Blog, Nature (2023), MIT Technology Review",
                Critique: """
                    - 'Quantum supremacy' claims are contested; classical algorithms have since matched some benchmarks
                    - Qubit counts alone are misleading — coherence time and error rates matter more
                    - Commercial timelines are routinely overstated; temper expectations accordingly
                    - The article should distinguish NISQ-era capabilities from long-term fault-tolerant potential
                    """),

            ["large language models"] = new(
                Facts: """
                    - LLMs are transformer-based neural networks trained on vast text corpora
                    - GPT-4 (OpenAI), Claude 3 (Anthropic), Gemini (Google) are leading frontier models
                    - Training costs for frontier models exceed $100M; inference costs are falling rapidly
                    - Key capabilities: reasoning, code generation, summarisation, translation
                    - Key limitations: hallucination, context window constraints, knowledge cutoffs
                    """,
                Sources: "OpenAI, Anthropic, Google DeepMind, Hugging Face, arXiv (2024)",
                Critique: """
                    - Benchmark scores are frequently gamed; real-world performance varies significantly
                    - Safety and alignment remain open research problems — avoid overstating progress
                    - The article should address environmental costs of training and inference
                    - Distinguish between capability (what models can do) and reliability (how often they do it correctly)
                    """),

            ["renewable energy"] = new(
                Facts: """
                    - Solar and wind are now the cheapest sources of new electricity generation globally
                    - Global renewable capacity additions hit a record 295 GW in 2022 (IEA)
                    - Battery storage costs have fallen 90% over the past decade
                    - Intermittency remains the primary grid-integration challenge
                    - Green hydrogen is emerging as a long-duration storage and industrial fuel solution
                    """,
                Sources: "IEA World Energy Outlook 2023, IRENA, BloombergNEF, NREL",
                Critique: """
                    - Grid-scale storage and transmission infrastructure investment is lagging capacity additions
                    - Lifecycle emissions of solar panels and batteries are non-trivial — include full LCA
                    - Policy dependency: many projections assume continued subsidies that may not materialise
                    - The article should address land use and supply chain (rare earth minerals) concerns
                    """),
        };

    private const string NotFound =
        "No data found for this topic. " +
        "Covered topics: 'quantum computing', 'large language models', 'renewable energy'.";

    /// <summary>
    /// Retrieves verified facts and statistics about a research topic.
    /// </summary>
    [Tool("Search for verified facts, statistics, and key developments about a research topic.")]
    public string SearchFacts(string topic) =>
        _topics.TryGetValue(topic, out var d) ? d.Facts : NotFound;

    /// <summary>
    /// Returns authoritative sources for a research topic.
    /// </summary>
    [Tool("Get a list of authoritative sources and references for a research topic.")]
    public string GetSources(string topic) =>
        _topics.TryGetValue(topic, out var d) ? d.Sources : NotFound;

    /// <summary>
    /// Returns editorial critique and improvement suggestions for a draft article.
    /// </summary>
    [Tool("Review a draft article and return editorial critique: factual gaps, balance issues, and improvement suggestions.")]
    public string ReviewDraft(string topic) =>
        _topics.TryGetValue(topic, out var d) ? d.Critique : NotFound;
}

/// <summary>Simulated research data for a single topic.</summary>
internal record TopicData(string Facts, string Sources, string Critique);
