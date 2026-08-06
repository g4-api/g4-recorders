using G4.Recorders.Common.Domain.Models.Mcp;

using G4.Recorders.Uia.Domain;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Uia.Controllers
{
    [ApiController]
    [Route("/api/v4/g4/mcp")]
    [SwaggerTag(description: "Exposes the recorder's fixed 5-tool catalog (Peek, GetScreenshot, MovePointer, " +
        "SetWindowFocus, ResolveGroundedElement) over MCP JSON-RPC, as an alternative to the equivalent REST " +
        "endpoints on RecorderController - both call the same underlying repository, so behavior never diverges.")]
    public class McpController(IRecorderMcpRepository repository) : ControllerBase
    {
        [HttpGet]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Establish SSE stream",
            Description = "Opens a text/event-stream channel for real-time updates and heartbeats (n8n-compatible)."
        )]
        [SwaggerResponse(StatusCodes.Status200OK, description: "SSE stream established.", contentTypes: "text/event-stream")]
        #endregion
        public async Task Get(CancellationToken token)
        {
            // Required SSE headers (n8n is strict about these)
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers.Connection = "keep-alive";
            Response.Headers["X-Accel-Buffering"] = "no";    // disable nginx buffering if present
            Response.Headers.ContentEncoding = "identity";   // prevent gzip on proxies

            // Start the response immediately so clients consider the stream "open"
            await Response.StartAsync(token);

            // Send an initial comment + heartbeat quickly so n8n marks it connected
            await Response.WriteAsync(": connected\n\n", token);
            await Response.Body.FlushAsync(token);

            // Periodic heartbeats (comments are valid SSE frames and cheaper than data events)
            // Keep them reasonably frequent to survive proxies/load balancers.
            var heartbeat = TimeSpan.FromSeconds(15);

            try
            {
                while (!token.IsCancellationRequested && !HttpContext.RequestAborted.IsCancellationRequested)
                {
                    await Task.Delay(heartbeat, token);

                    // Heartbeat
                    await Response.WriteAsync(": heartbeat\n\n", token);
                    await Response.Body.FlushAsync(token);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or server is shutting down – swallow gracefully.
            }
            finally
            {
                // Try to complete the response cleanly.
                if (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    await Response.CompleteAsync();
                }
            }
        }

        [HttpPost]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Handle MCP JSON-RPC requests",
            Description = "Dispatches the initialize, notifications/initialized, tools/list, and tools/call " +
                "JSON-RPC methods against the recorder's fixed tool catalog."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "Initialize result, tool catalog, or tool invocation result.",
            type: typeof(McpResponseModel),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status202Accepted, description: "Initialization notification acknowledged.")]
        [SwaggerResponse(StatusCodes.Status400BadRequest,
            description: "Unknown or unsupported JSON-RPC method.",
            type: typeof(object),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult Post(
            [FromBody, Required]
            [SwaggerParameter(description: "The JSON-RPC request payload: method (initialize/tools list/tools " +
                "call/initialized notification), id, and params.")]
            McpRequestModel mcpRequest)
        {
            // Dispatch based on the JSON-RPC method, matching the same switch shape already used by
            // G4.Services' own McpController for this exact protocol.
            return mcpRequest.Method switch
            {
                "initialize" => Ok(repository.Initialize(mcpRequest.Id)),
                "notifications/initialized" => Accepted(),
                "tools/list" => Ok(repository.FindTools(mcpRequest.Id)),
                "tools/call" => Ok(repository.CallTool(mcpRequest.Parameters, mcpRequest.Id)),
                _ => BadRequest(new { error = $"Unknown method '{mcpRequest.Method}'" })
            };
        }
    }
}
