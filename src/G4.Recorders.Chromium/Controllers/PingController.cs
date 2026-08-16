using G4.Recorders.Common.Domain.Extensions;
using G4.Recorders.Common.Domain.Models;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Swashbuckle.AspNetCore.Annotations;

using System.Net.Mime;

namespace G4.Recorders.Chromium.Controllers
{
    [ApiController]
    [Route("/api/v4/g4/recorders/chromium/[controller]")]
    [SwaggerTag(description: "This controller provides a simple health check endpoint to ensure the service is running and responsive.")]
    public class PingController : ControllerBase
    {
        [HttpGet]
        #region *** OpenApi Documentation ***
        [SwaggerOperation(
            Summary = "Health Check Endpoint",
            Description = "Returns recorder-host system details and a heartbeat payload.")]
        [SwaggerResponse(StatusCodes.Status200OK,
            description: "Service is active and responding with host metadata.",
            type: typeof(RecorderPingModel),
            contentTypes: MediaTypeNames.Application.Json)]
        #endregion
        public IActionResult TestConnection() => Ok(ControllerUtilities.NewPingResponse());
    }
}
