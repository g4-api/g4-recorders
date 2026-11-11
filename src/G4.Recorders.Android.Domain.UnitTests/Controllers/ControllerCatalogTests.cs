using G4.Recorders.Android.Controllers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Swashbuckle.AspNetCore.Annotations;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace G4.Recorders.Android.Domain.UnitTests.Controllers
{
    [TestClass]
    [TestCategory("ControllerCatalog")]
    [TestCategory("UnitTest")]
    public sealed class ControllerCatalogTests
    {
        // Every member a Swagger attribute can sit on: public instance methods declared by the controller itself.
        private const BindingFlags ActionFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        [TestMethod(DisplayName = "Verify that every Swagger text of PingController comes from its catalog and every catalog text is used")]
        public void PingControllerCatalogTest()
        {
            // Act and Assert: the published text and the catalog are the same set.
            AssertCatalog(typeof(PingController));
        }

        [TestMethod(DisplayName = "Verify that every Swagger text of RecorderController comes from its catalog and every catalog text is used")]
        public void RecorderControllerCatalogTest()
        {
            // Act and Assert: the published text and the catalog are the same set.
            AssertCatalog(typeof(RecorderController));
        }

        // Compares the text a controller publishes through its Swagger attributes with the constants of its private
        // nested Catalog. A literal in an attribute shows up as published text without a constant, and a stale
        // constant shows up as a constant nothing publishes. Reads attributes only; owns no state.
        private static void AssertCatalog(Type controller)
        {
            // Arrange: read every constant of the catalog and its nested entry classes.
            var catalog = controller.GetNestedType("Catalog", BindingFlags.NonPublic);
            Assert.IsNotNull(catalog, $"{controller.Name} has no nested Catalog.");
            var constants = GetConstants(catalog).ToHashSet(StringComparer.Ordinal);

            // Act: read every published text from the class, its actions and their parameters.
            var actions = controller.GetMethods(ActionFlags);
            var parameters = actions.SelectMany(action => action.GetParameters());
            var published = new HashSet<string>(StringComparer.Ordinal);
            AddTexts(published, controller.GetCustomAttributes(inherit: false));

            foreach (var action in actions)
            {
                AddTexts(published, action.GetCustomAttributes(inherit: false));
            }

            foreach (var parameter in parameters)
            {
                AddTexts(published, parameter.GetCustomAttributes(inherit: false));
            }

            // Assert: no literal bypasses the catalog, and no catalog text is left unused.
            var literals = published.Except(constants).ToArray();
            var unused = constants.Except(published).ToArray();
            Assert.IsEmpty(literals, $"Swagger text not in the catalog: {string.Join(" | ", literals)}");
            Assert.IsEmpty(unused, $"Catalog text no attribute publishes: {string.Join(" | ", unused)}");
        }

        // Adds the client-facing text of every Swagger attribute in a list; other attributes are ignored.
        private static void AddTexts(HashSet<string> texts, object[] attributes)
        {
            // Collect each attribute kind's text members; an unset member adds nothing.
            foreach (var attribute in attributes)
            {
                string[] values = attribute switch
                {
                    SwaggerTagAttribute tag => [tag.Description],
                    SwaggerOperationAttribute operation => [operation.Summary, operation.Description],
                    SwaggerResponseAttribute response => [response.Description],
                    SwaggerParameterAttribute parameter => [parameter.Description],
                    _ => []
                };

                texts.UnionWith(values.Where(value => !string.IsNullOrEmpty(value)));
            }
        }

        // Returns the values of every string constant in a catalog class and its nested classes, recursively.
        private static IEnumerable<string> GetConstants(Type catalog)
        {
            // Read this class's literals, including the private Common class's.
            const BindingFlags constantFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var values = catalog
                .GetFields(constantFlags)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue());

            // Then every nested entry class's.
            var nested = catalog
                .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(GetConstants);

            return values.Concat(nested);
        }
    }
}
