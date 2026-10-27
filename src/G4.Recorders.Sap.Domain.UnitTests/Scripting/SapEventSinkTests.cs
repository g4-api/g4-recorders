using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Runtime.InteropServices;

using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain.UnitTests.Scripting
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapEventSink))]
    [TestCategory("UnitTest")]
    public sealed unsafe partial class SapEventSinkTests
    {
        // Identifies a made-up event interface, standing for SAP's GuiSession event dispinterface.
        private static readonly Guid EventInterfaceId = new("5A1E0D2C-7F3B-4C88-9E61-0B4D2F9A6C13");

        // Identifies IDispatch.
        private static readonly Guid DispatchInterfaceId = new("00020400-0000-0000-C000-000000000046");

        // Lists the command of the Change call the test makes.
        private static readonly object[] Command = ["SP", "text", "BCUSER"];

        // Reported by QueryInterface for an interface the object does not implement.
        private const int NoInterface = unchecked((int)0x80004002);

        [TestMethod(DisplayName = "Verify that the sink answers for IDispatch and its event interface, and nothing else")]
        public void QueryInterfaceTest()
        {
            // Arrange: a sink for one event interface.
            using var sink = SapEventSink.New([EventInterfaceId], (_, _) => { });
            var unknown = sink.GetUnknown();

            // Act: ask for the event interface, IDispatch, and an unrelated interface.
            var eventResult = Marshal.QueryInterface(unknown, in EventInterfaceId, out var eventPointer);
            var dispatchResult = Marshal.QueryInterface(unknown, in DispatchInterfaceId, out var dispatchPointer);
            var otherResult = Marshal.QueryInterface(unknown, Guid.NewGuid(), out _);

            // Assert: the first two succeed and the last is refused.
            Assert.AreEqual(0, eventResult);
            Assert.AreEqual(0, dispatchResult);
            Assert.AreEqual(NoInterface, otherResult);
            Marshal.Release(eventPointer);
            Marshal.Release(dispatchPointer);
        }

        [TestMethod(DisplayName = "Verify that Invoke hands the handler the dispatch identifier and the arguments in declaration order")]
        public void InvokeTest()
        {
            // Arrange: a sink that captures its call, and the Change arguments: session, component, command array.
            int receivedId = 0;
            object[] received = null;
            using var sink = SapEventSink.New([EventInterfaceId], (id, arguments) => (receivedId, received) = (id, arguments));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(sink.GetUnknown(), in EventInterfaceId, out var dispatch));

            // Act: call Invoke through the vtable, as SAP GUI does.
            try
            {
                var result = InvokeDispatch(dispatch, dispatchId: 7, "session", "component", new object[] { Command });
                Assert.AreEqual(0, result);
            }
            finally
            {
                Marshal.Release(dispatch);
            }

            // Assert: the identifier and the arguments, with the SAFEARRAY converted to an object array.
            Assert.AreEqual(7, receivedId);
            Assert.HasCount(3, received);
            Assert.AreEqual("session", received[0]);
            Assert.AreEqual("component", received[1]);
            var commands = (object[])received[2];
            Assert.AreSequenceEqual(Command, (object[])commands[0]);
        }

        [TestMethod(DisplayName = "Verify that a failing handler does not fail the caller's Invoke")]
        public void InvokeHandlerFailureTest()
        {
            // Arrange: a sink whose handler throws.
            using var sink = SapEventSink.New([EventInterfaceId], (_, _) => throw new InvalidOperationException("handler failed"));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(sink.GetUnknown(), in EventInterfaceId, out var dispatch));

            // Act: raise an event.
            int result;
            try
            {
                result = InvokeDispatch(dispatch, dispatchId: 1, "session");
            }
            finally
            {
                Marshal.Release(dispatch);
            }

            // Assert: SAP GUI would see success.
            Assert.AreEqual(0, result);
        }

        // Calls IDispatch.Invoke (vtable slot 6) with arguments stored last-first, as COM callers do.
        private static int InvokeDispatch(IntPtr dispatch, int dispatchId, params object[] arguments)
        {
            // Allocate one VARIANT per argument; 64-bit VARIANTs are 24 bytes.
            var variantSize = IntPtr.Size == 8 ? 24 : 16;
            var variants = Marshal.AllocCoTaskMem(variantSize * arguments.Length);

            try
            {
                for (var index = 0; index < arguments.Length; index++)
                {
                    Marshal.GetNativeVariantForObject(arguments[index], variants + ((arguments.Length - 1 - index) * variantSize));
                }

                var parameters = new SapEventSink.DispatchParameters { Arguments = variants, ArgumentCount = (uint)arguments.Length };
                var vtable = *(IntPtr**)dispatch;
                var invoke = (delegate* unmanaged<IntPtr, int, Guid*, uint, ushort, SapEventSink.DispatchParameters*, IntPtr, IntPtr, uint*, int>)vtable[6];
                var emptyId = Guid.Empty;

                return invoke(dispatch, dispatchId, &emptyId, 0, 1, &parameters, IntPtr.Zero, IntPtr.Zero, null);
            }
            finally
            {
                // Clear each VARIANT, releasing the SAFEARRAY and strings, then free the block.
                for (var index = 0; index < arguments.Length; index++)
                {
                    Marshal.ThrowExceptionForHR(VariantClear(variants + (index * variantSize)));
                }

                Marshal.FreeCoTaskMem(variants);
            }
        }

        // Releases the resources a VARIANT holds.
        [LibraryImport("oleaut32.dll")]
        private static partial int VariantClear(IntPtr variant);
    }
}
