using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using G4.Recorders.Sap.Domain.Middlewares;
using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.UnitTests.Support;

namespace G4.Recorders.Sap.Domain.UnitTests.Middlewares
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapSessionSubscriptions))]
    [TestCategory("UnitTest")]
    public sealed class SapSessionSubscriptionsTests
    {
        // Holds the default recorder values: both streams, record-mode permission, no scope.
        private static readonly SapRecorderOptionsModel DefaultOptions = TestOptions.Resolve(request: null);

        // Lists the Record assignments of an attach followed by a restoring detach.
        private static readonly string[] ExpectedRecordToggle = ["set Record=True", "set Record=False"];

        [TestMethod(DisplayName = "Verify that a new session is subscribed, set to recording mode, and restored on detach")]
        public void SynchronizeAttachAndRestoreTest()
        {
            // Arrange: a session that is not recording, and a subscription that counts its disposal.
            var tree = new FakeSapTree();
            tree.Session.With("Record", false);
            var subscription = new FakeSubscription();
            var subscriptions = new SapSessionSubscriptions((_, _) => subscription, _ => { });

            // Act: attach, then detach with SAP GUI still reachable.
            subscriptions.Synchronize([tree.Session], DefaultOptions);
            var status = subscriptions.GetStatus();
            subscriptions.DetachAll(isRestoring: true);

            // Assert: Record went on and back off, the session was reported recording, and the subscription ended.
            Assert.AreSequenceEqual(ExpectedRecordToggle, tree.Session.Invocations);
            Assert.IsTrue(status[0].IsRecording);
            Assert.AreEqual(FakeSapTree.SessionId, status[0].Id);
            Assert.AreEqual(1, subscription.DisposeCount);
        }

        [TestMethod(DisplayName = "Verify that a session refusing recording mode is reported and unsubscribed")]
        public void SynchronizeRecordingDisabledTest()
        {
            // Arrange: a session whose system disables recording.
            var tree = new FakeSapTree();
            tree.Session.SetPropertyFailure = new COMException("Recording is disabled.");
            var subscription = new FakeSubscription();
            var subscriptions = new SapSessionSubscriptions((_, _) => subscription, _ => { });

            // Act: try to attach, twice.
            subscriptions.Synchronize([tree.Session], DefaultOptions);
            subscriptions.Synchronize([tree.Session], DefaultOptions);
            var status = subscriptions.GetStatus();

            // Assert: one refusal, reported with a reason and not retried.
            Assert.IsFalse(status[0].IsRecording);
            Assert.Contains("user_scripting_disable_recording", status[0].Reason);
            Assert.AreEqual(1, subscription.DisposeCount);
            Assert.HasCount(1, tree.Session.Invocations);
        }

        [TestMethod(DisplayName = "Verify that a closed session is dropped without restoring its recording mode")]
        public void SynchronizeClosedSessionTest()
        {
            // Arrange: an attached session.
            var tree = new FakeSapTree();
            var subscription = new FakeSubscription();
            var subscriptions = new SapSessionSubscriptions((_, _) => subscription, _ => { });
            subscriptions.Synchronize([tree.Session], DefaultOptions);

            // Act: synchronize after the session closed.
            subscriptions.Synchronize([], DefaultOptions);

            // Assert: the subscription ended and nothing remains to report.
            Assert.AreEqual(1, subscription.DisposeCount);
            Assert.IsEmpty(subscriptions.GetStatus());
        }

        [TestMethod(DisplayName = "Verify that a session without events is reported and not set to recording mode")]
        public void SynchronizeEventsUnavailableTest()
        {
            // Arrange: a subscription attempt that fails as for an object without connection points.
            var tree = new FakeSapTree();
            var subscriptions = new SapSessionSubscriptions((_, _) => throw new InvalidCastException(), _ => { });

            // Act: try to attach.
            subscriptions.Synchronize([tree.Session], DefaultOptions);

            // Assert: reported, and Record was never touched.
            Assert.IsFalse(subscriptions.GetStatus()[0].IsRecording);
            Assert.IsEmpty(tree.Session.Invocations);
        }

        [TestMethod(DisplayName = "Verify that without record-mode permission the session is subscribed but Record is never set")]
        public void SynchronizeWithoutRecordPermissionTest()
        {
            // Arrange: a session that is not recording, and values that forbid setting Record.
            var tree = new FakeSapTree();
            tree.Session.With("Record", false);
            var subscription = new FakeSubscription();
            var subscriptions = new SapSessionSubscriptions((_, _) => subscription, _ => { });
            var options = TestOptions.Resolve(new SapRecorderParametersModel { SetRecordMode = false });

            // Act: attach, read the status, and detach.
            subscriptions.Synchronize([tree.Session], options);
            var status = subscriptions.GetStatus();
            subscriptions.DetachAll(isRestoring: true);

            // Assert: Record untouched, the session reported as not recording, and the subscription ended.
            Assert.IsEmpty(tree.Session.Invocations);
            Assert.IsFalse(status[0].IsRecording);
            Assert.Contains("setRecordMode", status[0].Reason);
            Assert.AreEqual(1, subscription.DisposeCount);
        }

        [TestMethod(DisplayName = "Verify that changed recorder values re-attach every session")]
        public void SynchronizeChangedOptionsTest()
        {
            // Arrange: a session attached with the defaults.
            var tree = new FakeSapTree();
            tree.Session.With("Record", false);
            var subscriptionCount = 0;
            var subscriptions = new SapSessionSubscriptions((_, _) => { subscriptionCount++; return new FakeSubscription(); }, _ => { });
            subscriptions.Synchronize([tree.Session], DefaultOptions);

            // Act: synchronize with values that forbid setting Record.
            subscriptions.Synchronize([tree.Session], TestOptions.Resolve(new SapRecorderParametersModel { SetRecordMode = false }));

            // Assert: Record was restored and the session subscribed again, now without Record.
            Assert.AreEqual(2, subscriptionCount);
            Assert.AreSequenceEqual(ExpectedRecordToggle, tree.Session.Invocations);
        }

        [TestMethod(DisplayName = "Verify that a Change event publishes one event per command with the component's chain")]
        public void ReceiveChangeTest()
        {
            // Arrange: subscriptions that collect published events.
            var tree = new FakeSapTree();
            var published = new List<SapEventModel>();
            var subscriptions = new SapSessionSubscriptions((_, _) => new FakeSubscription(), published.Add);
            var commandArray = new object[] { new object[] { "SP", "text", "BCUSER" }, new object[] { "M", "setFocus" } };

            // Act: receive a Change for the user field.
            subscriptions.Receive([tree.Session, tree.UserField, commandArray], timestamp: 7);

            // Assert: two events targeting the field.
            Assert.HasCount(2, published);
            Assert.AreEqual("SendKeys", published[0].Event);
            Assert.AreEqual("InvokeSapMethod", published[1].Event);
            Assert.AreEqual("//GuiTextField[@id='/app/con[0]/ses[0]/wnd[0]/usr/txtRSYST-BNAME']", published[0].Chain.Locator);
        }

        [TestMethod(DisplayName = "Verify that session events other than Change are ignored")]
        #region *** Data Set ***
        [DataRow(1)]
        [DataRow(2)]
        #endregion
        public void ReceiveOtherEventTest(int argumentCount)
        {
            // Arrange: subscriptions that collect published events, and an event of another shape.
            var tree = new FakeSapTree();
            var published = new List<SapEventModel>();
            var subscriptions = new SapSessionSubscriptions((_, _) => new FakeSubscription(), published.Add);
            var arguments = argumentCount == 1 ? new object[] { tree.Session } : new object[] { tree.Session, "/nSE16" };

            // Act: receive the event.
            subscriptions.Receive(arguments, timestamp: 7);

            // Assert: nothing is published.
            Assert.IsEmpty(published);
        }

        // Counts how often a subscription is disposed.
        private sealed class FakeSubscription : IDisposable
        {
            // Gets the number of Dispose calls.
            internal int DisposeCount { get; private set; }

            // Records one disposal.
            public void Dispose() => DisposeCount++;
        }
    }
}
