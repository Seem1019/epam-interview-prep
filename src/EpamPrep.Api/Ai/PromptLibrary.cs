using System.Text.Json;

namespace EpamPrep.Api.Ai;

/// <summary>
/// Loads system prompts and JSON schemas from the repo-level <c>prompts/</c> folder.
/// Files are re-read on every call so prompt tweaks apply without restarting the API.
/// </summary>
public sealed class PromptLibrary
{
    private readonly string _directory;

    public PromptLibrary(IConfiguration configuration, IWebHostEnvironment env)
    {
        var configured = configuration["Prompts:Directory"];
        _directory = !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : FindUpwards(env.ContentRootPath, "prompts")
              ?? throw new DirectoryNotFoundException("No se encontró la carpeta prompts/. Configura Prompts:Directory.");
    }

    internal PromptLibrary(string directory) => _directory = directory;

    public string Directory => _directory;

    public string Get(string name) =>
        File.ReadAllText(Path.Combine(_directory, name + ".md")).Replace("\r\n", "\n").Trim();

    public JsonElement Schema(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "schemas", name + ".json")));
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// System blocks ordered from most to least stable, each with a cache breakpoint:
    /// [EPAM context + rubric] → [feature instructions] → [candidate profile].
    /// Anything volatile (time, topic, answer) goes in the user message, never here.
    /// </summary>
    public IReadOnlyList<AiSystemBlock> SystemFor(string feature, string? profile) =>
    [
        new(Get("shared/epam-context"), CacheBreakpoint: true),
        new(Get(feature), CacheBreakpoint: true),
        new(ProfileBlock(profile), CacheBreakpoint: true),
    ];

    internal static string ProfileBlock(string? profile) =>
        string.IsNullOrWhiteSpace(profile)
            ? "# Candidate profile\n\nThe candidate has NOT provided a CV or experience summary yet. " +
              "Do not invent or assume any facts about their companies, projects, years or achievements. " +
              "Experience questions must stay open (\"Tell me about a time you...\") without presupposing details."
            : "# Candidate profile (written by the candidate)\n\n" + profile.Trim();

    private static string? FindUpwards(string start, string folder)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, folder);
            if (System.IO.Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "shared", "epam-context.md")))
            {
                return candidate;
            }
        }
        return null;
    }
}
