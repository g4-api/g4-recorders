using G4.Recorders.Common.Domain.Models.Mcp;

using G4.Recorders.Uia.Domain;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System.ComponentModel.DataAnnotations;
using System.Net.Mime;

namespace G4.Recorders.Uia.Controllers
{
    [ApiController]
    [Route("/api/v4/g4/mcp")]
    [SwaggerTag(description: "Exposes the recorder's fixed 5-tool catalog (Peek, GetScreenshot, MovePointer, " +
        "SetWindowFocus, ResolveGroundedElement) over MCP JSON-RPC, as an alternative to the equivalent REST " +
        "endpoints on RecorderController - both call the same underlying repository, so behavior never diverges.")]
    public class McpController(IRecorderMcpRepository repository) : ControllerBase
    {
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
