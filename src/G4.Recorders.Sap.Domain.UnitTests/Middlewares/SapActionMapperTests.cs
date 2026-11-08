using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

using G4.Recorders.Sap.Domain.Middlewares;
using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.UnitTests.Support;

namespace G4.Recorders.Sap.Domain.UnitTests.Middlewares
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapActionMapper))]
    [TestCategory("UnitTest")]
    public sealed class SapActionMapperTests
    {
        // Lists the events of a logon screen submit: the field text, then the Enter key.
        private static readonly string[] ExpectedSubmitEvents = ["SendKeys", "SendSapVirtualKey"];

        // Lists the arguments of a toolbar button press.
        private static readonly object[] RefreshArguments = ["&REFRESH"];

        [TestMethod(DisplayName = "Verify that a text assignment becomes SendKeys with the Chromium value shape")]
        public void ConvertTextTest()
        {
            // Arrange: the user field's chain.
            var chain = SapChainFactory.NewChain(new FakeSapTree().UserField);

            // Act: map a text assignment.
            var recordingEvent = SapActionMapper.ConvertToEvent(chain, ["SP", "text", "BCUSER"], isPassword: false, timestamp: 5);

            // Assert: the typed-text action with the text and no redaction flag.
            var value = (SapSendKeysValueModel)recordingEvent.Value;
            Assert.AreEqual("Keyboard", recordingEvent.Type);
            Assert.AreEqual("SendKeys", recordingEvent.Event);
            Assert.AreEqual("BCUSER", value.Text);
            Assert.IsNull(value.IsRedacted);
            Assert.AreEqual(5L, recordingEvent.Timestamp);
        }

        [TestMethod(DisplayName = "Verify that password text is redacted as in the Chromium recorder")]
        public void ConvertPasswordTextTest()
        {
            // Arrange: the password field's chain, whose trigger type drives redaction.
            var chain = SapChainFactory.NewChain(new FakeSapTree().PasswordField);

            // Act: map the command array SAP reports for a typed password.
            var recordingEvent = SapActionMapper.ConvertToEvents(chain, new object[] { new object[] { "SP", "text", "secret" } }, timestamp: 5).Single();

            // Assert: the text never leaves the recorder.
            var value = (SapSendKeysValueModel)recordingEvent.Value;
            Assert.AreEqual("***", value.Text);
            Assert.IsTrue(value.IsRedacted);
        }

        [TestMethod(DisplayName = "Verify that press and select become InvokeClick")]
        #region *** Data Set ***
        [DataRow("press")]
        [DataRow("select")]
        #endregion
        public void ConvertClickTest(string method)
        {
            // Arrange: the Enter button's chain.
            var chain = SapChainFactory.NewChain(new FakeSapTree().EnterButton);

            // Act: map the method call.
            var recordingEvent = SapActionMapper.ConvertToEvent(chain, ["M", method], isPassword: false, timestamp: 5);

            // Assert: a click.
            Assert.AreEqual("Mouse", recordingEvent.Type);
            Assert.AreEqual("InvokeClick", recordingEvent.Event);
        }

        [TestMethod(DisplayName = "Verify that sendVKey becomes SendSapVirtualKey with the key number")]
        public void ConvertVirtualKeyTest()
        {
            // Arrange: the main window's chain.
            var chain = SapChainFactory.NewChain(new FakeSapTree().MainWindow);

            // Act: map Enter, SAP virtual key 0.
            var recordingEvent = SapActionMapper.ConvertToEvent(chain, ["M", "sendVKey", 0], isPassword: false, timestamp: 5);

            // Assert: a keyboard action with the key.
            Assert.AreEqual("Keyboard", recordingEvent.Type);
            Assert.AreEqual("SendSapVirtualKey", recordingEvent.Event);
            Assert.AreEqual(0, ((SapVirtualKeyValueModel)recordingEvent.Value).Key);
        }

        [TestMethod(DisplayName = "Verify that other methods become InvokeSapMethod with the driver's call body")]
        public void ConvertShellMethodTest()
        {
            // Arrange: the grid shell's chain.
            var chain = SapChainFactory.NewChain(new FakeSapTree().GridShell);

            // Act: map a toolbar button press.
            var recordingEvent = SapActionMapper.ConvertToEvent(chain, ["M", "pressToolbarButton", "&REFRESH"], isPassword: false, timestamp: 5);

            // Assert: the method and its arguments, ready for sap/element/{e}/call.
            var value = (SapMethodValueModel)recordingEvent.Value;
            Assert.AreEqual("InvokeSapMethod", recordingEvent.Event);
            Assert.AreEqual("pressToolbarButton", value.Method);
            Assert.AreSequenceEqual(RefreshArguments, value.Arguments);
        }

        [TestMethod(DisplayName = "Verify that other properties become SetSapProperty with the driver's property body")]
        public void ConvertShellPropertyTest()
        {
            // Arrange: the grid shell's chain.
            var chain = SapChainFactory.NewChain(new FakeSapTree().GridShell);

            // Act: map a row selection.
            var recordingEvent = SapActionMapper.ConvertToEvent(chain, ["SP", "selectedRows", "2-4"], isPassword: false, timestamp: 5);

            // Assert: the property and its value, ready for sap/element/{e}/property.
            var value = (SapPropertyValueModel)recordingEvent.Value;
            Assert.AreEqual("Form", recordingEvent.Type);
            Assert.AreEqual("SetSapProperty", recordingEvent.Event);
            Assert.AreEqual("selectedRows", value.Name);
            Assert.AreEqual("2-4", value.Value);
        }

        [TestMethod(DisplayName = "Verify that a command array maps in order and skips unknown or malformed commands")]
        public void ConvertCommandArrayTest()
        {
            // Arrange: the field text, an unknown kind, a malformed command, and Enter.
            var chain = SapChainFactory.NewChain(new FakeSapTree().UserField);
            var commandArray = new object[]
            {
                new object[] { "SP", "text", "BCUSER" },
                new object[] { "XX", "unknown" },
                new object[] { "M" },
                new object[] { "M", "sendVKey", 0 }
            };

            // Act: map the array.
            var events = SapActionMapper.ConvertToEvents(chain, commandArray, timestamp: 5);

            // Assert: the two recognized commands, in order.
            Assert.AreSequenceEqual(ExpectedSubmitEvents, events.Select(i => i.Event));
        }

        [TestMethod(DisplayName = "Verify that a single flat command is accepted as an array of one")]
        public void ConvertFlatCommandTest()
        {
            // Arrange: the Enter button's chain.
            var chain = SapChainFactory.NewChain(new FakeSapTree().EnterButton);

            // Act: map a flat command instead of an array of commands.
            var events = SapActionMapper.ConvertToEvents(chain, new object[] { "M", "press" }, timestamp: 5);

            // Assert: one click.
            Assert.HasCount(1, events);
            Assert.AreEqual("InvokeClick", events[0].Event);
        }
    }
}
