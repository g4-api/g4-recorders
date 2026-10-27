/*
 * RESOURCES
 * https://learn.microsoft.com/en-us/windows/win32/api/winuser/
 * https://learn.microsoft.com/en-us/windows/win32/api/objbase/nf-objbase-getrunningobjecttable
 */
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace G4.Recorders.Sap.Domain.Native
{
    /// <summary>
    /// Declares the Win32 and COM entry points the SAP recorder uses for window hit-testing, input hooks, message
    /// pumping, and the running object table.
    /// </summary>
    /// <remarks>
    /// Declarations use source-generated marshalling. Callers own the threading rules: hooks and message pumps are
    /// called on the thread that owns them, and the running object table is read on the scripting thread.
    /// </remarks>
    internal static partial class NativeMethods
    {
        #region *** Constants    ***
        /// <summary>
        /// Selects the root window in <see cref="GetAncestor"/>, walking past child windows to the top level.
        /// </summary>
        internal const uint GaRoot = 2;

        /// <summary>
        /// Wakes <see cref="MsgWaitForMultipleObjectsEx"/> for any queued input, including COM's own window messages.
        /// </summary>
        internal const uint QsAllInput = 0x04FF;

        /// <summary>
        /// Makes <see cref="MsgWaitForMultipleObjectsEx"/> return for messages already in the queue, not only new ones.
        /// </summary>
        internal const uint MwmoInputAvailable = 0x0004;

        /// <summary>
        /// Removes each message <see cref="PeekMessage"/> returns from the queue.
        /// </summary>
        internal const uint PmRemove = 0x0001;

        /// <summary>
        /// Identifies the low-level keyboard hook for <see cref="SetWindowsHookEx"/>.
        /// </summary>
        internal const int WhKeyboardLowLevel = 13;

        /// <summary>
        /// Identifies the low-level mouse hook for <see cref="SetWindowsHookEx"/>.
        /// </summary>
        internal const int WhMouseLowLevel = 14;

        /// <summary>
        /// Restores a minimized window in <see cref="ShowWindow"/>.
        /// </summary>
        internal const int SwRestore = 9;

        /// <summary>
        /// Posts a quit request to a thread's message loop.
        /// </summary>
        internal const uint WmQuit = 0x0012;
        #endregion

        #region *** Delegates    ***
        /// <summary>
        /// Receives low-level hook notifications; the callback returns the result of <see cref="CallNextHookEx"/>.
        /// </summary>
        /// <param name="code">The hook code; negative values are passed on without processing.</param>
        /// <param name="wParam">The message identifier.</param>
        /// <param name="lParam">A pointer to the hook structure.</param>
        /// <returns>The value the next hook in the chain returned.</returns>
        internal delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Passes a hook notification to the next hook in the chain.
        /// </summary>
        [LibraryImport("user32.dll")]
        internal static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Creates a bind context, which moniker display names require.
        /// </summary>
        [LibraryImport("ole32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int CreateBindCtx(int reserved, out IntPtr bindContext);

        /// <summary>
        /// Dispatches a message to its window procedure; COM uses it to deliver incoming calls on STA threads.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
        internal static partial IntPtr DispatchMessage(in Message message);

        /// <summary>
        /// Gets the root, parent, or owner of a window.
        /// </summary>
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetAncestor(IntPtr window, uint flags);

        /// <summary>
        /// Reads the state of a key at the time the input event was generated.
        /// </summary>
        [LibraryImport("user32.dll")]
        internal static partial short GetAsyncKeyState(int virtualKey);

        /// <summary>
        /// Reads the cursor position in physical screen coordinates for a per-monitor DPI aware process.
        /// </summary>
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetCursorPos(out Point point);

        /// <summary>
        /// Gets the window that currently receives keyboard input.
        /// </summary>
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetForegroundWindow();

        /// <summary>
        /// Gets the active keyboard layout of a thread.
        /// </summary>
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetKeyboardLayout(uint threadId);

        /// <summary>
        /// Reads the localized name of a key from its scan code.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "GetKeyNameTextW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int GetKeyNameText(int lParam, [Out] char[] buffer, int size);

        /// <summary>
        /// Reads the toggle and pressed state of a key from the calling thread's state.
        /// </summary>
        [LibraryImport("user32.dll")]
        internal static partial short GetKeyState(int virtualKey);

        /// <summary>
        /// Gets the module handle of the calling process.
        /// </summary>
        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr GetModuleHandle(string moduleName);

        /// <summary>
        /// Returns the running object table of the calling user's session.
        /// </summary>
        [LibraryImport("ole32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial int GetRunningObjectTable(int reserved, out IntPtr table);

        /// <summary>
        /// Gets the thread that created a window and, optionally, its process.
        /// </summary>
        [LibraryImport("user32.dll")]
        internal static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        /// <summary>
        /// Waits for a handle or for queued window messages, so a thread can pump messages while it is idle.
        /// </summary>
        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[] handles, uint milliseconds, uint wakeMask, uint flags);

        /// <summary>
        /// Removes the next message from the calling thread's queue, if any.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PeekMessage(out Message message, IntPtr window, uint filterMin, uint filterMax, uint removeMessage);

        /// <summary>
        /// Posts a message to a thread's queue, waking its message loop.
        /// </summary>
        [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Installs a low-level hook on the calling thread, which then has to pump messages.
        /// </summary>
        /// <remarks>
        /// The procedure is a function pointer from <see cref="Marshal.GetFunctionPointerForDelegate{TDelegate}(TDelegate)"/>;
        /// the caller keeps the <see cref="HookProcedure"/> delegate alive until the hook is removed.
        /// </remarks>
        [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        internal static partial IntPtr SetWindowsHookEx(int hookId, IntPtr procedure, IntPtr module, uint threadId);

        /// <summary>
        /// Translates a virtual key into the characters it types with a given keyboard state and layout.
        /// </summary>
        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int ToUnicodeEx(
            uint virtualKey,
            uint scanCode,
            byte[] keyState,
            [Out] char[] buffer,
            int bufferSize,
            uint flags,
            IntPtr layout);

        /// <summary>
        /// Translates virtual-key messages into character messages.
        /// </summary>
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TranslateMessage(in Message message);

        /// <summary>
        /// Removes a hook installed by <see cref="SetWindowsHookEx"/>.
        /// </summary>
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnhookWindowsHookEx(IntPtr hook);

        /// <summary>
        /// Gets the window at a screen point.
        /// </summary>
        [LibraryImport("user32.dll")]
        internal static partial IntPtr WindowFromPoint(Point point);

        /// <summary>
        /// Tests whether a window is minimized.
        /// </summary>
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsIconic(IntPtr window);

        /// <summary>
        /// Asks Windows to bring a window to the foreground; Windows may refuse, so callers verify the outcome.
        /// </summary>
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetForegroundWindow(IntPtr window);

        /// <summary>
        /// Moves the cursor to physical screen coordinates.
        /// </summary>
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetPhysicalCursorPos(int x, int y);

        /// <summary>
        /// Changes a window's show state, such as restoring it from minimized.
        /// </summary>
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ShowWindow(IntPtr window, int command);

        /// <summary>
        /// Gets the thread identifier of the calling thread, which <see cref="PostThreadMessage"/> addresses.
        /// </summary>
        [LibraryImport("kernel32.dll")]
        internal static partial uint GetCurrentThreadId();

        /// <summary>
        /// Converts a native interface pointer into a runtime callable wrapper and releases the pointer's own reference.
        /// </summary>
        /// <param name="pointer">The interface pointer returned by a native call.</param>
        /// <returns>The wrapper, which holds its own reference.</returns>
        internal static object ConvertToObject(IntPtr pointer)
        {
            try
            {
                // Wrap the pointer; the wrapper takes its own reference.
                return Marshal.GetObjectForIUnknown(pointer);
            }
            finally
            {
                // Release the reference the native call returned, so only the wrapper keeps the object alive.
                Marshal.Release(pointer);
            }
        }

        /// <summary>
        /// Opens the running object table and a bind context for reading moniker display names.
        /// </summary>
        /// <param name="table">The running object table.</param>
        /// <param name="bindContext">The bind context.</param>
        internal static void OpenRunningObjectTable(out IRunningObjectTable table, out IBindCtx bindContext)
        {
            // Open both objects; either call fails only when COM is unusable in this process.
            Marshal.ThrowExceptionForHR(GetRunningObjectTable(reserved: 0, out var tablePointer));
            table = (IRunningObjectTable)ConvertToObject(tablePointer);

            Marshal.ThrowExceptionForHR(CreateBindCtx(reserved: 0, out var bindContextPointer));
            bindContext = (IBindCtx)ConvertToObject(bindContextPointer);
        }
        #endregion

        #region *** Nested Types ***
        /// <summary>
        /// Mirrors the Win32 <c>KBDLLHOOKSTRUCT</c> structure passed to the low-level keyboard hook.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardHookData
        {
            /// <summary>
            /// The virtual-key code.
            /// </summary>
            public uint VirtualKey;

            /// <summary>
            /// The hardware scan code.
            /// </summary>
            public uint ScanCode;

            /// <summary>
            /// The extended-key, injected, and transition flags.
            /// </summary>
            public uint Flags;

            /// <summary>
            /// The message time stamp.
            /// </summary>
            public uint Time;

            /// <summary>
            /// Extra information associated with the message.
            /// </summary>
            public IntPtr ExtraInfo;
        }

        /// <summary>
        /// Mirrors the Win32 <c>MSLLHOOKSTRUCT</c> structure passed to the low-level mouse hook.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseHookData
        {
            /// <summary>
            /// The cursor position in physical screen coordinates.
            /// </summary>
            public Point Point;

            /// <summary>
            /// The wheel delta in the high word for wheel messages.
            /// </summary>
            public uint MouseData;

            /// <summary>
            /// The injected flags.
            /// </summary>
            public uint Flags;

            /// <summary>
            /// The message time stamp.
            /// </summary>
            public uint Time;

            /// <summary>
            /// Extra information associated with the message.
            /// </summary>
            public IntPtr ExtraInfo;
        }

        /// <summary>
        /// Mirrors the Win32 <c>MSG</c> structure read by the message pump.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Message
        {
            /// <summary>
            /// The window that receives the message.
            /// </summary>
            public IntPtr Window;

            /// <summary>
            /// The message identifier.
            /// </summary>
            public uint Id;

            /// <summary>
            /// The first message parameter.
            /// </summary>
            public IntPtr WParam;

            /// <summary>
            /// The second message parameter.
            /// </summary>
            public IntPtr LParam;

            /// <summary>
            /// The time the message was posted.
            /// </summary>
            public uint Time;

            /// <summary>
            /// The cursor position when the message was posted.
            /// </summary>
            public Point Point;

            /// <summary>
            /// Private data some message sources append.
            /// </summary>
            public uint Private;
        }

        /// <summary>
        /// Mirrors the Win32 <c>POINT</c> structure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            /// <summary>
            /// The horizontal coordinate.
            /// </summary>
            public int X;

            /// <summary>
            /// The vertical coordinate.
            /// </summary>
            public int Y;
        }
        #endregion
    }
}
