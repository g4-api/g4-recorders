using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace G4.Recorders.Common.Domain.Models
{
    /// <summary>
    /// Represents a generic error model used to provide standardized error responses.
    /// </summary>
    /// <param name="httpContext">The HTTP context of the current request.</param>
    public class GenericErrorModel(HttpContext httpContext) : ProblemDetails
    {
        #region *** Constructors ***
        /// <summary>
        /// Initializes a new instance of the <see cref="GenericErrorModel"/> class with default values.
        /// </summary>
        public GenericErrorModel()
            : this(httpContext: default)
        {
            // Default constructor calls the parameterized constructor with a null HttpContext.
        }
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets the collection of validation errors associated with the error response.
        /// </summary>
        public Dictionary<string, string[]> Errors { get; set; } = [];

        /// <summary>
        /// Gets the request information associated with the error.
        /// </summary>
        public object Request { get; set; } = httpContext?.Request.Method == "GET"
            ? $"{httpContext.Request.Method} {httpContext.Request.Path} {httpContext.Request.Protocol}"
            : default;

        /// <summary>
        /// Gets the route data associated with the error.
        /// </summary>
        public Dictionary<string, object> RouteData { get; } = [];

        /// <summary>
        /// Gets the unique identifier for tracing the request.
        /// </summary>
        public virtual string TraceId { get; } = httpContext?.TraceIdentifier ?? DateTime.Now.ToString("yyyyMMdd-HHmmss-fffffff");
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Adds an error message to the error model.
        /// </summary>
        /// <param name="name">The name of the field or property associated with the error.</param>
        /// <param name="value">The error message(s) associated with the field.</param>
        /// <returns>The current instance of <see cref="GenericErrorModel"/>.</returns>
        public GenericErrorModel AddError(string name, params string[] value)
        {
            Errors[name] = value;
            return this;
        }

        /// <summary>
        /// Adds multiple validation errors to the error model.
        /// </summary>
        /// <param name="errors">A dictionary of field names and their corresponding error messages.</param>
        /// <returns>The current instance of <see cref="GenericErrorModel"/>.</returns>
        public GenericErrorModel AddErrors(IDictionary<string, string[]> errors)
        {
            foreach (var error in errors)
            {
                Errors[error.Key] = error.Value;
            }
            return this;
        }

        /// <summary>
        /// Adds validation errors to the <see cref="Errors"/> dictionary.
        /// </summary>
        /// <param name="validationResults">A list of <see cref="ValidationResult"/> instances containing validation errors.</param>
        /// <returns>The current instance of <see cref="GenericErrorModel"/> for method chaining.</returns>
        public GenericErrorModel AddErrors(List<ValidationResult> validationResults)
        {
            foreach (var validationResult in validationResults)
            {
                if (!validationResult.MemberNames.Any())
                {
                    Errors["$"] = [.. Errors["$"], validationResult.ErrorMessage];
                }

                foreach (var memberName in validationResult.MemberNames)
                {
                    Errors[memberName] = [validationResult.ErrorMessage];
                }
            }
            return this;
        }

        /// <summary>
        /// Adds route data to the error model.
        /// </summary>
        /// <param name="routeData">A dictionary containing route data.</param>
        /// <returns>The current instance of <see cref="GenericErrorModel"/>.</returns>
        public GenericErrorModel AddRouteData(IDictionary<string, object> routeData)
        {
            foreach (var data in routeData)
            {
                RouteData[data.Key] = data.Value;
            }
            return this;
        }

        /// <summary>
        /// Sets the request object associated with the error.
        /// </summary>
        /// <param name="request">The request object.</param>
        /// <returns>The current instance of <see cref="GenericErrorModel"/>.</returns>
        public GenericErrorModel SetRequest(object request)
        {
            static object FormatRequest(object request)
            {
                return request;
            }

            Request = FormatRequest(request);
            return this;
        }
        #endregion
    }
}
