using G4.Recorders.Android.Domain;
using G4.Recorders.Android.Domain.Models;
using G4.Recorders.Common.Domain.Models;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System;
using System.IO;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Controllers
{
    [ApiController]
    [Route("/api/v4/g4/recorders/android/[controller]")]
    [SwaggerTag(description: Catalog.Tag)]
    public class RecorderController(IAndroidRecorderDomain domain) : ControllerBase
    {
        #region *** Methods      ***
        [HttpGet("element")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.GetElementChain.Summary, Description = Catalog.GetElementChain.Description)]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: Catalog.GetElementChain.Responses.Ok,
            type: typeof(AndroidChainModel),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status503ServiceUnavailable,
            description: Catalog.GetElementChain.Responses.ServiceUnavailable,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status504GatewayTimeout,
            description: Catalog.GetElementChain.Responses.GatewayTimeout,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public async Task<IActionResult> GetElementChainAsync(
            [FromQuery(Name = "x")]
            [SwaggerParameter(description: Catalog.GetElementChain.Parameters.X)]
            int? x,

            [FromQuery(Name = "y")]
            [SwaggerParameter(description: Catalog.GetElementChain.Parameters.Y)]
            int? y,

            [FromQuery(Name = "focused")]
            [SwaggerParameter(description: Catalog.GetElementChain.Parameters.Focused)]
            bool focused,

            CancellationToken cancellationToken)
        {
            // Normalize caller intent before crossing the asynchronous app transport.
            var request = RecorderPeekRequestResolver.Resolve(x, y, focused);

            try
            {
                // Wait for the connected recorder app to resolve the node and return its chain.
                var chain = await domain.Repository.GetElementChainAsync(request, cancellationToken).ConfigureAwait(false);

                return Ok(chain);
            }
            catch (TimeoutException exception)
            {
                // Report app latency apart from connection availability.
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (InvalidOperationException exception)
            {
                // Report connection and app-side failures without transport internals.
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }

        [HttpGet("observe")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.GetScreenshot.Summary, Description = Catalog.GetScreenshot.Description)]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: Catalog.GetScreenshot.Responses.Ok,
            type: typeof(RecorderScreenshotModel),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status503ServiceUnavailable,
            description: Catalog.GetScreenshot.Responses.ServiceUnavailable,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public async Task<IActionResult> GetScreenshotAsync(
            [FromQuery(Name = "metricsOnly")]
            [SwaggerParameter(description: Catalog.GetScreenshot.Parameters.MetricsOnly)]
            bool metricsOnly)
        {
            try
            {
                // Capture through the launcher, which owns the device the recording runs on.
                var screenshot = await domain.Launcher.GetScreenshotAsync(metricsOnly).ConfigureAwait(false);

                return Ok(screenshot);
            }
            catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException or TimeoutException)
            {
                // A missing adb or device is a recorder-host availability problem, not a caller error.
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }

        [HttpPost("point")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.MovePointer.Summary, Description = Catalog.MovePointer.Description)]
        [SwaggerResponse(StatusCodes.Status501NotImplemented,
            description: Catalog.Common.NotImplemented,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult MovePointer(
            [FromQuery(Name = "x")]
            [SwaggerParameter(description: Catalog.MovePointer.Parameters.X)]
            int x,

            [FromQuery(Name = "y")]
            [SwaggerParameter(description: Catalog.MovePointer.Parameters.Y)]
            int y)
        {
            try
            {
                return Ok(domain.Repository.MovePointer(x, y));
            }
            catch (NotImplementedException exception)
            {
                // Report the missing capability distinctly instead of an unhandled 500.
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status501NotImplemented);
            }
        }

        [HttpPost("ground")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.ResolveGroundedElement.Summary, Description = Catalog.ResolveGroundedElement.Description)]
        [SwaggerResponse(StatusCodes.Status501NotImplemented,
            description: Catalog.Common.NotImplemented,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public async Task<IActionResult> ResolveGroundedElementAsync(
            [FromQuery(Name = "x")]
            [SwaggerParameter(description: Catalog.ResolveGroundedElement.Parameters.X)]
            int x,

            [FromQuery(Name = "y")]
            [SwaggerParameter(description: Catalog.ResolveGroundedElement.Parameters.Y)]
            int y,

            [FromQuery(Name = "skipOffset")]
            [SwaggerParameter(description: Catalog.ResolveGroundedElement.Parameters.SkipOffset)]
            bool skipOffset,

            CancellationToken cancellationToken)
        {
            try
            {
                var chain = await domain.Repository.ResolveGroundedElementAsync(x, y, skipOffset, cancellationToken).ConfigureAwait(false);

                return Ok(chain);
            }
            catch (NotImplementedException exception)
            {
                // Report the missing capability distinctly instead of an unhandled 500.
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status501NotImplemented);
            }
        }

        [HttpPost("focus")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.SetWindowFocus.Summary, Description = Catalog.SetWindowFocus.Description)]
        [SwaggerResponse(StatusCodes.Status501NotImplemented,
            description: Catalog.Common.NotImplemented,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult SetWindowFocus(
            [FromQuery(Name = "windowTitle")]
            [SwaggerParameter(description: Catalog.SetWindowFocus.Parameters.WindowTitle)]
            string windowTitle,

            [FromQuery(Name = "processName")]
            [SwaggerParameter(description: Catalog.SetWindowFocus.Parameters.ProcessName)]
            string processName)
        {
            try
            {
                return Ok(domain.Repository.SetWindowFocus(windowTitle, processName));
            }
            catch (NotImplementedException exception)
            {
                // Report the missing capability distinctly instead of an unhandled 500.
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status501NotImplemented);
            }
        }

        [HttpPost("device")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.StartDevice.Summary, Description = Catalog.StartDevice.Description)]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: Catalog.StartDevice.Responses.Ok,
            type: typeof(int),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status400BadRequest,
            description: Catalog.StartDevice.Responses.BadRequest,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public async Task<IActionResult> StartDeviceAsync([FromBody] DriverParametersModel driverParameters)
        {
            try
            {
                // The recorder host runs adb itself, so remote clients never reach the device directly.
                var processId = await domain.Launcher.StartAsync(driverParameters).ConfigureAwait(false);

                return Ok(processId);
            }
            catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException or TimeoutException)
            {
                // Report readiness failures of the recorder host or the device as caller-visible problems.
                return Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }

        [HttpDelete("device/{processId:int}")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.StopDevice.Summary, Description = Catalog.StopDevice.Description)]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: Catalog.StopDevice.Responses.Ok,
            type: typeof(bool),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public async Task<IActionResult> StopDeviceAsync(int processId)
        {
            // Stop never throws; every restore step that fails is logged on the recorder host.
            var isStopped = await domain.Launcher.StopAsync(processId).ConfigureAwait(false);

            return Ok(isStopped);
        }
        #endregion

        #region *** Nested Types ***
        // Holds the published wording of the controller and every action, one entry per action in method order. The text
        // is a wire contract: clients read it on the Swagger page and in the OpenAPI document.
        private static class Catalog
        {
            public const string Tag = "Utilities for preparing Android devices and resolving their elements through the connected recorder app.";

            // Holds wording reused by the actions the Android surface does not implement yet.
            public static class Common
            {
                public const string NotImplemented = "This capability is not implemented on the Android recorder surface yet.";
            }

            // Wording of GET element.
            public static class GetElementChain
            {
                public const string Summary = "Get the Android element chain";
                public const string Description = "Resolves a node from screen coordinates, focus, or the most recent interaction through the " +
                    "connected recorder app. Any supplied coordinate takes precedence and a missing axis defaults to zero.";

                // Parameter wording, one constant per query parameter.
                public static class Parameters
                {
                    public const string X = "The optional horizontal screen coordinate in device pixels.";
                    public const string Y = "The optional vertical screen coordinate in device pixels.";
                    public const string Focused = "If true and both coordinates are missing, resolve the focused node. " +
                        "Coordinates take precedence when either axis is supplied.";
                }

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string Ok = "Node chain resolved, or an empty chain when no node matched.";
                    public const string ServiceUnavailable = "No unambiguous recorder app is connected.";
                    public const string GatewayTimeout = "The recorder app did not answer before the lookup timeout.";
                }
            }

            // Wording of GET observe.
            public static class GetScreenshot
            {
                public const string Summary = "Capture the device screen";
                public const string Description = "Captures the screen of the device being recorded, or of the only connected device, as a PNG. " +
                    "The size is the display size in device pixels; the origin is always zero.";

                // Parameter wording, one constant per query parameter.
                public static class Parameters
                {
                    public const string MetricsOnly = "When true, skips the pixel capture and returns only the screen size.";
                }

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string Ok = "The screenshot, or only the screen size when metricsOnly is true.";
                    public const string ServiceUnavailable = "adb or the device is unavailable on the recorder host.";
                }
            }

            // Wording of POST point.
            public static class MovePointer
            {
                public const string Summary = "Move the pointer";
                public const string Description = "Not implemented on the Android recorder surface yet - a touch screen has no pointer to move.";

                // Parameter wording, one constant per query parameter.
                public static class Parameters
                {
                    public const string X = "The horizontal screen coordinate to move the pointer to.";
                    public const string Y = "The vertical screen coordinate to move the pointer to.";
                }
            }

            // Wording of POST ground.
            public static class ResolveGroundedElement
            {
                public const string Summary = "Ground a coordinate against the Android tree";
                public const string Description = "Not implemented on the Android recorder surface yet - pointer move and offset resolution have no touch equivalent.";

                // Parameter wording, one constant per query parameter.
                public static class Parameters
                {
                    public const string X = "The horizontal screen coordinate to ground against.";
                    public const string Y = "The vertical screen coordinate to ground against.";
                    public const string SkipOffset = "When true, skips offset computation.";
                }
            }

            // Wording of POST focus.
            public static class SetWindowFocus
            {
                public const string Summary = "Bring a window to the foreground";
                public const string Description = "Not implemented on the Android recorder surface yet - the launcher starts the application instead.";

                // Parameter wording, one constant per query parameter.
                public static class Parameters
                {
                    public const string WindowTitle = "A case-insensitive substring to match against a window title.";
                    public const string ProcessName = "An exact process name to match.";
                }
            }

            // Wording of POST device.
            public static class StartDevice
            {
                public const string Summary = "Start recording on an Android device";
                public const string Description = "Installs the recorder app on the device, maps its hub port with adb reverse, enables its " +
                    "accessibility service and starts the application named by appium:appPackage and appium:appActivity.";

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string Ok = "Recording started. The value is the application's process id, or a negative id when no application was started.";
                    public const string BadRequest = "adb, the device, the recorder app or the application is unavailable on the recorder host.";
                }
            }

            // Wording of DELETE device.
            public static class StopDevice
            {
                public const string Summary = "Stop recording on an Android device";
                public const string Description = "Asks the recorder app to send its pending input and close, stops the application it started, " +
                    "and restores the device's accessibility settings and port mappings.";

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string Ok = "True when the id belonged to a recording this host started; false otherwise.";
                }
            }
        }
        #endregion
    }
}
