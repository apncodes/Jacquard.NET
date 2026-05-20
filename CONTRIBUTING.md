# Contributing to Jacquard.NET

Thank you for helping bring Strands to the .NET community.

## Getting started

1. Fork and clone the repo
2. Install .NET 10 SDK — [dot.net](https://dot.net)
3. Run `dotnet build Jacquard.sln` — everything should build clean, zero warnings
4. Run `dotnet test Jacquard.sln` — all tests should pass

## Making changes

- C# 13 patterns throughout — `record`, primary constructors, pattern matching
- `CancellationToken ct = default` on every async method — no exceptions
- `ConfigureAwait(false)` on every `await` in library code
- `IAsyncEnumerable<T>` for all streaming return types — no sync-over-async
- xUnit tests for every new class — mock `IModel` with Moq, never call live endpoints
- XML doc comments on all public types and members
- Build must remain warning-free — `TreatWarningsAsErrors = true`

## What we need most

- **Model providers** — Ollama, Azure OpenAI, Cohere, Mistral
- **Built-in tools** — database query, code execution, vector search, email/calendar
- **Real-world samples** — RAG pipelines, customer service bots, coding assistants
- **Documentation** — guides, API reference, tutorials
- **Bug reports** — especially around edge cases in the event loop and session management

## Pull request process

1. Open an issue first for significant changes — alignment before code
2. Reference the [Jacquard Agents documentation](https://strandsagents.com) for shared concepts — keep semantics consistent
3. Include tests covering the new behaviour
4. Update the relevant README if adding features
5. Keep PRs focused — one feature or fix per PR

## Relationship to AWS Strands

Jacquard.NET implements the same model-driven architecture as [AWS Jacquard Agents](https://strandsagents.com). When working on shared concepts (event loop, tool system, hooks, orchestration patterns), refer to the official Strands documentation to keep semantics consistent. Where C# offers a better native pattern — `IAsyncEnumerable` instead of async generators, `Register<TEvent>` instead of string event names — prefer the idiomatic .NET approach and document why in the PR.

## Code of conduct

Be kind. This is a community project. Constructive disagreement is welcome; personal attacks are not.
