using G4.Recorders.Chromium.Domain;
using G4.Recorders.Chromium.Domain.Models;

using G4.Recorders.Common.Domain.Models;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Chromium.Controllers
{
    [ApiController]
    [Route("/api/v4/g4/[controller]")]
    [SwaggerTag(description: "Utilities for resolving Chromium DOM elements through the connected recorder extension.")]
    public class RecorderController(IChromiumRecorderRepository repository) : ControllerBase
    {
        [HttpGet]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Peek the Chromium DOM",
            Description = "Resolves a DOM element from viewport coordinates, focus, or the current pointer position. " +
                "Any supplied coordinate takes precedence and a missing coordinate axis defaults to zero."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "DOM ancestor chain successfully resolved, or an empty chain when no element matched.",
            type: typeof(ChromiumChainModel),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status503ServiceUnavailable,
            description: "No unambiguous recorder extension is connected.",
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status504GatewayTimeout,
            description: "The recorder extension did not answer before the lookup timeout.",
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public async Task<IActionResult> PeekAsync(
            [FromQuery(Name = "x")]
            [SwaggerParameter(description: "The optional X coordinate relative to the active Chromium frame viewport.")]
            int? x,

            [FromQuery(Name = "y")]
            [SwaggerParameter(description: "The optional Y coordinate relative to the active Chromium frame viewport.")]
            int? y,

            [FromQuery(Name = "focused")]
            [SwaggerParameter(description: "If true and both coordinates are missing, resolve the focused DOM element. " +
                "Coordinates take precedence when either axis is supplied.")]
            bool focused,

            CancellationToken cancellationToken)
        {
            // Normalize caller intent before crossing the asynchronous browser transport boundary.
            var request = RecorderPeekRequestResolver.Resolve(x, y, focused);

            try
            {
                // Wait for the connected extension to resolve the selected DOM target and return its chain.
                var chain = await repository.GetAsync(request, cancellationToken).ConfigureAwait(false);

                return Ok(chain);
            }
            catch (TimeoutException exception)
            {
                // Report extension latency distinctly so callers can separate it from connection availability.
                return Problem(
                    detail: exception.Message,
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (InvalidOperationException exception)
            {
                // Report connection and extension execution failures without exposing transport internals.
                return Problem(
                    detail: exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }
    }
}
