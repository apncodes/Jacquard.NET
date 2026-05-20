---
sidebar_position: 7
---

# Model Providers

## What it is

Jacquard.NET ships four model providers out of the box, all in `Jacquard.Core`. Every provider implements `IModel` — swap one for another by changing a single line. No third-party SDKs required: each provider uses `HttpClient` + `System.Text.Json` directly.

## Amazon Bedrock

The primary provider for AWS deployments. Uses the Bedrock Converse API with support for cross-region inference profiles, Guardrails, and all Bedrock-hosted model families.

```bash
dotnet add package Jacquard.Models.Bedrock
```

```csharp
// Cross-region inference profile (recommended — no on-demand throughput required)
var model = new BedrockModel(
    region: "us-east-1",
    modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0");

// Specific region model
var model = new BedrockModel(
    region: "us-east-1",
    modelId: "anthropic.claude-3-haiku-20240307-v1:0");

// With Guardrails
var model = new BedrockModel(
    region: "us-east-1",
    modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0",
    guardrailId: "abc123",
    guardrailVersion: "1");
```

**DI registration:**
```csharp
builder.Services.AddBedrockModel(
    region: "us-east-1",
    modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0");
```

**Credentials:** Resolved automatically via the standard AWS credential chain — environment variables, `~/.aws/credentials`, IAM instance profile, or AWS SSO.

**Supported model families:** Claude (Anthropic), Nova (Amazon), Llama (Meta), Mistral, Titan, and all other Bedrock-hosted models.

:::tip Cross-region inference profiles
Use cross-region profile IDs (prefixed with `us.`, `eu.`, or `ap.`) rather than bare model IDs. Bare model IDs require on-demand throughput which is not available by default. Profile IDs automatically route to the nearest available region.
:::

---

## Anthropic Direct API

Call Claude models directly via the Anthropic Messages API. No AWS account required — just an Anthropic API key.

```csharp
// Direct API
var model = new AnthropicModel(
    apiKey: "sk-ant-...",
    modelId: "claude-sonnet-4-5");  // default: "claude-sonnet-4-5"

// With IHttpClientFactory (recommended in hosted apps)
var model = new AnthropicModel(
    apiKey: "sk-ant-...",
    modelId: "claude-haiku-4-5",
    httpClientFactory: httpClientFactory);
```

**DI registration:**
```csharp
builder.Services.AddAnthropicModel(
    apiKey: configuration["Anthropic:ApiKey"]!,
    modelId: "claude-sonnet-4-5");
```

**Available models:** All Claude models available via the Anthropic API — Claude 3.5 Haiku, Claude Sonnet 4.5, Claude Sonnet 4.6, Claude Opus 4, and newer releases.

**Implementation note:** Uses `HttpClient` + `System.Text.Json` directly. No `Anthropic.SDK` NuGet package required. AOT-safe.

---

## OpenAI-Compatible Endpoint

Works with OpenAI, Azure OpenAI, Ollama, LM Studio, and any endpoint that implements the OpenAI Chat Completions API.

```csharp
// OpenAI
var model = new OpenAICompatibleModel(
    baseUrl: "https://api.openai.com/v1",
    apiKey: "sk-...",
    modelId: "gpt-4o");

// Azure OpenAI
var model = new OpenAICompatibleModel(
    baseUrl: "https://YOUR_RESOURCE.openai.azure.com/openai/deployments/YOUR_DEPLOYMENT",
    apiKey: "YOUR_AZURE_KEY",
    modelId: "gpt-4o");

// Ollama (local, no auth)
var model = new OpenAICompatibleModel(
    baseUrl: "http://localhost:11434/v1",
    apiKey: "",  // empty string for unauthenticated endpoints
    modelId: "llama3.2");

// LM Studio
var model = new OpenAICompatibleModel(
    baseUrl: "http://localhost:1234/v1",
    apiKey: "lm-studio",
    modelId: "local-model");
```

**DI registration:**
```csharp
builder.Services.AddOpenAICompatibleModel(
    baseUrl: "https://api.openai.com/v1",
    apiKey: configuration["OpenAI:ApiKey"]!,
    modelId: "gpt-4o-mini");
```

**Implementation note:** Uses `HttpClient` + `System.Text.Json` directly. No `OpenAI` NuGet package required. AOT-safe.

---

## Google Gemini

Call Gemini models via the Gemini Developer REST API. Uses a Google AI Studio API key.

```csharp
var model = new GeminiModel(
    apiKey: "AIza...",
    modelId: "gemini-2.0-flash");  // default: "gemini-2.0-flash"

// With IHttpClientFactory
var model = new GeminiModel(
    apiKey: "AIza...",
    modelId: "gemini-2.5-flash",
    httpClientFactory: httpClientFactory);
```

**DI registration:**
```csharp
builder.Services.AddGeminiModel(
    apiKey: configuration["Gemini:ApiKey"]!,
    modelId: "gemini-2.0-flash");
```

**Available models:** Gemini 2.0 Flash, Gemini 2.5 Flash, Gemini 2.5 Pro, and other Gemini Developer API models.

**Implementation note:** Uses `HttpClient` + `System.Text.Json` directly. No Google AI SDK required. AOT-safe.

---

## Swapping models

Because all providers implement `IModel`, swapping is one line:

```csharp
// Development: Ollama (free, local)
IModel model = new OpenAICompatibleModel("http://localhost:11434/v1", "", "llama3.2");

// Staging: Bedrock Haiku (fast, cheap)
IModel model = new BedrockModel("us-east-1", "us.anthropic.claude-haiku-4-5-20251001-v1:0");

// Production: Bedrock Sonnet (best quality)
IModel model = new BedrockModel("us-east-1", "us.anthropic.claude-sonnet-4-6");

var agent = new Agent(model, systemPrompt: "...", toolProviders: [...]);
// Agent code is identical regardless of which model is used
```

## Model parameters

Override model parameters per-invocation:

```csharp
var result = await agent.InvokeAsync(
    "Write a creative story",
    parameters: new ModelParameters
    {
        Temperature = 0.9f,
        MaxTokens = 2000
    });
```

## Implementing a custom provider

Implement `IModel` to add any model not covered by the built-in providers:

```csharp
public sealed class MyCustomModel : IModel
{
    public async Task<ModelResponse> InvokeAsync(ModelRequest request, CancellationToken ct = default)
    {
        // Call your model API
        // Map the response to ModelResponse
    }

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Stream events from your model API
    }
}
```
