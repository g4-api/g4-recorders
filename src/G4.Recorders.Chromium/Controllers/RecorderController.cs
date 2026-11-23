using G4.Recorders.Chromium.Domain;
using G4.Recorders.Chromium.Domain.Models;

using G4.Recorders.Common.Domain.Models;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System;
using System.IO;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Chromium.Controllers
{
    [ApiController]
    [Route("/api/v4/g4/recorders/chromium/[controller]")]
    [SwaggerTag(description: Catalog.Tag)]
    public class RecorderController(
        IChromiumRecorderRepository repository,
        IChromiumRecorderLauncher launcher) : ControllerBase
    {
        #region *** Constants    ***
        // Selects elements with a CSS selector; the exact casing is the G4 strategy name sent to the extension.
        private const string CssSelectorStrategy = "CssSelector";

        // Selects anchors by their complete visible text; the casing is the G4 strategy name sent to the extension.
        private const string LinkTextStrategy = "LinkText";

        // Selects anchors by a fragment of their visible text; the casing is the G4 strategy name sent to the extension.
        private const string PartialLinkTextStrategy = "PartialLinkText";

        // Selects elements by tag name; the exact casing is the G4 strategy name sent to the extension.
        private const string TagNameStrategy = "TagName";

        // Selects elements with an XPath expression; also the strategy a locator test uses when the caller names none.
        private const string XpathStrategy = "Xpath";

        // Lists the five W3C WebDriver strategies a locator test accepts, each in the casing the extension expects.
        private static readonly string[] LocatorStrategies =
        [
            XpathStrategy,
            CssSelectorStrategy,
            LinkTextStrategy,
            PartialLinkTextStrategy,
            TagNameStrategy
        ];
        #endregion

        #region *** Methods      ***
        [HttpGet("element")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.GetElementChain.Summary, Description = Catalog.GetElementChain.Description)]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: Catalog.GetElementChain.Responses.Ok,
            type: typeof(ChromiumChainModel),
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
            // Normalize caller intent before crossing the asynchronous browser transport boundary.
            var request = RecorderPeekRequestResolver.Resolve(x, y, focused);

            try
            {
                // Wait for the connected extension to resolve the selected DOM target and return its chain.
                var chain = await repository.GetElementChainAsync(request, cancellationToken).ConfigureAwait(false);

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

        [HttpGet("observe")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.GetScreenshot.Summary, Description = Catalog.GetScreenshot.Description)]
        [SwaggerResponse(StatusCodes.Status501NotImplemented,
            description: Catalog.GetScreenshot.Responses.NotImplemented,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult GetScreenshot(
            [FromQuery(Name = "metricsOnly")]
            [SwaggerParameter(description: Catalog.GetScreenshot.Parameters.MetricsOnly)]
            bool metricsOnly)
        {
            try
            {
                // Ask the repository for the capture so this surface answers through the shared recorder contract.
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
        [SwaggerOperation(Summary = Catalog.MovePointer.Summary, Description = Catalog.MovePointer.Description)]
        [SwaggerResponse(StatusCodes.Status501NotImplemented,
            description: Catalog.MovePointer.Responses.NotImplemented,
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
                // Ask the repository to move the pointer so this surface answers through the shared recorder contract.
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

        [HttpPost("ground")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.ResolveGroundedElement.Summary, Description = Catalog.ResolveGroundedElement.Description)]
        [SwaggerResponse(StatusCodes.Status501NotImplemented,
            description: Catalog.ResolveGroundedElement.Responses.NotImplemented,
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
                // Ask the repository to ground the coordinate so this surface answers through the shared contract.
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
        [SwaggerOperation(Summary = Catalog.SetWindowFocus.Summary, Description = Catalog.SetWindowFocus.Description)]
        [SwaggerResponse(StatusCodes.Status501NotImplemented,
            description: Catalog.SetWindowFocus.Responses.NotImplemented,
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
                // Ask the repository to focus the window so this surface answers through the shared recorder contract.
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

        [HttpPost("browser")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.StartBrowser.Summary, Description = Catalog.StartBrowser.Description)]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: Catalog.StartBrowser.Responses.Ok,
            type: typeof(int),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status400BadRequest,
            description: Catalog.StartBrowser.Responses.BadRequest,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult StartBrowser([FromBody] DriverParametersModel driverParameters)
        {
            try
            {
                // Delegate the operating-system interaction to the recorder-owned launcher so HTTP clients stay remote-safe.
                var processId = launcher.Start(driverParameters);

                return Ok(processId);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
            {
                // Report recorder-host readiness failures as caller-visible input/environment problems.
                return Problem(
                    detail: exception.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        [HttpDelete("browser/{processId:int}")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.StopBrowser.Summary, Description = Catalog.StopBrowser.Description)]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: Catalog.StopBrowser.Responses.Ok,
            type: typeof(bool),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public async Task<IActionResult> StopBrowserAsync(int processId)
        {
            // Keep browser shutdown on the recorder machine so remote clients never manipulate an operating-system process.
            var isStopped = await launcher.StopAsync(processId).ConfigureAwait(false);

            return Ok(isStopped);
        }

        [HttpPost("locator")]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.TestLocator.Summary, Description = Catalog.TestLocator.Description)]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: Catalog.TestLocator.Responses.Ok,
            type: typeof(ChromiumLocatorTestResultModel),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status400BadRequest,
            description: Catalog.TestLocator.Responses.BadRequest,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status503ServiceUnavailable,
            description: Catalog.TestLocator.Responses.ServiceUnavailable,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status504GatewayTimeout,
            description: Catalog.TestLocator.Responses.GatewayTimeout,
            type: typeof(ProblemDetails),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public async Task<IActionResult> TestLocatorAsync(
            [FromBody]
            [SwaggerRequestBody(description: Catalog.TestLocator.Parameters.Locator)]
            ChromiumLocatorTestModel locator,

            CancellationToken cancellationToken)
        {
            // Reject a missing expression before crossing the browser transport, because no strategy can evaluate it.
            if (string.IsNullOrWhiteSpace(locator?.Value))
            {
                return Problem(
                    detail: "The locator value is required and cannot be blank.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // Normalize the optional strategy to its exact G4 casing so the extension receives one known spelling.
            var strategy = ResolveLocatorStrategy(@using: locator.Using);

            if (strategy == null)
            {
                // Name the rejected strategy and the accepted ones so the caller can correct the request.
                var detail = $"The locator strategy '{locator.Using}' is not supported. " +
                    $"Supported strategies: {string.Join(", ", LocatorStrategies)}.";

                return Problem(detail, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                // Wait for the connected extension to evaluate the locator in every frame and summarize its matches.
                var result = await repository
                    .TestLocatorAsync(@using: strategy, value: locator.Value, cancellationToken)
                    .ConfigureAwait(false);

                return Ok(result);
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

        // Resolves a caller-supplied locator strategy to its exact G4 casing before a locator test is sent.
        // A null or blank strategy selects XPath, a known strategy is matched without regard to casing, and an
        // unknown strategy returns null so the action can answer with a 400 problem. The helper reads only the
        // immutable strategy list and has no side effects.
        private static string ResolveLocatorStrategy(string @using)
        {
            // Select the documented default when the caller omitted the optional strategy.
            if (string.IsNullOrWhiteSpace(@using))
            {
                return XpathStrategy;
            }

            // Match without regard to casing and return the canonical spelling; null reports an unknown strategy.
            return Array.Find(
                array: LocatorStrategies,
                match: strategy => string.Equals(strategy, @using, StringComparison.OrdinalIgnoreCase));
        }
        #endregion

        #region *** Nested Types ***
        // Holds the published wording of the controller and every action, one entry per action in method order. The text
        // is a wire contract: clients read it on the Swagger page and in the OpenAPI document.
        private static class Catalog
        {
            public const string Tag = "Utilities for resolving Chromium DOM elements through the connected recorder extension.";

            // Holds wording reused by the actions the Chromium surface does not implement yet; entries alias these
            // constants instead of copying the text.
            private static class Common
            {
                public const string NotImplemented = "This capability is not implemented on the Chromium recorder surface yet.";
            }

            // Wording of GET element.
            public static class GetElementChain
            {
                public const string Summary = "Get the Chromium DOM element chain";
                public const string Description = "Resolves a DOM element from viewport coordinates, focus, or the current pointer position. " +
                    "Any supplied coordinate takes precedence and a missing coordinate axis defaults to zero.";

                // Parameter wording, one constant per query parameter.
                public static class Parameters
                {
                    public const string X = "The optional X coordinate relative to the active Chromium frame viewport.";
                    public const string Y = "The optional Y coordinate relative to the active Chromium frame viewport.";
                    public const string Focused = "If true and both coordinates are missing, resolve the focused DOM element. " +
                        "Coordinates take precedence when either axis is supplied.";
                }

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string Ok = "DOM ancestor chain successfully resolved, or an empty chain when no element matched.";
                    public const string ServiceUnavailable = "No unambiguous recorder extension is connected.";
                    public const string GatewayTimeout = "The recorder extension did not answer before the lookup timeout.";
                }
            }

            // Wording of GET observe.
            public static class GetScreenshot
            {
                public const string Summary = "Capture the virtual desktop";
                public const string Description = "Not implemented on the Chromium recorder surface yet - this host targets plain " +
                    "net10.0 and is not guaranteed to run on a machine with a real desktop to capture.";

                // Parameter wording, one constant per query parameter.
                public static class Parameters
                {
                    public const string MetricsOnly = "When true, skips the pixel capture and returns only virtual-desktop bounds.";
                }

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string NotImplemented = Common.NotImplemented;
                }
            }

            // Wording of POST point.
            public static class MovePointer
            {
                public const string Summary = "Move the pointer";
                public const string Description = "Not implemented on the Chromium recorder surface yet - the extension surface has no " +
                    "physical desktop cursor to move.";

                // Parameter wording, one constant per query parameter.
                public static class Parameters
                {
                    public const string X = "The horizontal viewport coordinate to move the pointer to.";
                    public const string Y = "The vertical viewport coordinate to move the pointer to.";
                }

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string NotImplemented = Common.NotImplemented;
                }
            }

            // Wording of POST ground.
            public static class ResolveGroundedElement
            {
                public const string Summary = "Ground a coordinate against the Chromium DOM";
                public const string Description = "Not implemented on the Chromium recorder surface yet - pointer-move and offset " +
                    "resolution await this surface's own physical-screen mapping.";

                // Parameter wording, one constant per query parameter.
                public static class Parameters
                {
                    public const string X = "The horizontal viewport coordinate to ground against.";
                    public const string Y = "The vertical viewport coordinate to ground against.";
                    public const string SkipOffset = "When true, skips offset computation and leaves the returned chain's offset null.";
                }

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string NotImplemented = Common.NotImplemented;
                }
            }

            // Wording of POST focus.
            public static class SetWindowFocus
            {
                public const string Summary = "Bring a window to the foreground";
                public const string Description = "Not implemented on the Chromium recorder surface yet - the extension surface has no " +
                    "equivalent of an OS-level window handle to focus.";

                // Parameter wording, one constant per query parameter.
                public static class Parameters
                {
                    public const string WindowTitle = "A case-insensitive substring to match against a running process's window title.";
                    public const string ProcessName = "An exact process name to match. Combined with windowTitle as an AND when both are supplied.";
                }

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string NotImplemented = Common.NotImplemented;
                }
            }

            // Wording of POST browser.
            public static class StartBrowser
            {
                public const string Summary = "Start a Chromium recorder browser";
                public const string Description = "Starts Chromium on the recorder host with the bundled recorder extension. When the " +
                    "request omits the binary path, the recorder resolves Chrome from its own G4 sandbox layout.";

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string Ok = "Browser process started on the recorder host.";
                    public const string BadRequest = "The browser binary or recorder extension is unavailable on the recorder host.";
                }
            }

            // Wording of DELETE browser.
            public static class StopBrowser
            {
                public const string Summary = "Stop a Chromium recorder browser";
                public const string Description = "Stops a browser previously started by this recorder host, using the recorder extension " +
                    "for graceful close before the recorder-owned process fallback.";

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string Ok = "The recorder accepted the browser stop request.";
                }
            }

            // Wording of POST locator.
            public static class TestLocator
            {
                public const string Summary = "Test a locator against the live browser";
                public const string Description = "Evaluates a locator in every frame of the page through the connected " +
                    "recorder extension and returns the total match count with the first matched elements. The strategy is " +
                    "optional, defaults to Xpath and is matched without regard to casing: Xpath, CssSelector, LinkText, " +
                    "PartialLinkText or TagName.";

                // Parameter wording, one constant per action parameter.
                public static class Parameters
                {
                    public const string Locator = "The locator to test: the optional strategy in 'using' and the required " +
                        "expression in 'value'.";
                }

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string Ok = "The locator was evaluated. The count is zero and the matches are empty when " +
                        "no element matched.";
                    public const string BadRequest = "The locator value is missing or blank, or the locator strategy is not supported.";
                    public const string ServiceUnavailable = "No unambiguous recorder extension is connected, or the extension " +
                        "failed to evaluate the locator.";
                    public const string GatewayTimeout = "The recorder extension did not answer before the locator test timeout.";
                }
            }
        }
        #endregion
    }
}
