using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace EpamPrep.Api.Infrastructure;

/// <summary>Minimal Server-Sent Events writer (POST + fetch streaming on the client, so EventSource is not used).</summary>
public sealed class SseWriter
{
    private readonly HttpResponse _response;
    private readonly JsonSerializerOptions _json;

    private SseWriter(HttpResponse response, JsonSerializerOptions json)
    {
        _response = response;
        _json = json;
    }

    public static SseWriter Start(HttpContext context, JsonSerializerOptions json)
    {
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        var r = context.Response;
        r.StatusCode = StatusCodes.Status200OK;
        r.ContentType = "text/event-stream";
        r.Headers.CacheControl = "no-cache";
        r.Headers["X-Accel-Buffering"] = "no";
        return new SseWriter(r, json);
    }

    public async Task SendAsync(string eventName, object data, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(data, _json);
        await _response.WriteAsync($"event: {eventName}\ndata: {payload}\n\n", ct);
        await _response.Body.FlushAsync(ct);
    }
}
