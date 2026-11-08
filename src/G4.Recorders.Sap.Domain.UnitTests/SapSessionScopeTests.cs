using Microsoft.VisualStudio.TestTools.UnitTesting;

using G4.Recorders.Sap.Domain.Middlewares;
using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.UnitTests.Support;

namespace G4.Recorders.Sap.Domain.UnitTests
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapSessionScope))]
    [TestCategory("UnitTest")]
    public sealed class SapSessionScopeTests
    {
        [TestMethod(DisplayName = "Verify that each set scope member must match, ignoring case, and unset members match anything")]
        #region *** Data Set ***
        [DataRow(null, null, null, true)]
        [DataRow("sim", null, null, true)]
        [DataRow("S4H", null, null, false)]
        [DataRow("SIM", "001", null, true)]
        [DataRow("SIM", "100", null, false)]
        [DataRow(null, null, "/app/con[0]/ses[0]", true)]
        [DataRow(null, null, "/app/con[0]/ses[1]", false)]
        #endregion
        public void TestInScopeTest(string systemName, string client, string session, bool expected)
        {
            // Arrange: the fake session of system SIM, client 001, and a scope built from the row.
            var tree = new FakeSapTree();
            var scope = new SapScopeParametersModel
            {
                Client = client,
                Sessions = session is null ? [] : [session],
                SystemName = systemName
            };

            // Act: test the session.
            var isInScope = SapSessionScope.TestInScope(tree.Session, scope);

            // Assert: the expected match.
            Assert.AreEqual(expected, isInScope);
        }

        [TestMethod(DisplayName = "Verify that a session outside the scope is reported and never attached")]
        public void SynchronizeOutOfScopeTest()
        {
            // Arrange: subscriptions with a scope for another system.
            var tree = new FakeSapTree();
            var subscribed = false;
            var subscriptions = new SapSessionSubscriptions((_, _) => { subscribed = true; return null; }, _ => { });
            var options = TestOptions.Resolve(new SapRecorderParametersModel { Scope = new SapScopeParametersModel { SystemName = "S4H" } });

            // Act: synchronize.
            subscriptions.Synchronize([tree.Session], options);
            var status = subscriptions.GetStatus();

            // Assert: no subscription, no Record change, and the scope reason.
            Assert.IsFalse(subscribed);
            Assert.IsEmpty(tree.Session.Invocations);
            Assert.AreEqual(SapSessionScope.OutOfScopeReason, status[0].Reason);
        }

        [TestMethod(DisplayName = "Verify that input on a window of a session outside the scope is not recorded")]
        public void ResolveInputOutOfScopeTest()
        {
            // Arrange: the user field focused in the foreground SAP window, and a scope for another client.
            var tree = new FakeSapTree();
            tree.MainWindow.With("GuiFocus", tree.UserField);
            var resolver = new SapInputResolver();
            var record = new SapInputRecord
            {
                ForegroundWindow = FakeSapTree.MainWindowHandle,
                Generation = 1,
                IsKeyboard = true,
                KeyText = "a",
                Message = SapInputRecord.KeyDown,
                VirtualKey = 0x41
            };

            // Act: resolve the key in and out of scope.
            var outOfScope = resolver.Resolve(tree.Application, record, new SapScopeParametersModel { Client = "100" });
            var inScope = resolver.Resolve(tree.Application, record, new SapScopeParametersModel { Client = "001" });

            // Assert: only the in-scope key is recorded.
            Assert.IsNull(outOfScope);
            Assert.IsNotNull(inScope);
        }
    }
}
