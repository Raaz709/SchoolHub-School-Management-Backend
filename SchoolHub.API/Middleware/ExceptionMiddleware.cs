using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace SchoolHub.API.Middleware
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IHostEnvironment _env;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await WriteErrorAsync(context, ex);
            }
        }

        private async Task WriteErrorAsync(HttpContext context, Exception ex)
        {
            var (status, message) = Classify(ex);

            if (status >= 500)
            {
                // Only genuine server faults are logged as errors; a bad request
                // from a client is not worth an error-level stack trace.
                _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            }
            else
            {
                _logger.LogInformation("{Status} on {Method} {Path}: {Message}", status, context.Request.Method, context.Request.Path, message);
            }

            // The response may already be on the wire; rewriting the status code
            // would throw, so let it propagate rather than corrupting the body.
            if (context.Response.HasStarted) return;

            context.Response.Clear();
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = status;

            object response = _env.IsDevelopment()
                ? new { StatusCode = status, Message = message, Detail = ex.Message, Trace = ex.StackTrace }
                : new { StatusCode = status, Message = message };

            // Matches the rest of the API: PropertyNamingPolicy is null elsewhere.
            var json = JsonSerializer.Serialize(response);

            await context.Response.WriteAsync(json);
        }

        /// <summary>
        /// Turns well-understood failures into the status code the caller deserves.
        ///
        /// Previously every unhandled exception became a 500, so ordinary client
        /// mistakes — a duplicate roll number, submitting to an assignment that
        /// does not exist, a malformed JSON body — looked like server outages.
        /// </summary>
        private (int Status, string Message) Classify(Exception ex)
        {
            if (ex is PostgresException pg)
            {
                // SqlState values, per PostgreSQL error code appendix.
                return pg.SqlState switch
                {
                    PostgresErrorCodes.UniqueViolation =>
                        ((int)HttpStatusCode.Conflict, "That value is already in use by another record."),
                    PostgresErrorCodes.ForeignKeyViolation =>
                        ((int)HttpStatusCode.BadRequest, "A referenced record does not exist."),
                    PostgresErrorCodes.NotNullViolation =>
                        ((int)HttpStatusCode.BadRequest, "A required field was missing."),
                    PostgresErrorCodes.CheckViolation =>
                        ((int)HttpStatusCode.BadRequest, "A value failed a database constraint."),
                    PostgresErrorCodes.StringDataRightTruncation or
                    PostgresErrorCodes.NumericValueOutOfRange or
                    PostgresErrorCodes.InvalidTextRepresentation =>
                        ((int)HttpStatusCode.BadRequest, "A supplied value was not valid."),
                    _ => ((int)HttpStatusCode.InternalServerError, "Internal Server Error occurred."),
                };
            }

            // Malformed JSON body or an unparsable route/query value.
            if (ex is BadHttpRequestException)
            {
                return ((int)HttpStatusCode.BadRequest, "The request could not be understood.");
            }

            if (ex is FormatException or OverflowException or ArgumentException)
            {
                return ((int)HttpStatusCode.BadRequest, "A supplied value was not valid.");
            }

            return ((int)HttpStatusCode.InternalServerError, "Internal Server Error occurred.");
        }
    }
}
