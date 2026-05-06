using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace VendingManager.Web.Middleware;

/// <summary>
/// Middleware global de ProblemDetails según RFC 7807.
/// Captura todas las excepciones no manejadas y retorna una respuesta conforme a RFC 7807.
/// </summary>
public sealed class GlobalProblemDetailsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;
    private readonly ILogger<GlobalProblemDetailsMiddleware> _logger;

    public GlobalProblemDetailsMiddleware(
        RequestDelegate next,
        IHostEnvironment env,
        ILogger<GlobalProblemDetailsMiddleware> logger)
    {
        _next = next;
        _env = env;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        _logger.LogError(exception,
            "Unhandled exception occurred. ExceptionType: {ExceptionType}, TraceId: {TraceId}",
            exception.GetType().FullName,
            traceId);

        var (statusCode, title, detail) = MapException(exception);

        // In Production, suppress internal details
        var productionSafeDetail = _env.IsProduction()
            ? "An error occurred while processing your request."
            : detail;

        var problemDetails = new
        {
            type = "https://tools.ietf.org/html/rfc7807",
            title,
            status = statusCode,
            detail = productionSafeDetail,
            traceId
        };

        context.Response.ContentType = "application/problem+json; charset=utf-8";
        context.Response.StatusCode = statusCode;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(problemDetails, options);
        await context.Response.WriteAsync(json);
    }

    private static (int StatusCode, string Title, string Detail) MapException(Exception exception)
    {
        return exception switch
        {
            ArgumentException argEx => (
                (int)HttpStatusCode.BadRequest,
                "One or more validation errors occurred.",
                argEx.Message
            ),

            UnauthorizedAccessException => (
                (int)HttpStatusCode.Unauthorized,
                "Unauthorized",
                "Authentication is required to access this resource."
            ),

            ForbiddenAccessException => (
                (int)HttpStatusCode.Forbidden,
                "Access denied",
                "You do not have permission to access this resource."
            ),

            KeyNotFoundException keyEx => (
                (int)HttpStatusCode.NotFound,
                "Resource not found",
                keyEx.Message
            ),

            InvalidOperationException invalidOpEx => (
                (int)HttpStatusCode.Conflict,
                "Business rule violation",
                invalidOpEx.Message
            ),

            DbUpdateException dbEx when dbEx.InnerException?.Message.Contains("REFERENCE constraint") == true
                || dbEx.InnerException?.Message.Contains("foreign key") == true => (
                (int)HttpStatusCode.Conflict,
                "Database constraint violation",
                dbEx.InnerException?.Message ?? "A database constraint violation occurred."
            ),

            DbUpdateConcurrencyException => (
                (int)HttpStatusCode.Conflict,
                "Concurrent modification detected",
                "The resource was modified by another process. Please refresh and try again."
            ),

            _ => (
                (int)HttpStatusCode.InternalServerError,
                "An error occurred while processing your request.",
                exception.Message
            )
        };
    }
}

/// <summary>
/// Excepción personalizada para escenarios de acceso prohibido (403).
/// </summary>
public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException() : base("Access denied.") { }
    public ForbiddenAccessException(string message) : base(message) { }
    public ForbiddenAccessException(string message, Exception inner) : base(message, inner) { }
}
