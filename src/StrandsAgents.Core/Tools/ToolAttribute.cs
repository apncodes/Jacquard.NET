namespace StrandsAgents.Core;

/// <summary>
/// Marks a method as an agent tool. The Strands source generator will emit
/// a compile-time ITool wrapper with a JSON schema derived from parameter types.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ToolAttribute : Attribute
{
    /// <summary>Human-readable description sent to the model.</summary>
    public string Description { get; }

    /// <summary>Optional override for the tool name. Defaults to the method name.</summary>
    public string? Name { get; init; }

    public ToolAttribute(string description)
    {
        Description = description;
    }
}
