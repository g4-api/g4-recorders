using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain.Middlewares
{
    /// <summary>
    /// Keeps the recorder subscribed to the <c>Change</c> events of every open SAP session while a client listens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each synchronization compares the open sessions with the attached ones. A new session is subscribed first and
    /// then switched to recording mode (<c>Record = true</c>), because SAP GUI raises <c>Change</c> only while it
    /// records; its previous <c>Record</c> value is kept. A closed session is dropped. When no client listens, every
    /// session is unsubscribed and its <c>Record</c> value restored.
    /// </para>
    /// <para>
    /// A session whose system disables recording refuses <c>Record</c>; it is reported in the status and left alone. A
    /// session outside the scope of the recorder values is never attached and is reported with that reason.
    /// </para>
    /// <para>
    /// Instances run on the scripting thread only.
    /// </para>
    /// </remarks>
    /// <param name="subscribe">Subscribes a handler to a session's events and returns the subscription.</param>
    /// <param name="publish">Receives every event mapped from a <c>Change</c>.</param>
    internal sealed class SapSessionSubscriptions(
        Func<ISapGuiObject, Action<int, object[]>, IDisposable> subscribe,
        Action<SapEventModel> publish)
    {
        #region *** Constants    ***
        // Reports a session whose system refuses recording mode.
        private const string RecordingDisabledReason =
            "SAP GUI refused recording mode; the system may disable recording (sapgui/user_scripting_disable_recording).";

        // Reports a session that offers no events to subscribe to.
        private const string EventsUnavailableReason = "The session raises no scripting events.";

        // Reports a session left out of recording mode because the recorder may not set it.
        private const string RecordModeNotAllowedReason =
            "Record is off, and setRecordMode does not allow the recorder to turn it on.";
        #endregion

        #region *** Fields       ***
        // Holds each attached session by identifier, with its subscription and previous Record value.
        private readonly Dictionary<string, AttachedSession> _attached = new(StringComparer.Ordinal);

        // Receives every event mapped from a Change.
        private readonly Action<SapEventModel> _publish = publish;

        // Holds the values the attached sessions were attached with; different values re-attach every session.
        private SapRecorderOptionsModel _options;

        // Holds the reasons of sessions that could not be attached, by identifier, so they are not retried each pass.
        private readonly Dictionary<string, string> _refused = new(StringComparer.Ordinal);

        // Subscribes a handler to a session's events.
        private readonly Func<ISapGuiObject, Action<int, object[]>, IDisposable> _subscribe = subscribe;
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Unsubscribes from every session and, when asked, restores each session's previous <c>Record</c> value.
        /// </summary>
        /// <param name="isRestoring">Whether SAP GUI is still reachable, so <c>Record</c> can be restored.</param>
        internal void DetachAll(bool isRestoring)
        {
            // Release every subscription first, so no event arrives while recording mode is restored.
            foreach (var attached in _attached.Values)
            {
                attached.Subscription.Dispose();
                if (isRestoring && attached.IsRecordOwned)
                {
                    SetRecord(attached.Session, attached.PreviousRecord);
                }
            }

            _attached.Clear();
            _refused.Clear();
            _options = null;
        }

        /// <summary>
        /// Gets the recording status of every attached or refused session.
        /// </summary>
        /// <returns>The status, attached sessions first.</returns>
        internal List<SapSessionStatusModel> GetStatus()
        {
            var recording = _attached.Select(i => NewAttachedStatus(i.Key, i.Value));
            var refused = _refused.Select(i => new SapSessionStatusModel { Id = i.Key, Reason = i.Value });

            return [.. recording.Concat(refused)];
        }

        /// <summary>
        /// Handles one session event: a <c>Change</c> is mapped into events and published; other events are ignored.
        /// </summary>
        /// <param name="arguments">The event arguments: session, component, and command array for a <c>Change</c>.</param>
        /// <param name="timestamp">The time the event arrived, in Unix epoch milliseconds.</param>
        /// <remarks>
        /// <c>Change</c> is recognized by its shape, the only session event whose third argument is an array, so the
        /// recorder needs no SAP type library to know its dispatch identifier.
        /// </remarks>
        internal void Receive(object[] arguments, long timestamp)
        {
            // Accept only the Change shape: session, component, command array.
            if (arguments is not { Length: 3 } || arguments[2] is not object[] commandArray)
            {
                return;
            }

            // Wrap the component unless it already is a scripting object, then map each command.
            var component = arguments[1] as ISapGuiObject ?? (arguments[1] is null ? null : new ComSapGuiObject(arguments[1]));
            var chain = SapChainFactory.NewChain(component);

            foreach (var recordingEvent in SapActionMapper.ConvertToEvents(chain, commandArray, timestamp))
            {
                _publish(recordingEvent);
            }
        }

        /// <summary>
        /// Attaches new sessions and drops closed ones.
        /// </summary>
        /// <param name="sessions">The sessions currently open.</param>
        /// <param name="options">
        /// The recorder values in force; values that changed since the last pass re-attach every session.
        /// </param>
        internal void Synchronize(IReadOnlyList<ISapGuiObject> sessions, SapRecorderOptionsModel options)
        {
            // Start over when the values changed, for example after a StartRecorder request.
            ArgumentNullException.ThrowIfNull(argument: options);
            if (_options is not null && !_options.TestEquivalent(options))
            {
                DetachAll(isRestoring: true);
            }

            _options = options;

            // Index the open sessions by identifier.
            var open = new Dictionary<string, ISapGuiObject>(StringComparer.Ordinal);
            foreach (var session in sessions)
            {
                open[SapGuiNavigator.GetString(session, propertyName: "Id")] = session;
            }

            // Drop closed sessions; their Record value went with them.
            foreach (var closedId in _attached.Keys.Where(i => !open.ContainsKey(i)).ToList())
            {
                _attached[closedId].Subscription.Dispose();
                _attached.Remove(closedId);
            }

            foreach (var closedId in _refused.Keys.Where(i => !open.ContainsKey(i)).ToList())
            {
                _refused.Remove(closedId);
            }

            // Attach sessions seen for the first time; sessions outside the scope are only reported.
            foreach (var (id, session) in open)
            {
                if (_attached.ContainsKey(id) || _refused.ContainsKey(id))
                {
                    continue;
                }

                if (!SapSessionScope.TestInScope(session, options.Scope))
                {
                    _refused[id] = SapSessionScope.OutOfScopeReason;
                    continue;
                }

                Attach(id, session);
            }
        }

        // Subscribes to a session, then switches it to recording mode; a refusal is recorded as the session's status.
        private void Attach(string id, ISapGuiObject session)
        {
            // Subscribe first, so the first Change after Record is set is not missed.
            IDisposable subscription;
            try
            {
                subscription = _subscribe(session, (_, arguments) => Receive(arguments, GetTimestamp()));
            }
            catch (Exception exception) when (exception is COMException or InvalidCastException or InvalidOperationException)
            {
                _refused[id] = EventsUnavailableReason;
                return;
            }

            // Without permission, keep the subscription and leave Record as the user set it.
            var previousRecord = SapGuiNavigator.GetBoolean(session, propertyName: "Record");
            if (!_options.SetRecordMode.GetValueOrDefault())
            {
                _attached[id] = new AttachedSession(session, subscription, previousRecord, IsRecordOwned: false);
                return;
            }

            // Switch to recording mode, keeping the previous value; a refusal means the system disables recording.
            if (!SetRecord(session, value: true))
            {
                subscription.Dispose();
                _refused[id] = RecordingDisabledReason;
                return;
            }

            _attached[id] = new AttachedSession(session, subscription, previousRecord, IsRecordOwned: true);
        }

        // Builds the status of an attached session: recording when the recorder owns Record or the user turned it on.
        private static SapSessionStatusModel NewAttachedStatus(string id, AttachedSession attached)
        {
            // Read Record live for sessions the recorder may not switch, because the user can turn it on at any time.
            var isRecording = attached.IsRecordOwned || SapGuiNavigator.GetBoolean(attached.Session, propertyName: "Record");

            return new SapSessionStatusModel
            {
                Id = id,
                IsRecording = isRecording,
                Reason = isRecording ? null : RecordModeNotAllowedReason
            };
        }

        // Gets the current time in Unix epoch milliseconds, the timestamp every recorded event carries.
        private static long GetTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        // Assigns Record; returns false when SAP GUI refuses it or the session has gone.
        private static bool SetRecord(ISapGuiObject session, bool value)
        {
            try
            {
                session.SetProperty("Record", value);
                return true;
            }
            catch (COMException)
            {
                return false;
            }
        }
        #endregion

        #region *** Nested Types ***
        /// <summary>
        /// Holds one attached session, its event subscription, and the <c>Record</c> value to restore.
        /// </summary>
        /// <param name="Session">The session.</param>
        /// <param name="Subscription">The event subscription.</param>
        /// <param name="PreviousRecord">The <c>Record</c> value before the recorder attached.</param>
        /// <param name="IsRecordOwned">Whether the recorder switched <c>Record</c> on and restores it on detach.</param>
        private sealed record AttachedSession(
            ISapGuiObject Session,
            IDisposable Subscription,
            bool PreviousRecord,
            bool IsRecordOwned);
        #endregion
    }
}
