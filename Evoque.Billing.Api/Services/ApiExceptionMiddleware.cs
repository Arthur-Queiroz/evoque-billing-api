using Evoque.Billing.Api.Domain;

namespace Evoque.Billing.Api.Services;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException exception)
        {
            logger.LogWarning(exception, "Regra de domínio impediu a requisição {RequestPath}.", context.Request.Path);
            context.Response.StatusCode = GetStatusCode(exception);
            await context.Response.WriteAsJsonAsync(new { error = exception.Message });
        }
    }

    private static int GetStatusCode(DomainException exception)
    {
        return exception switch
        {
            NotFoundException => StatusCodes.Status404NotFound,
            ConflictException => StatusCodes.Status409Conflict,
            ExternalOperationNotAllowedException => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };
    }
}
