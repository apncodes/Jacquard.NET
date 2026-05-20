using BlazorResearch;
using BlazorResearch.Components;
using Jacquard.Core;
using Jacquard.Models.Bedrock;

// BlazorResearch — a Blazor Server research portal backed by a Jacquard multi-agent swarm.
//
// Architecture:
//   Browser → Blazor Server (SignalR) → Research.razor component
//     ↓
//   3 analyst agents (ParallelOrchestrator) — each uses ResearchTool_Search_Tool
//     ↓ (results arrive as each agent completes — StateHasChanged updates UI live)
//   Synthesis agent — streams conclusion via IAsyncEnumerable + InvokeAsync(StateHasChanged)
//     ↓
//   GetStructuredOutputAsync<ResearchReport> — typed extraction displayed as summary card
//
// SDK features shown:
//   • Blazor Server + IAsyncEnumerable  — real-time UI updates without JavaScript
//   • ParallelOrchestrator              — 3 agents with Task.WhenAll, cards update as each finishes
//   • StreamAsync + StateHasChanged     — synthesis streams token-by-token into the DOM
//   • GetStructuredOutputAsync<T>       — typed report extraction
//   • [Tool] source generator           — ResearchTool_Search_Tool
//
// Prerequisites: AWS credentials configured (env vars, ~/.aws/credentials, or IAM role).
//
// Usage:
//   dotnet run
//   Then open http://localhost:5050 in your browser.

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5050");

// ── Jacquard services ───────────────────────────────────────────────────────────

builder.Services.AddSingleton<IModel>(_ => new BedrockModel(
    region:  builder.Configuration["Bedrock:Region"]  ?? "us-east-1",
    modelId: builder.Configuration["Bedrock:ModelId"] ?? "us.anthropic.claude-haiku-4-5-20251001-v1:0"));

// The source-generated wrapper is registered as a factory lambda because its
// constructor requires a ResearchTool instance.
builder.Services.AddSingleton<ITool>(_ =>
    new ResearchTool_Search_Tool(new ResearchTool()));

// ── Blazor ─────────────────────────────────────────────────────────────────────

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

Console.WriteLine("BlazorResearch portal running at http://localhost:5050");
app.Run();
