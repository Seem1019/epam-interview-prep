using System.Text.Json.Serialization;
using EpamPrep.Api;
using EpamPrep.Api.Ai;
using EpamPrep.Api.Data;
using EpamPrep.Api.Features.Dashboard;
using EpamPrep.Api.Features.Interview;
using EpamPrep.Api.Features.Lessons;
using EpamPrep.Api.Features.Pronunciation;
using EpamPrep.Api.Features.Quiz;
using EpamPrep.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ----- Options -----
builder.Services.Configure<AnthropicOptions>(config.GetSection(AnthropicOptions.Section));
builder.Services.PostConfigure<AnthropicOptions>(o =>
{
    // Env var wins over user-secrets; neither ever lives in appsettings.json.
    o.ApiKey = config["ANTHROPIC_API_KEY"] is { Length: > 0 } env ? env : config["Anthropic:ApiKey"];
});
builder.Services.Configure<InterviewOptions>(config.GetSection("Interview"));

// ----- Infrastructure -----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PromptLibrary>();
// Provider: "cli" = local Claude Code with your Claude.ai subscription (default, no API key);
// "api" = Anthropic API with ANTHROPIC_API_KEY (pay per use). DemoMode overrides both.
builder.Services.AddSingleton<IClaudeClient>(sp =>
    config.GetValue<bool>("Anthropic:DemoMode") ? new DemoClaudeClient()
    : string.Equals(config["Anthropic:Provider"], "api", StringComparison.OrdinalIgnoreCase)
        ? ActivatorUtilities.CreateInstance<AnthropicClaudeClient>(sp)
        : ActivatorUtilities.CreateInstance<ClaudeCliClient>(sp));

// Demo mode gets its own database so canned results never pollute real progress.
var dbFile = config.GetValue<bool>("Anthropic:DemoMode") ? "epam-prep.demo.db" : "epam-prep.db";
var dbPath = Path.Combine(builder.Environment.ContentRootPath, dbFile);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(config.GetConnectionString("Default") ?? $"Data Source={dbPath}"));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// ----- Features -----
builder.Services.AddScoped<InterviewService>();
builder.Services.AddScoped<QuizService>();
builder.Services.AddScoped<LessonService>();
builder.Services.AddScoped<PronunciationService>();
builder.Services.AddScoped<DashboardService>();

var app = builder.Build();

// Local single-user app: applying migrations at startup is fine here.
// (In a multi-instance production deployment you'd run an idempotent script from the pipeline instead.)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DataSeeder.SeedAsync(db);
}

app.UseExceptionHandler();

// Serve the built front-end (web/dist) when it exists; in dev, Vite serves it and proxies /api here.
var dist = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "web", "dist"));
if (Directory.Exists(dist))
{
    var files = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dist);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });
}

app.MapApi();

var ai = app.Services.GetRequiredService<IClaudeClient>();
switch (ai)
{
    case ClaudeCliClient cli when cli.IsConfigured:
        app.Logger.LogInformation("IA: Claude Code local ({Exe}) con tu suscripción, modelo {Model}.", cli.Executable, config["Anthropic:Cli:Model"]);
        break;
    case ClaudeCliClient:
        app.Logger.LogWarning("IA: no se encontró el ejecutable 'claude'. Instala Claude Code o configura Anthropic:Cli:Path.");
        break;
    default:
        app.Logger.LogInformation(ai.IsConfigured ? "IA: API de Anthropic, modelo {Model}." : "IA: falta ANTHROPIC_API_KEY.", config["Anthropic:Model"]);
        break;
}

app.Run();

public partial class Program;

