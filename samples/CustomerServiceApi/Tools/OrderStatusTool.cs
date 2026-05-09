using StrandsAgents.Core;

// Namespace must match the project root namespace so the source-generated
// OrderStatusTool_GetStatus_Tool class is accessible in Program.cs without extra usings.
namespace CustomerServiceApi;

/// <summary>
/// Simulates an order management system lookup.
/// In production this would call your OMS or ERP API.
/// </summary>
public sealed class OrderStatusTool
{
    private static readonly IReadOnlyDictionary<string, string> _orders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ORD-4521"] = "Status: Shipped | Carrier: FedEx | Tracking: 274899172985 | ETA: Apr 17",
            ["ORD-4890"] = "Status: Processing | Items: 2× Widget Pro, 1× Cable Kit | Est. dispatch: Apr 16",
            ["ORD-3317"] = "Status: Delivered | Delivered: Apr 12 | Signed by: J. Smith",
            ["ORD-5001"] = "Status: Cancelled | Reason: Customer request | Refund: $45.99 — 3–5 business days",
        };

    /// <summary>
    /// Returns the current status of an order.
    /// The source generator emits <c>OrderStatusTool_GetStatus_Tool</c> at compile time.
    /// </summary>
    [Tool("Look up the current status and shipping details for a customer order by order ID (format: ORD-XXXX).")]
    public string GetStatus(string orderId) =>
        _orders.TryGetValue(orderId.Trim(), out var status)
            ? status
            : $"No order found for ID '{orderId}'. Please check the order confirmation email.";
}
