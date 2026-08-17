using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// Turns an unhandled exception into an RFC 7807 problem body. A database failure is a
/// transient outage of a dependency, so it answers <c>503</c>; anything else is <c>500</c>.
/// The exception is logged but never surfaced: provider messages carry the host, port,
/// database name and user of the connection (DEV-14).
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService
) : IExceptionHandler
{
    public const string DatabaseUnavailableTitle = "Service Unavailable";

    public const string DatabaseUnavailableDetail =
        "The service is temporarily unable to reach its data store. Please try again shortly.";

    public const string UnexpectedErrorTitle = "Internal Server Error";

    public const string UnexpectedErrorDetail = "An unexpected error occurred.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        logger.LogError(exception, "Unhandled exception occurred");

        var databaseFailure = IsDatabaseFailure(exception);

        httpContext.Response.StatusCode = databaseFailure
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                // The exception is deliberately not attached — nothing derived from it may
                // reach the caller.
                ProblemDetails =
                {
                    Status = httpContext.Response.StatusCode,
                    Title = databaseFailure ? DatabaseUnavailableTitle : UnexpectedErrorTitle,
                    Detail = databaseFailure ? DatabaseUnavailableDetail : UnexpectedErrorDetail,
                },
            }
        );
    }

    /// <summary>
    /// A provider failure often arrives wrapped (for example inside a
    /// <c>DbUpdateException</c>), so the whole inner chain is inspected.
    /// </summary>
    private static bool IsDatabaseFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException)
                return true;
        }

        return false;
    }
}
