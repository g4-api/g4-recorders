using G4.Extensions;

using Microsoft.AspNetCore.Mvc.Formatters;

using System.Net.Mime;
using System.Threading.Tasks;

namespace G4.Recorders.Common.Domain.Formatters
{
    /// <summary>
    /// An input formatter that handles plain text content types.
    /// </summary>
    public class PlainTextInputFormatter : InputFormatter
    {
        #region *** Constants    ***
        private const string ContentType = MediaTypeNames.Text.Plain;
        #endregion

        #region *** Constructors ***
        /// <summary>
        /// Initializes a new instance of the <see cref="PlainTextInputFormatter"/> class.
        /// </summary>
        public PlainTextInputFormatter()
        {
            SupportedMediaTypes.Add(ContentType);
        }
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Asynchronously reads an object from the request body.
        /// </summary>
        /// <param name="context">The context for input formatter which contains the HTTP context and model metadata.</param>
        /// <returns>A task that, when completed, returns an <see cref="InputFormatterResult"/> representing the deserialized request body.</returns>
        public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
        {
            var request = await context.HttpContext.Request.ReadAsync().ConfigureAwait(false);
            return await InputFormatterResult.SuccessAsync(request).ConfigureAwait(false);
        }

        /// <summary>
        /// Determines whether the input formatter can read the request body based on the content type.
        /// </summary>
        /// <param name="context">The context for input formatter which contains the HTTP context and model metadata.</param>
        /// <returns><c>true</c> if the content type is supported; otherwise, <c>false</c>.</returns>
        public override bool CanRead(InputFormatterContext context)
        {
            var contentType = context.HttpContext.Request.ContentType;
            return contentType?.StartsWith(ContentType) == true;
        }
        #endregion
    }
}
