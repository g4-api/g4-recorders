using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Sap.Domain;
using G4.Recorders.Sap.Domain.Middlewares;
using G4.Recorders.Sap.Domain.Models;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Mime;

namespace G4.Recorders.Sap.Controllers
{
    [ApiController]
    [Route("/api/v4/g4/recorders/sap/[controller]")]
    [SwaggerTag(description: "Utilities for peeking SAP GUI controls, the desktop primitives (screenshot, pointer, window " +
        "focus, grounding) the UIA recorder offers, the recording status, and SAP Logon start and stop.")]
    public class RecorderController(
        ISapRecorderRepository repository,
        ISapCursorPositionProvider cursorPositionProvider,
        SapRecordingStatusStore statusStore,
        ISapRecorderSettingsResolver settingsResolver,
        ISapLogonLauncher launcher) : ControllerBase
    {
        [HttpPost("logon")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Start SAP Logon on the recorder host",
            Description = "Makes SAP GUI available: attaches to a running SAP Logon, or starts one. Each value comes " +
                "from the request body, then the G4_SAP_LOGON_* environment variables, then appsettings.json (G4:Sap:Logon)."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "SAP GUI is available; processId is zero when SAP Logon was already running or no path resolved.",
            type: typeof(SapRecorderStartModel),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status400BadRequest,
            description: "SAP Logon was not found on the recorder host.",
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status503ServiceUnavailable,
            description: "SAP Logon started, but SAP GUI Scripting did not become available.",
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult StartLogon([FromBody] SapLogonParametersModel logon)
        {
            // Resolve the SAP Logon values with the same precedence as StartRecorder.
            var options = settingsResolver.Resolve(new SapRecorderParametersModel { SapLogon = logon });

            try
            {
                // Start or attach on the recorder host, so remote clients never manage the process themselves.
                var (processId, isLaunched) = launcher.Start(options.SapLogon);

                return Ok(new SapRecorderStartModel { IsLaunched = isLaunched, Options = options, ProcessId = processId });
            }
            catch (FileNotFoundException exception)
            {
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }

        [HttpDelete("logon/{processId:int}")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Stop a SAP Logon started by the recorder",
            Description = "Closes a SAP Logon this recorder host started, ending it when it does not exit in time. " +
                "Processes the recorder did not start are never touched."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "True when the process was stopped; false when the recorder did not start it or it already exited.",
            type: typeof(bool),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult StopLogon(int processId)
        {
            return Ok(launcher.Stop(processId));
        }

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

        [HttpPost("ground")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Ground a coordinate against SAP GUI",
            Description = "Physically moves the cursor to the given coordinates, resolves the SAP GUI control at the " +
                "settled cursor position, and - unless skipOffset is set - resolves the pointer's pixel offset from that " +
                "control's top-left corner in one call."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "Chain resolved, with its offset unless skipped; empty when no SAP control is at the point.",
            type: typeof(object),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status503ServiceUnavailable,
            description: "SAP Logon is not running, scripting is disabled, or SAP GUI did not answer in time.",
            type: typeof(ProblemDetails),
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
            try
            {
                // Ground on the recorder host; the cursor moves there, not on the caller's machine.
                return Ok(repository.ResolveGroundedElement(x, y, skipOffset));
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
                // Report SAP GUI being unavailable as a service condition the caller can retry.
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
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
        [SwaggerResponse(StatusCodes.Status400BadRequest,
            description: "Neither windowTitle nor processName was supplied.",
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
            catch (ArgumentException exception)
            {
                // Report a request without any identifier as the caller's mistake.
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }

        [HttpGet("element")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Get the SAP control chain at screen coordinates",
            Description = "Resolves the SAP GUI control from supplied coordinates, focus, or the current cursor position and returns its chain for inspection."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "Chain successfully resolved; empty when no SAP control matched.",
            type: typeof(object),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status500InternalServerError,
            description: "The current cursor position could not be read.",
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status503ServiceUnavailable,
            description: "SAP Logon is not running, scripting is disabled, or SAP GUI did not answer in time.",
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult GetElementChain(
            [FromQuery(Name = "x")]
            [SwaggerParameter(description: "The X screen coordinate in physical pixels.")]
            int? x,

            [FromQuery(Name = "y")]
            [SwaggerParameter(description: "The Y screen coordinate in physical pixels.")]
            int? y,

            [FromQuery(Name = "focused")]
            [SwaggerParameter(description: "If true and both coordinates are missing, peek the focused SAP control. " +
                "Coordinates take precedence when either x or y is supplied.")]
            bool focused)
        {
            // Normalize optional query values once so every recorder exposes identical selection behavior.
            var request = RecorderPeekRequestResolver.Resolve(x, y, focused);

            // Sample the physical cursor only for the final no-coordinate, no-focus fallback.
            var point = new RecorderPointModel { XPos = request.X, YPos = request.Y };
            if (request.Mode == RecorderPeekMode.Current && !cursorPositionProvider.GetCurrent(out point))
            {
                return Problem(
                    detail: "The current physical cursor position is unavailable.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            try
            {
                // Resolve focus, or the explicit or sampled point, through the repository's canonical chain builder.
                var chain = request.Mode == RecorderPeekMode.Focused
                    ? repository.GetElementChain()
                    : repository.GetElementChain(point.XPos, point.YPos);

                return Ok(chain);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
                // Report SAP GUI being unavailable as a service condition the caller can retry.
                return Problem(
                    detail: exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }

        [HttpGet("status")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Get the SAP recording status of each session",
            Description = "Returns whether each SAP session reports SAP actions to the recorder, and why not when it does not. " +
                "The list is empty while no hub client is connected, because sessions are attached only while a client listens."
        )]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "The per-session recording status.",
            type: typeof(List<SapSessionStatusModel>),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult GetRecordingStatus()
        {
            return Ok(statusStore.GetSessions());
        }
    }
}
