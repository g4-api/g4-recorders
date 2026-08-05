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
        [HttpGet("observe")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Capture the virtual desktop",
            Description = "Not implemented on the Chromium recorder surface yet - this host targets plain " +
                "net10.0 and is not guaranteed to run on a machine with a real desktop to capture."
        )]
        [SwaggerResponse(StatusCodes.Status501NotImplemented,
            description: "This capability is not implemented on the Chromium recorder surface yet.",
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult GetScreenshot(
            [FromQuery(Name = "metricsOnly")]
            [SwaggerParameter(description: "When true, skips the pixel capture and returns only virtual-desktop bounds.")]
            bool metricsOnly)
        {
            try
            {
                return Ok(repository.GetScreenshot(metricsOnly));
            }
            catch (NotImplementedException exception)
            {
                // Report the missing surface capability distinctly rather than letting it surface as an
                // unhandled 500, matching the same exception-to-HTTP mapping style already used by PeekAsync.
                return Problem(
                    detail: exception.Message,
                    statusCode: StatusCodes.Status501NotImplemented);
            }
        }

        [HttpPost("point")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Move the pointer",
            Description = "Not implemented on the Chromium recorder surface yet - the extension surface has no " +
                "physical desktop cursor to move."
        )]
        [SwaggerResponse(StatusCodes.Status501NotImplemented,
            description: "This capability is not implemented on the Chromium recorder surface yet.",
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult MovePointer(
            [FromQuery(Name = "x")]
            [SwaggerParameter(description: "The horizontal viewport coordinate to move the pointer to.")]
            int x,

            [FromQuery(Name = "y")]
            [SwaggerParameter(description: "The vertical viewport coordinate to move the pointer to.")]
            int y)
        {
            try
            {
                return Ok(repository.MovePointer(x, y));
            }
            catch (NotImplementedException exception)
            {
                // Report the missing surface capability distinctly rather than letting it surface as an
                // unhandled 500, matching the same exception-to-HTTP mapping style already used by PeekAsync.
                return Problem(
                    detail: exception.Message,
                    statusCode: StatusCodes.Status501NotImplemented);
            }
        }

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

        [HttpPost("ground")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Ground a coordinate against the Chromium DOM",
            Description = "Not implemented on the Chromium recorder surface yet - pointer-move and offset " +
                "resolution await this surface's own physical-screen mapping."
        )]
        [SwaggerResponse(StatusCodes.Status501NotImplemented,
            description: "This capability is not implemented on the Chromium recorder surface yet.",
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public async Task<IActionResult> ResolveGroundedElementAsync(
            [FromQuery(Name = "x")]
            [SwaggerParameter(description: "The horizontal viewport coordinate to ground against.")]
            int x,

            [FromQuery(Name = "y")]
            [SwaggerParameter(description: "The vertical viewport coordinate to ground against.")]
            int y,

            [FromQuery(Name = "skipOffset")]
            [SwaggerParameter(description: "When true, skips offset computation and leaves the returned chain's offset null.")]
            bool skipOffset,

            CancellationToken cancellationToken)
        {
            try
            {
                var chain = await repository.ResolveGroundedElementAsync(x, y, skipOffset, cancellationToken).ConfigureAwait(false);

                return Ok(chain);
            }
            catch (NotImplementedException exception)
            {
                // Report the missing surface capability distinctly rather than letting it surface as an
                // unhandled 500, matching the same exception-to-HTTP mapping style already used by PeekAsync.
                return Problem(
                    detail: exception.Message,
                    statusCode: StatusCodes.Status501NotImplemented);
            }
        }

        [HttpPost("focus")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Bring a window to the foreground",
            Description = "Not implemented on the Chromium recorder surface yet - the extension surface has no " +
                "equivalent of an OS-level window handle to focus."
        )]
        [SwaggerResponse(StatusCodes.Status501NotImplemented,
            description: "This capability is not implemented on the Chromium recorder surface yet.",
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult SetWindowFocus(
            [FromQuery(Name = "windowTitle")]
            [SwaggerParameter(description: "A case-insensitive substring to match against a running process's window title.")]
            string windowTitle,

            [FromQuery(Name = "processName")]
            [SwaggerParameter(description: "An exact process name to match. Combined with windowTitle as an AND when both are supplied.")]
            string processName)
        {
            try
            {
                return Ok(repository.SetWindowFocus(windowTitle, processName));
            }
            catch (NotImplementedException exception)
            {
                // Report the missing surface capability distinctly rather than letting it surface as an
                // unhandled 500, matching the same exception-to-HTTP mapping style already used by PeekAsync.
                return Problem(
                    detail: exception.Message,
                    statusCode: StatusCodes.Status501NotImplemented);
            }
        }
    }
}
