---
sidebar_position: 100
---

# Contributing

Jacquard.NET is open source under Apache 2.0 and welcomes contributions. This page covers how to get involved — for the full details, see [CONTRIBUTING.md](https://github.com/apncodes/Jacquard.NET/blob/main/CONTRIBUTING.md) in the repo root.

## Where to start

**Good first issues:** Look for issues labeled `good-first-issue` on GitHub. These are scoped, well-defined tasks that don't require deep framework knowledge.

**Areas of need:**

- **Model providers** — Ollama native provider, Azure OpenAI improvements, Cohere, Mistral
- **Built-in tools** — more tools in `StrandsAgents.Tools` (web search, database query, vector store)
- **Samples** — real-world examples showing production patterns
- **Documentation** — tutorials, how-to guides, concept explanations
- **Testing** — unit tests, integration tests, edge case coverage

## How to submit a PR

1. **Fork** the repository on GitHub
2. **Branch** from `main` — use a descriptive name like `feature/ollama-model` or `fix/streaming-cancel`
3. **Implement** your change, following the existing code style (see `Directory.Build.props` for shared settings)
4. **Test** — run `dotnet build` and `dotnet test` to make sure everything passes. `TreatWarningsAsErrors` is enabled, so fix any warnings.
5. **Submit** a pull request with a clear description of what changed and why

### Code style notes

- All public types are `sealed` unless inheritance is required
- Data types are `record`; mutable state uses classes
- `ConfigureAwait(false)` on every `await` in library code
- `System.Text.Json` only — no Newtonsoft
- No runtime reflection — tool schemas are generated at compile time

## What's needed most

In rough priority order:

1. **Additional model providers** — especially Ollama (local development), Cohere, and Mistral. The `IModel` interface is straightforward to implement.
2. **More built-in tools** — the `StrandsAgents.Tools` package currently has calculator, file read/write, and HTTP request. Web search, SQL query, and vector store tools would be valuable.
3. **Real-world samples** — production-style examples that go beyond "hello world". Think: RAG pipelines, customer service bots, code generation agents, data analysis workflows.
4. **Documentation improvements** — better explanations, more tutorials, API reference coverage.

## Larger proposals

For significant changes — new packages, architectural shifts, breaking API changes — open a [GitHub Discussion](https://github.com/apncodes/Jacquard.NET/discussions) first. This lets the community weigh in before you invest time in implementation.

Examples of "open a discussion first":
- Adding a new top-level NuGet package
- Changing the `IModel` or `ITool` interfaces
- New orchestration patterns in `StrandsAgents.MultiAgent`
- Changes to the source generator's output

## Running the project locally

```bash
# Build everything
dotnet build Jacquard.sln --configuration Release

# Run unit tests
dotnet test tests/StrandsAgents.Core.Tests --configuration Release
dotnet test tests/StrandsAgents.Runtime.Tests --configuration Release

# Run a sample
dotnet run --project samples/CliAgent
```

Integration tests require AWS credentials and `STRANDS_INTEGRATION_TESTS=true`:

```bash
STRANDS_INTEGRATION_TESTS=true dotnet test tests/StrandsAgents.Integration.Tests
```

## License

All contributions are licensed under Apache 2.0, the same license as the project.
