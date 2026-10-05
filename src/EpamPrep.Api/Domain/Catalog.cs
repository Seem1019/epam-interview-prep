namespace EpamPrep.Api.Domain;

public sealed record Subtopic(string Key, string Title);

/// <param name="Previous">Level from the previous EPAM interview (None = not evaluated).</param>
/// <param name="Target">Level needed for an L3 (senior+/near-lead) offer.</param>
/// <param name="Weak">Priority group: React, JavaScript and GenAI come first in the study plan.</param>
/// <param name="InterviewWeight">Relative share of time in a full mock interview.</param>
public sealed record Competency(
    string Key,
    string Title,
    Level Previous,
    Level Target,
    bool Weak,
    double InterviewWeight,
    IReadOnlyList<Subtopic> Subtopics);

/// <summary>Competencies and subtopics from the EPAM L3 guide, plus a technical-leadership block.</summary>
public static class Catalog
{
    public static readonly IReadOnlyList<Competency> All =
    [
        new("csharp", "C#", Level.Intermediate, Level.Advanced, false, 1.2,
        [
            new("oop-access", "Access modifiers & OOP in C#"),
            new("generics-variance", "Generics, covariance & contravariance"),
            new("async-await", "async/await internals (state machine, SynchronizationContext, ValueTask)"),
            new("threading-tpl", "Multithreading, TPL & locking primitives"),
            new("reflection", "Reflection, attributes & source generators"),
            new("advanced-features", "Streams, IAsyncEnumerable, Span<T>, records & pattern matching"),
        ]),
        new("dotnet", ".NET platform", Level.Intermediate, Level.Advanced, false, 1.0,
        [
            new("core-vs-framework", ".NET (Core) vs .NET Framework & .NET Standard"),
            new("di-logging", "Dependency Injection lifetimes & logging"),
            new("cli-config", ".NET CLI, configuration providers & Options pattern"),
            new("runtime-gc", "CLR, JIT, garbage collector & memory"),
        ]),
        new("aspnet", "ASP.NET Core", Level.Intermediate, Level.Advanced, false, 1.2,
        [
            new("startup-middleware", "App startup & middleware pipeline"),
            new("routing-filters", "Routing, filters & Minimal APIs vs controllers"),
            new("rest", "REST principles, versioning & idempotency"),
            new("host-env", "Web host, Kestrel & environments"),
            new("auth-jwt", "Authentication/Authorization, JWT & ASP.NET Core Identity"),
            new("binding-validation", "Model binding & validation"),
            new("errors-health", "Error handling, ProblemDetails, static files & health checks"),
        ]),
        new("ef", "Entity Framework", Level.Intermediate, Level.Advanced, false, 1.0,
        [
            new("orm-relationships", "ORM concepts, relationships & change tracking"),
            new("migrations", "Migrations in teams and production"),
            new("performance", "EF Core performance tuning (N+1, tracking, split queries, compiled queries)"),
            new("dapper", "Micro ORMs: Dapper vs EF Core"),
        ]),
        new("sql", "Databases (SQL & NoSQL)", Level.Intermediate, Level.Advanced, false, 1.0,
        [
            new("advanced-queries", "Advanced queries: joins, CTEs, window functions"),
            new("transactions", "Transactions, isolation levels & locking"),
            new("stored-procs", "Stored procedures, functions & views"),
            new("performance", "Indexes, execution plans & performance troubleshooting"),
            new("nosql-cap", "NoSQL types & CAP theorem"),
            new("mongodb", "MongoDB: indexes, aggregation, transactions, Map-Reduce"),
        ]),
        new("testing", ".NET Unit & Integration Testing", Level.Intermediate, Level.Advanced, false, 0.9,
        [
            new("pyramid", "Testing pyramid: unit, integration, component, API"),
            new("tdd-bdd", "TDD & BDD"),
            new("aaa-first-mocking", "AAA, FIRST, mocks vs stubs vs fakes"),
            new("integration-aspnet", "Integration tests: WebApplicationFactory & Testcontainers"),
        ]),
        new("patterns", "Design Patterns", Level.Intermediate, Level.Advanced, false, 0.9,
        [
            new("decorator", "Decorator in practice"),
            new("singleton", "Singleton (and why DI lifetimes replace it)"),
            new("factory", "Factory Method & Abstract Factory"),
            new("other-patterns", "Strategy, Observer, Repository/Unit of Work, Mediator/CQRS"),
        ]),
        new("design", "Software Design & Architecture", Level.Intermediate, Level.Advanced, false, 1.1,
        [
            new("oop-solid", "OOP & SOLID"),
            new("clean-code-smells", "Clean Code & code smells"),
            new("cohesion-coupling", "High cohesion / low coupling"),
            new("documentation", "Technical documentation & diagrams (C4, UML, ADRs)"),
            new("soa-microservices", "SOA vs microservices, messaging, consistency"),
            new("solution-architecture", "Solution architecture & non-functional requirements"),
        ]),
        new("cloud", "Cloud & DevOps (Azure)", Level.Intermediate, Level.Advanced, false, 0.8,
        [
            new("azure-dotnet", "Azure for .NET apps: App Service, Functions, AKS, Service Bus, Key Vault"),
            new("docker", "Docker for .NET"),
            new("iac", "Infrastructure as Code: Terraform & Bicep"),
            new("cicd", "CI/CD pipeline design (Azure DevOps, GitHub Actions)"),
            new("aws-gcp", "AWS / GCP equivalents"),
        ]),
        new("javascript", "JavaScript ES6+", Level.None, Level.Intermediate, true, 0.8,
        [
            new("es6-modules", "ES6+ syntax & modules"),
            new("async-event-loop", "Promises, async/await & the event loop"),
            new("dom", "DOM, events & browser APIs"),
            new("advanced", "Closures, this, prototypes, hoisting & scope"),
        ]),
        new("react", "React + TypeScript", Level.Novice, Level.Intermediate, true, 0.9,
        [
            new("components", "Component architecture with TypeScript"),
            new("hooks-rendering", "Hooks, rendering & reconciliation"),
            new("forms", "Forms & validation"),
            new("routing", "Routing (React Router)"),
            new("state", "State management: Context, Redux Toolkit, Zustand, React Query"),
            new("build-tools", "Build tools: Vite, webpack, rollup, gulp"),
        ]),
        new("genai", "AI Engineering / GenAI-assisted development", Level.Novice, Level.Intermediate, true, 0.8,
        [
            new("prompt-engineering", "Prompt engineering"),
            new("llms", "LLM landscape: OpenAI, Claude, Copilot, Gemini, DeepSeek"),
            new("ai-code-review", "AI code review tools"),
            new("agentic", "Agentic workflows"),
            new("genai-sdlc", "GenAI in the SDLC & CI/CD integration"),
        ]),
        new("process", "SDLC & Agile", Level.None, Level.Advanced, false, 0.5,
        [
            new("sdlc", "SDLC stages & roles"),
            new("requirements", "Functional & non-functional requirements"),
            new("dod-dor", "Definition of Done / Definition of Ready"),
            new("agile", "Agile, Scrum, Kanban vs Waterfall"),
        ]),
        new("leadership", "Technical leadership", Level.None, Level.Advanced, false, 0.9,
        [
            new("code-reviews", "Code reviews"),
            new("mentoring", "Mentoring"),
            new("estimation", "Estimation"),
            new("architecture-decisions", "Architecture decisions & defending trade-offs"),
            new("incidents", "Production incident management"),
            new("tech-debt", "Technical debt"),
            new("star", "Behavioral questions with STAR"),
        ]),
    ];

    private static readonly Dictionary<string, Competency> ByKey = All.ToDictionary(c => c.Key);

    public static Competency? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var c) ? c : null;

    public static Competency Get(string key) =>
        Find(key) ?? throw new KeyNotFoundException($"Competencia desconocida: {key}");

    /// <summary>Order of blocks in a full mock: basics first, as EPAM does.</summary>
    public static IReadOnlyList<string> FullInterviewOrder =>
        ["csharp", "dotnet", "aspnet", "ef", "sql", "testing", "patterns", "design", "cloud",
         "javascript", "react", "genai", "process", "leadership"];
}
