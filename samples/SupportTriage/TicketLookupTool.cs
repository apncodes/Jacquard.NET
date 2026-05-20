using Jacquard.Core;

namespace SupportTriage;

/// <summary>
/// Simulates a CRM lookup for support ticket and customer information.
/// In production this would call your ticketing system API.
/// </summary>
public sealed partial class TicketLookupTool
{
    private static readonly IReadOnlyDictionary<string, string> _crm =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TKT-001"] = "Customer: Alice Brown | Plan: Pro ($49.99/mo) | " +
                          "Issue: Charged $49.99 on Apr 1 AND Apr 3 | " +
                          "Payment method: Visa ending 4242 | Account status: Active",

            ["TKT-002"] = "Customer: Bob Smith | Plan: Standard | " +
                          "Device: iPhone 14 Pro (iOS 17.4) | App version: 3.1.2 | " +
                          "Issue: App crashes immediately on launch since v3.1.2 update (Apr 12) | " +
                          "Account status: Active",

            ["TKT-003"] = "Customer: Carol Davis | Plan: Enterprise | " +
                          "Current email: carol@oldcorp.com | " +
                          "Requested email: carol@newcorp.com | " +
                          "Identity verified: Yes (via support PIN) | Account status: Active",
        };

    /// <summary>
    /// Looks up a support ticket by ID and returns the CRM record.
    /// The source generator emits <c>TicketLookupTool_LookupTicket_Tool</c> at compile time.
    /// </summary>
    [Tool("Look up a support ticket by ID to retrieve customer and account details from the CRM.")]
    public string LookupTicket(string ticketId) =>
        _crm.TryGetValue(ticketId.Trim(), out var record)
            ? record
            : $"Ticket '{ticketId}' not found. Please verify the ticket ID.";
}
