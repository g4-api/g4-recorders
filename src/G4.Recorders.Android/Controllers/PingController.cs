using G4.Recorders.Common.Domain.Extensions;
using G4.Recorders.Common.Domain.Models;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System.Net.Mime;

namespace G4.Recorders.Android.Controllers
{
    [ApiController]
    [Route("/api/v4/g4/recorders/android/[controller]")]
    [SwaggerTag(description: Catalog.Tag)]
    public class PingController : ControllerBase
    {
        #region *** Methods      ***
        [HttpGet]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(Summary = Catalog.TestConnection.Summary, Description = Catalog.TestConnection.Description)]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: Catalog.TestConnection.Responses.Ok,
            type: typeof(RecorderPingModel),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult TestConnection() => Ok(ControllerUtilities.NewPingResponse());
        #endregion

        #region *** Nested Types ***
        // Holds the published wording of the controller and its action. The text is a wire contract: clients read it on
        // the Swagger page and in the OpenAPI document.
        private static class Catalog
        {
            public const string Tag = "This controller provides a simple health check endpoint to ensure the service is running and responsive.";

            // Wording of GET ping.
            public static class TestConnection
            {
                public const string Summary = "Health Check Endpoint";
                public const string Description = "Returns recorder-host system details and a heartbeat payload.";

                // Response wording, one constant per status.
                public static class Responses
                {
                    public const string Ok = "Service is active and responding with host metadata.";
                }
            }
        }
        #endregion
    }
}
