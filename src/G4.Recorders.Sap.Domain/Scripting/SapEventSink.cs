/*
 * RESOURCES
 * https://learn.microsoft.com/en-us/dotnet/standard/native-interop/tutorial-comwrappers
 * https://learn.microsoft.com/en-us/windows/win32/api/ocidl/nn-ocidl-iconnectionpoint
 */
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace G4.Recorders.Sap.Domain.Scripting
{
    /// <summary>
    /// Receives SAP GUI Scripting events, such as <c>GuiSession.Change</c>, without the SAP type library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SAP GUI raises events through COM connection points, which call the subscriber's <c>IDispatch.Invoke</c> with
    /// a dispatch identifier and the event arguments. The sink exposes a hand-built <c>IDispatch</c> through
    /// <see cref="ComWrappers"/>, answering for <c>IDispatch</c> and for every event interface the source offers, so
    /// the event interface identifiers are learned at run time from the source's connection points.
    /// </para>
    /// <para>
    /// Arguments are converted to managed values before the handler runs: scripting objects become runtime callable
    /// wrappers and SAFEARRAYs become object arrays. The handler runs on the thread that receives the call, which is
    /// the scripting thread, and its exceptions are swallowed so a failing handler never breaks SAP GUI.
    /// </para>
    /// </remarks>
    internal sealed class SapEventSink : IDisposable
    {
        #region *** Constants    ***
        // Identifies IPropertyNotifySink, a connection point that carries property changes, not SAP events.
        private static readonly Guid PropertyNotifySinkId = new("9BFBBC02-EFF1-101A-84ED-00AA00BE3B4F");
        #endregion

        #region *** Fields       ***
        // Runs for every event, with the dispatch identifier and the converted arguments.
        private readonly Action<int, object[]> _handler;

        // Holds every connection point the sink is advised on, with its cookie, until disposal.
        private readonly List<(IConnectionPointRaw Point, int Cookie)> _subscriptions = [];

        // Holds the sink's own COM identity while advised, so it is released once on disposal.
        private IntPtr _unknown;
        #endregion

        #region *** Constructors ***
        // Creates a sink for a set of event interfaces; Advise builds it from the source's connection points.
        private SapEventSink(Guid[] interfaceIds, Action<int, object[]> handler)
        {
            InterfaceIds = interfaceIds;
            _handler = handler;
        }
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets the event interface identifiers the sink answers for, besides <c>IDispatch</c>.
        /// </summary>
        internal Guid[] InterfaceIds { get; }
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Subscribes a handler to every event interface a scripting object raises.
        /// </summary>
        /// <param name="source">The scripting COM object, such as a <c>GuiSession</c>.</param>
        /// <param name="handler">The handler, which receives the dispatch identifier and the converted arguments.</param>
        /// <returns>The advised sink; dispose it to unsubscribe.</returns>
        /// <exception cref="InvalidOperationException">The object raises no events.</exception>
        internal static SapEventSink Advise(object source, Action<int, object[]> handler)
        {
            // Require the source and handler; the source must support connection points.
            ArgumentNullException.ThrowIfNull(argument: source);
            ArgumentNullException.ThrowIfNull(argument: handler);

            // Collect the event connection points, skipping property-change notifications.
            var points = GetConnectionPoints((IConnectionPointContainer)source);
            if (points.Count == 0)
            {
                throw new InvalidOperationException("The SAP GUI Scripting object raises no events.");
            }

            // Build the sink for the discovered interfaces and advise it on every point.
            var sink = new SapEventSink([.. points.Select(i => i.InterfaceId)], handler);
            var unknown = sink.GetUnknown();

            try
            {
                foreach (var (point, _) in points)
                {
                    point.Advise(unknown, out var cookie);
                    sink._subscriptions.Add((point, cookie));
                }
            }
            catch (COMException)
            {
                // Undo the subscriptions made so far, then report the refusal.
                sink.Dispose();
                throw;
            }

            return sink;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Unsubscribe from every point; a source that already exited fails, which is the same outcome.
            foreach (var (point, cookie) in _subscriptions)
            {
                try
                {
                    point.Unadvise(cookie);
                }
                catch (COMException)
                {
                    // The source is gone; there is nothing left to unsubscribe from.
                }
            }

            _subscriptions.Clear();

            // Release the sink's own COM identity once.
            if (_unknown != IntPtr.Zero)
            {
                Marshal.Release(_unknown);
                _unknown = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Creates a sink that is not advised anywhere yet, for callers that subscribe it themselves.
        /// </summary>
        /// <param name="interfaceIds">The event interface identifiers the sink answers for, besides <c>IDispatch</c>.</param>
        /// <param name="handler">The handler, which receives the dispatch identifier and the converted arguments.</param>
        /// <returns>The sink; dispose it to release its COM identity.</returns>
        internal static SapEventSink New(Guid[] interfaceIds, Action<int, object[]> handler)
        {
            // Require both parts; an empty identifier list still answers for IDispatch.
            ArgumentNullException.ThrowIfNull(argument: interfaceIds);
            ArgumentNullException.ThrowIfNull(argument: handler);

            return new SapEventSink(interfaceIds, handler);
        }

        /// <summary>
        /// Gets the sink's COM identity, creating it on first use; the sink owns the reference and releases it on disposal.
        /// </summary>
        /// <returns>The <c>IUnknown</c> pointer of the sink.</returns>
        internal IntPtr GetUnknown()
        {
            // Create the identity once; ComWrappers returns the same object for later calls anyway.
            if (_unknown == IntPtr.Zero)
            {
                _unknown = SapEventSinkWrappers.Instance.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None);
            }

            return _unknown;
        }

        /// <summary>
        /// Runs the handler for one event.
        /// </summary>
        /// <param name="dispatchId">The event's dispatch identifier.</param>
        /// <param name="arguments">The event arguments, in declaration order.</param>
        internal void Receive(int dispatchId, object[] arguments)
        {
            _handler(dispatchId, arguments);
        }

        // Reads the connection points of a source and the event interface each one carries.
        private static List<(IConnectionPointRaw Point, Guid InterfaceId)> GetConnectionPoints(IConnectionPointContainer container)
        {
            // Enumerate every point; the raw view of each lets Advise take the sink's IUnknown pointer directly.
            var points = new List<(IConnectionPointRaw, Guid)>();
            container.EnumConnectionPoints(out var enumerator);
            var buffer = new IConnectionPoint[1];

            while (enumerator.Next(1, buffer, IntPtr.Zero) == 0)
            {
                buffer[0].GetConnectionInterface(out var interfaceId);
                if (interfaceId != PropertyNotifySinkId)
                {
                    points.Add(((IConnectionPointRaw)buffer[0], interfaceId));
                }
            }

            return points;
        }
        #endregion

        #region *** Nested Types ***
        /// <summary>
        /// Views <c>IConnectionPoint</c> with pointer parameters, so the sink's own <c>IUnknown</c> can be advised as is.
        /// </summary>
        [ComImport]
        [Guid("B196B286-BAB4-101A-B69C-00AA00341D07")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IConnectionPointRaw
        {
            /// <summary>
            /// Gets the event interface the point carries.
            /// </summary>
            void GetConnectionInterface(out Guid interfaceId);

            /// <summary>
            /// Gets the container that owns the point.
            /// </summary>
            void GetConnectionPointContainer(out IntPtr container);

            /// <summary>
            /// Subscribes a sink and returns the cookie that identifies the subscription.
            /// </summary>
            void Advise(IntPtr sink, out int cookie);

            /// <summary>
            /// Ends a subscription.
            /// </summary>
            void Unadvise(int cookie);

            /// <summary>
            /// Enumerates the current subscriptions.
            /// </summary>
            void EnumConnections(out IntPtr enumerator);
        }

        /// <summary>
        /// Mirrors the Win32 <c>DISPPARAMS</c> structure passed to <c>IDispatch.Invoke</c>.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct DispatchParameters
        {
            /// <summary>
            /// The arguments, as an array of <c>VARIANT</c> values in reverse order.
            /// </summary>
            public IntPtr Arguments;

            /// <summary>
            /// The dispatch identifiers of named arguments.
            /// </summary>
            public IntPtr NamedArgumentIds;

            /// <summary>
            /// The number of arguments.
            /// </summary>
            public uint ArgumentCount;

            /// <summary>
            /// The number of named arguments.
            /// </summary>
            public uint NamedArgumentCount;
        }

        /// <summary>
        /// Builds the <c>IDispatch</c> identity of <see cref="SapEventSink"/> instances.
        /// </summary>
        /// <remarks>
        /// Every interface entry shares one vtable, because an event dispinterface is <c>IDispatch</c> with a different
        /// identifier. Interface entry tables are cached per set of identifiers and live for the process.
        /// </remarks>
        internal sealed unsafe class SapEventSinkWrappers : ComWrappers
        {
            /// <summary>
            /// The shared instance used to create sink identities.
            /// </summary>
            internal static readonly SapEventSinkWrappers Instance = new();

            // Reported for members an event sink does not implement.
            private const int NotImplemented = unchecked((int)0x80004001);

            // Reported by GetIDsOfNames; the sink is called by identifier only.
            private const int UnknownName = unchecked((int)0x80020006);

            // Identifies IDispatch, which every event source may ask for.
            private static readonly Guid DispatchInterfaceId = new("00020400-0000-0000-C000-000000000046");

            // Caches interface entry tables by their identifier set, because the native tables must outlive every sink.
            private static readonly ConcurrentDictionary<string, IntPtr> EntryTables = new();

            // Holds the shared IDispatch vtable.
            private static readonly IntPtr DispatchVtable = NewDispatchVtable();

            /// <inheritdoc />
            protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
            {
                // Answer for IDispatch and every event interface of this sink, all with the same vtable.
                var interfaceIds = ((SapEventSink)obj).InterfaceIds;
                count = interfaceIds.Length + 1;

                var table = EntryTables.GetOrAdd(string.Join(',', interfaceIds), _ => NewEntryTable(interfaceIds));

                return (ComInterfaceEntry*)table;
            }

            /// <inheritdoc />
            protected override object CreateObject(IntPtr externalComObject, CreateObjectFlags flags)
            {
                throw new NotSupportedException("Event sinks only expose managed objects.");
            }

            /// <inheritdoc />
            protected override void ReleaseObjects(IEnumerable objects)
            {
                throw new NotSupportedException("Event sinks track no external objects.");
            }

            // IDispatch.GetIDsOfNames: the source calls events by identifier, so names are unknown.
            [UnmanagedCallersOnly]
            private static int GetIdsOfNames(IntPtr self, Guid* interfaceId, IntPtr* names, uint count, uint locale, int* ids)
            {
                return UnknownName;
            }

            // IDispatch.GetTypeInfo: the sink has no type information.
            [UnmanagedCallersOnly]
            private static int GetTypeInfo(IntPtr self, uint index, uint locale, IntPtr* typeInfo)
            {
                return NotImplemented;
            }

            // IDispatch.GetTypeInfoCount: the sink has no type information.
            [UnmanagedCallersOnly]
            private static int GetTypeInfoCount(IntPtr self, uint* count)
            {
                *count = 0;
                return 0;
            }

            // IDispatch.Invoke: converts the arguments and runs the sink's handler; failures never reach SAP GUI.
            [UnmanagedCallersOnly]
            private static int Invoke(
                IntPtr self,
                int dispatchId,
                Guid* interfaceId,
                uint locale,
                ushort flags,
                DispatchParameters* parameters,
                IntPtr result,
                IntPtr exceptionInfo,
                uint* argumentError)
            {
                try
                {
                    // Resolve the managed sink behind this identity, then hand it the converted arguments.
                    var sink = ComInterfaceDispatch.GetInstance<SapEventSink>((ComInterfaceDispatch*)self);
                    sink.Receive(dispatchId, ReadArguments(parameters));
                }
                catch (Exception)
                {
                    // A failing handler must not fail SAP GUI's own event dispatch; the event is dropped.
                }

                return 0;
            }

            // Builds the interface entry table: IDispatch first, then each event interface.
            private static IntPtr NewEntryTable(Guid[] interfaceIds)
            {
                var entries = (ComInterfaceEntry*)NativeMemory.Alloc((nuint)(sizeof(ComInterfaceEntry) * (interfaceIds.Length + 1)));
                entries[0].IID = DispatchInterfaceId;
                entries[0].Vtable = DispatchVtable;

                for (var index = 0; index < interfaceIds.Length; index++)
                {
                    entries[index + 1].IID = interfaceIds[index];
                    entries[index + 1].Vtable = DispatchVtable;
                }

                return (IntPtr)entries;
            }

            // Builds the IDispatch vtable: the runtime's IUnknown, then the four IDispatch members.
            private static IntPtr NewDispatchVtable()
            {
                GetIUnknownImpl(out var queryInterface, out var addReference, out var release);
                var vtable = (IntPtr*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(SapEventSinkWrappers), IntPtr.Size * 7);

                vtable[0] = queryInterface;
                vtable[1] = addReference;
                vtable[2] = release;
                vtable[3] = (IntPtr)(delegate* unmanaged<IntPtr, uint*, int>)&GetTypeInfoCount;
                vtable[4] = (IntPtr)(delegate* unmanaged<IntPtr, uint, uint, IntPtr*, int>)&GetTypeInfo;
                vtable[5] = (IntPtr)(delegate* unmanaged<IntPtr, Guid*, IntPtr*, uint, uint, int*, int>)&GetIdsOfNames;
                vtable[6] = (IntPtr)(delegate* unmanaged<
                    IntPtr, int, Guid*, uint, ushort, DispatchParameters*, IntPtr, IntPtr, uint*, int>)&Invoke;

                return (IntPtr)vtable;
            }

            // Converts DISPPARAMS arguments, stored last-first, into managed values in declaration order.
            private static object[] ReadArguments(DispatchParameters* parameters)
            {
                // VARIANT is 24 bytes in 64-bit processes and 16 bytes in 32-bit ones.
                var count = (int)parameters->ArgumentCount;
                var variantSize = IntPtr.Size == 8 ? 24 : 16;
                var arguments = new object[count];

                for (var index = 0; index < count; index++)
                {
                    var variant = parameters->Arguments + ((count - 1 - index) * variantSize);
                    arguments[index] = Marshal.GetObjectForNativeVariant(variant);
                }

                return arguments;
            }
        }
        #endregion
    }
}
