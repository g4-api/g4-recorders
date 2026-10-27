using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using G4.Recorders.Sap.Domain.Hubs;
using G4.Recorders.Sap.Domain.Middlewares;
using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.Scripting;
using G4.Recorders.Sap.Domain.UnitTests.Support;

namespace G4.Recorders.Sap.Domain.UnitTests
{
    [TestClass]
    [DoNotParallelize]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory("EndToEnd")]
    [TestCategory("SapSimulator")]
    public sealed partial class SapSimulatorEndToEndTests
    {
        // Identifies the simulator's logon user field.
        private const string UserFieldId = "/app/con[0]/ses[0]/wnd[0]/usr/txtRSYST-BNAME";

        // Identifies the simulator's logon password field.
        private const string PasswordFieldId = "/app/con[0]/ses[0]/wnd[0]/usr/pwdRSYST-BCODE";

        // Identifies the simulator's main window.
        private const string MainWindowId = "/app/con[0]/ses[0]/wnd[0]";

        // Identifies the simulator's only session.
        private const string SessionId = "/app/con[0]/ses[0]";

        // Bounds every wait for the recorder or the simulator.
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

        // Lists the SAP actions of a logon: the user, the redacted password, and Enter.
        private static readonly string[] ExpectedLogonEvents = ["SendKeys", "SendKeys", "SendSapVirtualKey"];

        // Lists the user32 events of one left click.
        private static readonly string[] ExpectedClickEvents = ["Left Down", "Left Up"];

        [TestMethod(DisplayName = "Verify that the recorder peeks the simulator's controls by point and by focus")]
        public void PeekTest()
        {
            // Arrange: the simulator and a scripting host; focus the user field and read its position.
            using var simulator = SimulatorProcess.Start();
            using var host = new SapScriptingHost();
            var (x, y) = host.Invoke(application =>
            {
                var field = (ISapGuiObject)application.InvokeMethod("findById", UserFieldId, false);
                field.InvokeMethod("setFocus");

                return ((int)SapGuiNavigator.GetLong(field, "ScreenLeft") + 5, (int)SapGuiNavigator.GetLong(field, "ScreenTop") + 5);
            });
            var repository = new SapRecorderRepository(host, new SapCursorPositionProvider());

            // Act: peek by point and by focus.
            var byPoint = repository.GetElementChain(x, y);
            var byFocus = repository.GetElementChain();

            // Assert: both resolve the user field with the shared locator form.
            var expectedLocator = $"//GuiTextField[@id='{UserFieldId}']";
            Assert.AreEqual(expectedLocator, byPoint.Locator);
            Assert.AreEqual(expectedLocator, byFocus.Locator);
            Assert.AreEqual("GuiMainWindow", byPoint.TopWindow.ControlType);
            Assert.AreNotEqual(0, byPoint.Path[^1].ProcessId);
        }

        [TestMethod(DisplayName = "Verify that a logon is recorded as nativeSap actions and recording mode is restored afterwards")]
        public async Task NativeSapLogonTest()
        {
            // Arrange: the simulator, a host, and the action capture with a collecting broadcaster.
            using var simulator = SimulatorProcess.Start();
            using var host = new SapScriptingHost();
            var broadcaster = new CollectingBroadcaster();
            var statusStore = new SapRecordingStatusStore();
            using var service = new SapActionCaptureService(host, broadcaster, statusStore, NewOptionsStore(), NullLogger<SapActionCaptureService>.Instance);

            RecorderConnectionState.Instance.AddConnection();
            try
            {
                await service.StartAsync(CancellationToken.None);
                WaitFor(() => statusStore.GetSessions().Any(i => i.IsRecording), "the session to start recording");

                // Act: type the user and the password through scripting, then press Enter.
                host.Invoke(application =>
                {
                    ((ISapGuiObject)application.InvokeMethod("findById", UserFieldId, false)).SetProperty("Text", "BCUSER");
                    ((ISapGuiObject)application.InvokeMethod("findById", PasswordFieldId, false)).SetProperty("Text", "secret");
                    ((ISapGuiObject)application.InvokeMethod("findById", MainWindowId, false)).InvokeMethod("sendVKey", 0);

                    return true;
                });
                WaitFor(() => broadcaster.Messages.Count >= 3, "three nativeSap messages");
            }
            finally
            {
                RecorderConnectionState.Instance.RemoveConnection();
            }

            // Assert: the field texts, in change order and redacted for the password, then Enter on the window.
            var events = broadcaster.Messages.Select(i => i.NativeSap).ToList();
            Assert.IsTrue(broadcaster.Messages.All(i => i.User32 is null));
            Assert.AreSequenceEqual(ExpectedLogonEvents, events.Select(i => i.Event));
            Assert.AreEqual("BCUSER", ((SapSendKeysValueModel)events[0].Value).Text);
            Assert.AreEqual($"//GuiTextField[@id='{UserFieldId}']", events[0].Chain.Locator);
            Assert.AreEqual("***", ((SapSendKeysValueModel)events[1].Value).Text);
            Assert.AreEqual(0, ((SapVirtualKeyValueModel)events[2].Value).Key);

            // Assert: once no client listens, the session leaves recording mode.
            WaitFor(() => !host.Invoke(application => SapGuiNavigator.GetBoolean(GetSession(application), "Record")), "recording mode to be restored");
            await service.StopAsync(CancellationToken.None);
        }

        [TestMethod(DisplayName = "Verify that a system that disables recording is reported and leaves the session alone")]
        public async Task NativeSapRecordingDisabledTest()
        {
            // Arrange: a simulator whose sessions refuse recording mode.
            using var simulator = SimulatorProcess.Start("--recording-disabled");
            using var host = new SapScriptingHost();
            var statusStore = new SapRecordingStatusStore();
            using var service = new SapActionCaptureService(host, new CollectingBroadcaster(), statusStore, NewOptionsStore(), NullLogger<SapActionCaptureService>.Instance);

            RecorderConnectionState.Instance.AddConnection();
            try
            {
                // Act: let the service try to attach.
                await service.StartAsync(CancellationToken.None);
                WaitFor(() => statusStore.GetSessions().Count > 0, "the session status");
            }
            finally
            {
                RecorderConnectionState.Instance.RemoveConnection();
            }

            // Assert: the session is reported with the reason, and was not left recording.
            var status = statusStore.GetSessions().Single();
            Assert.AreEqual(SessionId, status.Id);
            Assert.IsFalse(status.IsRecording);
            Assert.Contains("user_scripting_disable_recording", status.Reason);
            Assert.IsFalse(host.Invoke(application => SapGuiNavigator.GetBoolean(GetSession(application), "Record")));
            await service.StopAsync(CancellationToken.None);
        }

        [TestMethod(DisplayName = "Verify that a session scope attaches only the listed session and reports the other one")]
        public async Task NativeSapScopeTest()
        {
            // Arrange: two sessions, and values that scope recording to the second one.
            using var simulator = SimulatorProcess.Start("--sessions", "2");
            using var host = new SapScriptingHost();
            var statusStore = new SapRecordingStatusStore();
            var optionsStore = NewOptionsStore();
            var scope = new SapScopeParametersModel { Sessions = ["/app/con[0]/ses[1]"] };
            optionsStore.Apply(TestOptions.Resolve(new SapRecorderParametersModel { Scope = scope }));
            using var service = new SapActionCaptureService(host, new CollectingBroadcaster(), statusStore, optionsStore, NullLogger<SapActionCaptureService>.Instance);

            RecorderConnectionState.Instance.AddConnection();
            try
            {
                // Act: let the service attach.
                await service.StartAsync(CancellationToken.None);
                WaitFor(() => statusStore.GetSessions().Count == 2, "both session statuses");
            }
            finally
            {
                RecorderConnectionState.Instance.RemoveConnection();
            }

            // Assert: ses[1] records; ses[0] is out of scope and keeps recording mode off.
            var status = statusStore.GetSessions();
            Assert.IsTrue(status.Single(i => i.Id == "/app/con[0]/ses[1]").IsRecording);
            Assert.AreEqual(SapSessionScope.OutOfScopeReason, status.Single(i => i.Id == SessionId).Reason);
            Assert.IsFalse(host.Invoke(application => SapGuiNavigator.GetBoolean(SapGuiNavigator.GetSessions(application)[0], "Record")));
            await service.StopAsync(CancellationToken.None);
        }

        [TestMethod(DisplayName = "Verify that the launcher starts SAP Logon from a path, attaches to it afterwards, and stops it")]
        public void LaunchSapLogonTest()
        {
            // Arrange: the simulator stands in for SAP Logon; nothing is running yet.
            var path = SimulatorProcess.AssertAvailable();
            using var host = new SapScriptingHost();
            var launcher = new SapLogonLauncher(host);
            var logon = new SapLogonParametersModel { Arguments = ["--quiet"], Path = path };

            // Act: start, start again, then stop.
            var (processId, isLaunched) = launcher.Start(logon);
            var (secondProcessId, isSecondLaunched) = launcher.Start(logon);
            var sessionCount = host.Invoke(application => SapGuiNavigator.GetSessions(application).Count);
            var isStopped = launcher.Stop(processId);

            // Assert: launched once and reachable, then attached without a second start, then stopped.
            Assert.IsTrue(isLaunched);
            Assert.IsGreaterThan(0, processId);
            Assert.AreEqual(1, sessionCount);
            Assert.IsFalse(isSecondLaunched);
            Assert.AreEqual(0, secondProcessId);
            Assert.IsTrue(isStopped);
        }

        [TestMethod(DisplayName = "Verify that a real click on a simulator field is recorded as user32 Down and Up with the field's chain")]
        public async Task User32ClickTest()
        {
            // Arrange: opt in to real input, start the simulator, and find the user field's center.
            SimulatorProcess.AssertEnabled(SimulatorProcess.InputVariable);
            using var simulator = SimulatorProcess.Start();
            using var host = new SapScriptingHost();
            var (x, y, windowHandle) = host.Invoke(application =>
            {
                var field = (ISapGuiObject)application.InvokeMethod("findById", UserFieldId, false);
                var window = (ISapGuiObject)application.InvokeMethod("findById", MainWindowId, false);
                var left = (int)SapGuiNavigator.GetLong(field, "ScreenLeft");
                var top = (int)SapGuiNavigator.GetLong(field, "ScreenTop");

                return (left + 10, top + 5, SapGuiNavigator.GetLong(window, "Handle"));
            });

            // Click only when the simulator is the window at the point, so the click never lands elsewhere.
            if (SapDesktop.GetRootWindow(x, y) != windowHandle)
            {
                Assert.Inconclusive("Another window covers the simulator; the click test needs it on top.");
            }

            var broadcaster = new CollectingBroadcaster();
            using var service = new SapInputCaptureService(host, broadcaster, NewOptionsStore(), NullLogger<SapInputCaptureService>.Instance);

            RecorderConnectionState.Instance.AddConnection();
            try
            {
                await service.StartAsync(CancellationToken.None);
                Thread.Sleep(millisecondsTimeout: 500);

                // Act: click the field, then put the cursor back.
                TestInput.Click(x, y);
                WaitFor(() => broadcaster.Messages.Count >= 2, "two user32 messages");
            }
            finally
            {
                RecorderConnectionState.Instance.RemoveConnection();
                await service.StopAsync(CancellationToken.None);
            }

            // Assert: Down and Up on the user field, with the offset inside it.
            var events = broadcaster.Messages.Select(i => i.User32).ToList();
            Assert.AreSequenceEqual(ExpectedClickEvents, events.Select(i => i.Event));
            Assert.AreEqual($"//GuiTextField[@id='{UserFieldId}']", events[0].Chain.Locator);
            Assert.AreEqual(10, events[1].Chain.MouseOffset.X);
        }

        // Creates an options store with the default values: both streams, record-mode permission, no scope.
        private static SapRecorderOptionsStore NewOptionsStore()
        {
            return new SapRecorderOptionsStore(TestOptions.NewResolver());
        }

        // Gets the simulator's only session.
        private static ISapGuiObject GetSession(ISapGuiObject application)
        {
            return SapGuiNavigator.GetSessions(application).Single();
        }

        // Polls a condition until it holds, failing the test with a description after the timeout.
        private static void WaitFor(Func<bool> condition, string description)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                if (stopwatch.Elapsed > WaitTimeout)
                {
                    Assert.Fail($"Timed out waiting for {description}.");
                }

                Thread.Sleep(millisecondsTimeout: 100);
            }
        }

        // Collects broadcast messages instead of sending them to hub clients.
        private sealed class CollectingBroadcaster : ISapRecordingBroadcaster
        {
            // Gets the messages in broadcast order.
            internal ConcurrentQueue<SapRecordingMessageModel> Messages { get; } = new();

            // Stores one message.
            public Task SendAsync(SapRecordingMessageModel message)
            {
                Messages.Enqueue(message);
                return Task.CompletedTask;
            }
        }

        // Sends a real left click through Windows, restoring the cursor afterwards.
        private static partial class TestInput
        {
            // Presses the left button.
            private const uint LeftDown = 0x0002;

            // Releases the left button.
            private const uint LeftUp = 0x0004;

            // Clicks at a physical screen point and puts the cursor back where it was.
            internal static void Click(int x, int y)
            {
                GetCursorPos(out var original);
                SetCursorPos(x, y);
                mouse_event(LeftDown, 0, 0, 0, IntPtr.Zero);
                mouse_event(LeftUp, 0, 0, 0, IntPtr.Zero);
                Thread.Sleep(millisecondsTimeout: 200);
                SetCursorPos(original.X, original.Y);
            }

            // Reads the cursor position.
            [LibraryImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static partial bool GetCursorPos(out Point point);

            // Synthesizes a mouse button event at the cursor.
            [LibraryImport("user32.dll")]
            private static partial void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extraInfo);

            // Moves the cursor.
            [LibraryImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static partial bool SetCursorPos(int x, int y);

            // Mirrors the Win32 POINT structure.
            [StructLayout(LayoutKind.Sequential)]
            private struct Point
            {
                // Gets the horizontal coordinate.
                public int X;

                // Gets the vertical coordinate.
                public int Y;
            }
        }
    }
}
