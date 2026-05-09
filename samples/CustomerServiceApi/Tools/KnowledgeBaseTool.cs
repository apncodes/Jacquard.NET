using StrandsAgents.Core;

// Namespace matches the project root so the generated KnowledgeBaseTool_Search_Tool
// is accessible in Program.cs and ChatSessionStore without extra usings.
namespace CustomerServiceApi;

/// <summary>
/// Simulates a knowledge base article search.
/// In production this would query a vector store or search index.
/// </summary>
public sealed class KnowledgeBaseTool
{
    private static readonly (string[] Keywords, string Article)[] _articles =
    [
        (["return", "refund", "exchange", "send back"],
            "Return & Refund Policy: Items can be returned within 30 days of delivery for a full refund. " +
            "Log in to your account, go to Orders → Return Item, and follow the prompts. " +
            "Refunds are credited to the original payment method within 5–7 business days. " +
            "Digital products and gift cards are non-refundable."),

        (["ship", "shipping", "delivery", "deliver", "track", "tracking", "arrive"],
            "Shipping FAQ: Standard shipping (free over $50) takes 3–5 business days. " +
            "Express shipping (1–2 days) is available at checkout for $12.99. " +
            "Once shipped you'll receive a tracking email — you can also check status via the app. " +
            "We ship Monday–Friday; orders placed after 2 PM dispatch the next business day."),

        (["password", "login", "sign in", "access", "locked", "forgot"],
            "Account Access: Click 'Forgot password' on the login screen to receive a reset link. " +
            "The link is valid for 30 minutes and is sent to your registered email. " +
            "If you don't see it, check spam/junk. After 5 failed login attempts your account " +
            "is locked for 15 minutes as a security measure."),

        (["cancel", "cancellation", "cancel my order"],
            "Order Cancellation: Orders can be cancelled within 60 minutes of placement. " +
            "Go to Orders → Select order → Cancel Order. " +
            "After 60 minutes the order has entered fulfilment and cannot be cancelled — " +
            "you will need to wait for delivery and then initiate a return."),

        (["warranty", "broken", "defect", "defective", "damaged"],
            "Warranty: All hardware products carry a 12-month limited warranty covering manufacturing defects. " +
            "Physical damage, water damage, and misuse are not covered. " +
            "To make a warranty claim, go to Account → My Products → Report Issue. " +
            "We'll send a prepaid return label and ship a replacement within 5 business days."),
    ];

    /// <summary>
    /// Searches knowledge base articles for answers to customer questions.
    /// The source generator emits <c>KnowledgeBaseTool_Search_Tool</c> at compile time.
    /// </summary>
    [Tool("Search the customer service knowledge base for policies, procedures, and FAQs. " +
          "Use this before escalating to a human agent.")]
    public string Search(string query)
    {
        var lower = query.ToLowerInvariant();
        foreach (var (keywords, article) in _articles)
        {
            if (keywords.Any(k => lower.Contains(k)))
                return article;
        }
        return "No specific article found for this query. " +
               "Please ask the customer for more details or escalate to a human agent.";
    }
}
