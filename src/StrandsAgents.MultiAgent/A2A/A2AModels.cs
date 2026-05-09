namespace StrandsAgents.MultiAgent;

/// <summary>Wire format for an A2A request.</summary>
public record A2ARequest(string Prompt);

/// <summary>Wire format for an A2A response.</summary>
public record A2AResponse(string Message, string StopReason, int InputTokens, int OutputTokens);
