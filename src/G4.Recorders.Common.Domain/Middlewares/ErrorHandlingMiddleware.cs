using G4.Recorders.Common.Domain.Models;

using G4.Extensions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace G4.Recorders.Common.Domain.Middlewares
{
    /// <summary>
    /// Middleware to handle exceptions and enhance error responses in the HTTP request pipeline.
    /// </summary>
    /// <param name="logger">The logger instance for logging information.</param>
    /// <param name="next">The next middleware in the pipeline.</param>
    public class ErrorHandlingMiddleware(ILogger<ErrorHandlingMiddleware> logger, RequestDelegate next)
    {
        #region *** Fields  ***
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly RequestDelegate _next = next;
        private readonly ILogger<ErrorHandlingMiddleware> _logger = logger;
        #endregion

        #region *** Methods ***
        /// <summary>
        /// Invokes the middleware to handle the HTTP request and response.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <returns>A task that represents the completion of request processing.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            var originalBodyStream = context.Response.Body;

            await using var memoryStream = new MemoryStream();

            context.Response.Body = memoryStream;

            try
            {
                await _next(context);

                memoryStream.Seek(0, SeekOrigin.Begin);

                var responseBody = await new StreamReader(memoryStream).ReadToEndAsync();

                responseBody = Regex.Replace(
                    input: responseBody,
                    pattern: @"(?i),({?)""traceId"".*?}",
                    replacement: string.Empty);

                memoryStream.Seek(0, SeekOrigin.Begin);

                var isSuccess = context.Response.StatusCode < 400;

                var isJsonResponse = responseBody.AssertJson();

                if (isSuccess)
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    await memoryStream.CopyToAsync(originalBodyStream);
                    return;
                }

                GenericErrorModel errorResponse;

                if (isJsonResponse)
                {
                    errorResponse = JsonSerializer.Deserialize<GenericErrorModel>(responseBody, s_jsonOptions) ?? new GenericErrorModel(context)
                    {
                        Status = context.Response.StatusCode,
                        Title = "An error occurred.",
                        Detail = "No additional details available."
                    };
                }
                else
                {
                    errorResponse = new GenericErrorModel(context)
                    {
                        Status = context.Response.StatusCode,
                        Title = "An error occurred.",
                        Detail = "No additional details available."
                    };
                }

                var routeData = context.GetRouteData().Values.ToDictionary(k => k.Key, v => v.Value);

                errorResponse.AddRouteData(routeData);

                var modifiedResponse = JsonSerializer.Serialize(errorResponse, s_jsonOptions);

                context.Response.ContentLength = System.Text.Encoding.UTF8.GetByteCount(modifiedResponse);

                context.Response.Body = originalBodyStream;

                context.Response.ContentType = MediaTypeNames.Application.ProblemJson;

                await context.Response.WriteAsync(modifiedResponse);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "An unhandled exception occurred.");

                context.Response.Body = originalBodyStream;

                await HandleExceptionAsync(context, exception: e, s_jsonOptions);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception, JsonSerializerOptions jsonOptions)
        {
            context.Response.ContentType = MediaTypeNames.Application.ProblemJson;

            (int statusCode, string title) = exception.GetBaseException().GetType().Name switch
            {
                "InvalidCredentialException" => (StatusCodes.Status401Unauthorized, "Invalid credentials provided."),
                "NoAvailableRunningTimeException" => (StatusCodes.Status403Forbidden, "No available running time."),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
            };

            context.Response.StatusCode = statusCode;

            var baseException = exception.GetBaseException();

            var routeData = context.GetRouteData().Values.ToDictionary(k => k.Key, v => v.Value);

            var response = new GenericErrorModel(context)
            {
                Status = context.Response.StatusCode,
                Title = title,
                Detail = baseException.Message
            };

            response.AddRouteData(routeData);

            response.AddErrors(new Dictionary<string, string[]>
            {
                [baseException.GetType().Name] = [baseException.ToString()]
            });

            var jsonResponse = JsonSerializer.Serialize(response, jsonOptions);

            return context.Response.WriteAsync(jsonResponse);
        }
        #endregion
    }
}
