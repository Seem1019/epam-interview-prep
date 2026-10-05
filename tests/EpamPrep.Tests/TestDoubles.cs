using System.Runtime.CompilerServices;
using System.Text.Json;
using EpamPrep.Api.Ai;
using EpamPrep.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EpamPrep.Tests;

/// <summary>Scripted LLM: returns queued JSON / text and records every request. No network, no cost.</summary>
public sealed class FakeClaudeClient : IClaudeClient
{
    public Queue<string> JsonResponses { get; } = new();
    public Queue<string> TextResponses { get; } = new();
    public List<(AiRequest Request, JsonElement? Schema)> Calls { get; } = [];

    public bool IsConfigured => true;

    public async IAsyncEnumerable<string> StreamTextAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Calls.Add((request, null));
        var text = TextResponses.Count > 0 ? TextResponses.Dequeue() : "Next question?";
        foreach (var chunk in text.Chunk(7))
        {
            await Task.Yield();
            yield return new string(chunk);
        }
    }

    public Task<JsonDocument> GetJsonAsync(AiRequest request, JsonElement schema, CancellationToken ct = default)
    {
        Calls.Add((request, schema));
        if (JsonResponses.Count == 0) throw new InvalidOperationException("No scripted JSON response left.");
        return Task.FromResult(JsonDocument.Parse(JsonResponses.Dequeue()));
    }
}

public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>In-memory SQLite with the real migrations applied (closer to production than the InMemory provider).</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDb()
    {
        _connection.Open();
        Context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        Context.Database.Migrate();
    }

    public AppDbContext Context { get; }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}

public static class TestPrompts
{
    public static PromptLibrary Load()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "prompts");
            if (File.Exists(Path.Combine(candidate, "shared", "epam-context.md"))) return new PromptLibrary(candidate);
        }
        throw new DirectoryNotFoundException("prompts/ not found");
    }
}
