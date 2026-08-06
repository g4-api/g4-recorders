using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Uia.Domain;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System.Net.Mime;

namespace G4.Recorders.Uia.Controllers
{
    [ApiController]
    [Route("/api/v4/g4/[controller]")]
    [SwaggerTag(description: "Utilities for peeking UI Automation elements and returning ancestor chains for debugging and inspection.")]
    public class RecorderController(
        IUiaRecorderRepository repository,
        IUiaCursorPositionProvider cursorPositionProvider) : ControllerBase
    {
        [HttpGet("observe")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Capture the virtual desktop",
            Description = "Captures the full virtual desktop as a base64 PNG, along with its coordinate-space " +
                "metrics. Set metricsOnly to skip the actual pixel capture and return only the metrics."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "The captured image (unless metricsOnly) and its coordinate-space metadata.",
            type: typeof(RecorderScreenshotModel),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult GetScreenshot(
            [FromQuery(Name = "metricsOnly")]
            [SwaggerParameter(description: "When true, skips the pixel capture and returns only virtual-desktop bounds.")]
            bool metricsOnly)
        {
            return Ok(repository.GetScreenshot(metricsOnly));
        }

        [HttpPost("point")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Move the physical cursor",
            Description = "Physically moves the desktop cursor to the given physical screen coordinates and " +
                "returns the position it actually settled at."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "The cursor position read back after the move.",
            type: typeof(RecorderPointModel),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult MovePointer(
            [FromQuery(Name = "x")]
            [SwaggerParameter(description: "The horizontal physical screen coordinate to move the cursor to.")]
            int x,

            [FromQuery(Name = "y")]
            [SwaggerParameter(description: "The vertical physical screen coordinate to move the cursor to.")]
            int y)
        {
            return Ok(repository.MovePointer(x, y));
        }

        [HttpGet("element")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Get the UIA element chain at screen coordinates",
            Description = "Resolves the UI Automation element from supplied coordinates, focus, or the current cursor position and returns its ancestor chain for inspection."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "Ancestor chain successfully resolved.",
            type: typeof(object),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status500InternalServerError,
            description: "Unexpected error while resolving the UIA tree.",
            type: typeof(object),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult GetElementChain(
            [FromQuery(Name = "x")]
            [SwaggerParameter(description: "The X screen coordinate in pixels (device-independent if applicable).")]
            int? x,

            [FromQuery(Name = "y")]
            [SwaggerParameter(description: "The Y screen coordinate in pixels (device-independent if applicable).")]
            int? y,

            [FromQuery(Name = "focused")]
            [SwaggerParameter(description: "If true and both coordinates are missing, peek the currently focused " +
                "UI element. Coordinates take precedence when either x or y is supplied.")]
            bool focused)
        {
            // Normalize optional query values once so UIA and Chromium expose identical selection behavior.
            var request = RecorderPeekRequestResolver.Resolve(x, y, focused);

            // Honor any supplied coordinate and use zero for its missing axis before focus is considered.
            if (request.Mode == RecorderPeekMode.Coordinates)
            {
                return Ok(repository.GetElementChain(request.X, request.Y));
            }

            // Resolve keyboard focus only when both coordinate values were omitted and focus was requested.
            if (request.Mode == RecorderPeekMode.Focused)
            {
                return Ok(repository.GetElementChain());
            }

            // Sample the physical cursor only for the final no-coordinate, no-focus fallback.
            if (!cursorPositionProvider.GetCurrent(out var point))
            {
                return Problem(
                    detail: "The current physical cursor position is unavailable.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            // Resolve the sampled point through the existing coordinate path so chain construction remains canonical.
            return Ok(repository.GetElementChain(point.XPos, point.YPos));
        }

        [HttpPost("ground")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Ground a coordinate against the UIA tree",
            Description = "Physically moves the cursor to the given coordinates, peeks the UI Automation element " +
                "found there, and - unless skipOffset is set - resolves the pointer's pixel offset from that " +
                "element's top-left corner. Combines MovePointer, Peek, and offset resolution into one atomic call."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "Ancestor chain successfully resolved, with Offset populated unless skipped.",
            type: typeof(object),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult ResolveGroundedElement(
            [FromQuery(Name = "x")]
            [SwaggerParameter(description: "The horizontal physical screen coordinate to ground against.")]
            int x,

            [FromQuery(Name = "y")]
            [SwaggerParameter(description: "The vertical physical screen coordinate to ground against.")]
            int y,

            [FromQuery(Name = "skipOffset")]
            [SwaggerParameter(description: "When true, skips offset computation and leaves the returned chain's offset null.")]
            bool skipOffset)
        {
            return Ok(repository.ResolveGroundedElement(x, y, skipOffset));
        }

        [HttpPost("focus")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Bring a window to the foreground",
            Description = "Best-effort attempt to restore and focus a running process's top-level window before a " +
                "screen-dependent operation runs. Reports the match/focus outcome rather than throwing when the " +
                "target cannot be found, is ambiguous, or could not be focused."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "The match and focus outcome.",
            type: typeof(RecorderWindowFocusModel),
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
            return Ok(repository.SetWindowFocus(windowTitle, processName));
        }
    }
}
