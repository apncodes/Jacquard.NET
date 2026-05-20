using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Jacquard.Core;

/// <summary>
/// Central telemetry definitions for the Jacquard SDK.
/// Uses built-in System.Diagnostics — no third-party OTel SDK required.
/// Consumers wire up listeners via ActivitySource.AddActivityListener / MeterListener.
/// </summary>
internal static class JacquardTelemetry
{
    internal const string SourceName = "Jacquard.Agent";

    /// <summary>ActivitySource for distributed tracing. Zero overhead when no listener is attached.</summary>
    internal static readonly ActivitySource ActivitySource = new(SourceName, "0.1.0");

    private static readonly Meter _meter = new(SourceName, "0.1.0");

    /// <summary>Number of input tokens consumed across all model calls.</summary>
    internal static readonly Counter<long> TokensInput =
        _meter.CreateCounter<long>("jacquard.tokens.input", "tokens", "Input tokens consumed");

    /// <summary>Number of output tokens produced across all model calls.</summary>
    internal static readonly Counter<long> TokensOutput =
        _meter.CreateCounter<long>("jacquard.tokens.output", "tokens", "Output tokens produced");

    /// <summary>Number of tool calls executed.</summary>
    internal static readonly Counter<long> ToolCalls =
        _meter.CreateCounter<long>("jacquard.tool.calls", "calls", "Number of tool calls executed");

    /// <summary>Agent invocation latency in milliseconds.</summary>
    internal static readonly Histogram<double> AgentLatency =
        _meter.CreateHistogram<double>("jacquard.agent.latency", "ms", "Agent invocation latency");
}
