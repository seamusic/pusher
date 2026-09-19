using MessagePusher.Application.Results;
using MessagePusher.Application.Services;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (UnauthorizedException ex)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(ApiResult.Fail(ex.Message));
        }
        catch (WebhookHttpException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(ApiResult.Fail(ex.Message));
        }
        catch (BusinessException ex)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(ApiResult.Fail(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(ApiResult.Fail("内部服务器错误"));
        }
    }
}
