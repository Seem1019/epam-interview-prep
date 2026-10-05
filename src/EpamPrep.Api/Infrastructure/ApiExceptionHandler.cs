using EpamPrep.Api.Ai;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EpamPrep.Api.Infrastructure;

/// <summary>Maps domain/AI exceptions to RFC 7807 ProblemDetails with a stable "code" the UI can switch on.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var status = ex switch
        {
            AiNotConfiguredException or AiLoginRequiredException => StatusCodes.Status503ServiceUnavailable,
            AiException or Anthropic.Exceptions.AnthropicApiException => StatusCodes.Status502BadGateway,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            ArgumentException => StatusCodes.Status400BadRequest,
            InvalidOperationException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };
        if (status >= 500) logger.LogError(ex, "Request failed");

        http.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = ex,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = Endpoints.UserMessage(ex),
                Extensions = { ["code"] = Endpoints.ErrorCode(ex) },
            },
        });
    }
}
