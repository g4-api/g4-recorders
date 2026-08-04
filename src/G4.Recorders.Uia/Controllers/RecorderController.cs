using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System.Net.Mime;

using G4.Recorders.Common.Domain.Models;

using G4.Recorders.Uia.Domain;

namespace G4.Recorders.Uia.Controllers
{
    [ApiController]
    [Route("/api/v4/g4/[controller]")]
    [SwaggerTag(description: "Utilities for peeking UI Automation elements and returning ancestor chains for debugging and inspection.")]
    public class RecorderController(
        IUiaRecorderRepository repository,
        IUiaCursorPositionProvider cursorPositionProvider) : ControllerBase
    {
        [HttpGet]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Peek UIA tree at screen coordinates",
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
        public IActionResult Peek(
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
                return Ok(repository.Peek(request.X, request.Y));
            }

            // Resolve keyboard focus only when both coordinate values were omitted and focus was requested.
            if (request.Mode == RecorderPeekMode.Focused)
            {
                return Ok(repository.Peek());
            }

            // Sample the physical cursor only for the final no-coordinate, no-focus fallback.
            if (!cursorPositionProvider.GetCurrent(out var point))
            {
                return Problem(
                    detail: "The current physical cursor position is unavailable.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            // Resolve the sampled point through the existing coordinate path so chain construction remains canonical.
            return Ok(repository.Peek(point.XPos, point.YPos));
        }
    }
}
