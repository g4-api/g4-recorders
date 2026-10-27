using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using G4.Recorders.Sap.Domain.Hubs;
using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.Native;
using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain.Middlewares
{
    /// <summary>
    /// Captures global mouse and keyboard input and broadcasts it as the <c>user32</c> stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Low-level hooks run on a dedicated thread that only pumps messages. Each callback reads what is cheap and
    /// time-sensitive (key text, modifiers, window handles), queues a record on the scripting thread, and returns at
    /// once, so Windows never drops the hook for being slow. The scripting thread resolves the SAP control and sends the
    /// event in a message whose <c>user32</c> key is set.
    /// </para>
    /// <para>
    /// Input is captured only while a hub client is connected. Each captured record counts toward the shared queue
    /// state, so a client's flush waits for input it already produced.
    /// </para>
    /// </remarks>
    /// <param name="scriptingHost">The host that resolves SAP controls on the scripting thread.</param>
    /// <param name="broadcaster">The broadcaster that sends recording messages to hub clients.</param>
    /// <param name="optionsStore">The store holding the recorder values, which say whether <c>user32</c> is sent.</param>
    /// <param name="logger">The logger for hook installation and resolution failures; it never receives input.</param>
    public sealed class SapInputCaptureService(
        ISapScriptingHost scriptingHost,
        ISapRecordingBroadcaster broadcaster,
        SapRecorderOptionsStore optionsStore,
        ILogger<SapInputCaptureService> logger) : BackgroundService
    {
        #region *** Constants    ***
        // Caps one idle wait of the hook thread, so it rechecks cancellation without input.
        private const uint IdleWaitMilliseconds = 250;

        // Marks an extended key in the keyboard hook flags.
        private const uint ExtendedKeyFlag = 0x01;

        // Identifies pointer moves, which the stream never records.
        private const int MouseMove = 0x0200;
        #endregion

        #region *** Fields       ***
        // Sends recording messages to hub clients.
        private readonly ISapRecordingBroadcaster _broadcaster = broadcaster;

        // Gates capture on connected clients and stamps each record with its generation.
        private readonly RecorderConnectionState _connectionState = RecorderConnectionState.Instance;

        // Logs hook lifecycle and resolution failures; input values are never logged.
        private readonly ILogger<SapInputCaptureService> _logger = logger;

        // Holds the recorder values; user32 is captured only while its stream is enabled.
        private readonly SapRecorderOptionsStore _optionsStore = optionsStore;

        // Counts records from capture to broadcast, for flush requests.
        private readonly RecorderEventQueueState _queueState = RecorderEventQueueState.Instance;

        // Resolves records on the scripting thread; used only there.
        private readonly SapInputResolver _resolver = new();

        // Runs SAP lookups and the broadcast on the scripting thread.
        private readonly ISapScriptingHost _scriptingHost = scriptingHost;

        // Keeps the keyboard callback delegate alive while its hook is installed.
        private NativeMethods.HookProcedure _keyboardProcedure;

        // Keeps the mouse callback delegate alive while its hook is installed.
        private NativeMethods.HookProcedure _mouseProcedure;
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Run the hooks on their own thread; it installs them, pumps messages, and removes them on stop.
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                RunHooks(stoppingToken);
                completion.TrySetResult();
            })
            {
                IsBackground = true,
                Name = "SapRecorderHooks"
            };
            thread.Start();

            return completion.Task;
        }

        // Queues one record for resolution and broadcast on the scripting thread.
        private void AddRecord(SapInputRecord record)
        {
            // Count the record before it becomes visible, so a flush never misses it.
            _queueState.AddEnqueued();
            _scriptingHost.Post(application =>
            {
                try
                {
                    // Resolve against SAP GUI and send only input aimed at SAP.
                    var userEvent = _resolver.Resolve(application, record, _optionsStore.Current.Scope);
                    if (userEvent is not null)
                    {
                        _ = _broadcaster.SendAsync(new SapRecordingMessageModel { User32 = userEvent });
                    }
                }
                catch (Exception exception) when (exception is COMException or InvalidOperationException)
                {
                    // A closing or busy session cannot be resolved; skip the record without its values.
                    _logger.LogWarning("Skipped a recorded input event: {Reason}", exception.Message);
                }
                finally
                {
                    _queueState.AddProcessed();
                }
            });
        }

        // Installs a hook with the module handle first, then without it, which some hosts require.
        private static IntPtr InstallHook(int hookId, NativeMethods.HookProcedure procedure)
        {
            // Hand Windows a native pointer to the delegate; the caller keeps the delegate alive.
            var pointer = Marshal.GetFunctionPointerForDelegate(procedure);
            var hook = NativeMethods.SetWindowsHookEx(hookId, pointer, NativeMethods.GetModuleHandle(null), 0);

            return hook != IntPtr.Zero ? hook : NativeMethods.SetWindowsHookEx(hookId, pointer, IntPtr.Zero, 0);
        }

        // Keyboard hook callback: captures key presses and releases while a client listens.
        private IntPtr ReceiveKeyboardEvent(int code, IntPtr wParam, IntPtr lParam)
        {
            // Pass through invalid codes and idle time untouched.
            var message = wParam.ToInt32();
            var isKeyMessage = message is
                SapInputRecord.KeyDown or SapInputRecord.KeyUp or SapInputRecord.SystemKeyDown or SapInputRecord.SystemKeyUp;
            if (code < 0 || !isKeyMessage || !TestCapturing())
            {
                return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            }

            // Resolve the key text and modifiers now, while the keyboard state still matches the press.
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);
            var isExtended = (data.Flags & ExtendedKeyFlag) != 0;
            var isKeyDown = message is SapInputRecord.KeyDown or SapInputRecord.SystemKeyDown;
            var (keyText, modifiers) = SapKeyboardTranslator.ResolveKeyInput(
                data.VirtualKey,
                data.ScanCode,
                isExtended,
                isKeyDown);

            // Queue the record with the foreground window, which decides whether the key was aimed at SAP.
            AddRecord(new SapInputRecord
            {
                ForegroundWindow = SapDesktop.GetForegroundRootWindow(),
                Generation = _connectionState.SessionGeneration,
                IsExtended = isExtended,
                IsKeyboard = true,
                KeyText = keyText,
                Message = message,
                Modifiers = modifiers,
                ScanCode = data.ScanCode,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                VirtualKey = data.VirtualKey
            });

            return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        // Mouse hook callback: captures button and wheel events while a client listens; moves are never recorded.
        private IntPtr ReceiveMouseEvent(int code, IntPtr wParam, IntPtr lParam)
        {
            // Pass through invalid codes, pointer moves, and idle time untouched.
            var message = wParam.ToInt32();
            if (code < 0 || message == MouseMove || !TestCapturing())
            {
                return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            }

            // Read the window under the pointer now, before the click changes the screen.
            var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(lParam);

            // Queue the record; the wheel delta sits in the high word of the mouse data.
            AddRecord(new SapInputRecord
            {
                ForegroundWindow = SapDesktop.GetForegroundRootWindow(),
                Generation = _connectionState.SessionGeneration,
                Message = message,
                RootWindow = SapDesktop.GetRootWindow(data.Point.X, data.Point.Y),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                WheelDelta = unchecked((short)((data.MouseData >> 16) & 0xFFFF)),
                X = data.Point.X,
                Y = data.Point.Y
            });

            return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        // Tests whether input is captured now: a client listens and the user32 stream is enabled.
        private bool TestCapturing()
        {
            return _connectionState.CaptureActive && _optionsStore.Current.TestStream(SapRecordingStreams.User32);
        }

        // Thread body: installs both hooks, pumps messages until stopped, then removes the hooks.
        private void RunHooks(CancellationToken stoppingToken)
        {
            // Keep the delegates in fields, so the garbage collector never frees a callback Windows still calls.
            _keyboardProcedure = ReceiveKeyboardEvent;
            _mouseProcedure = ReceiveMouseEvent;
            var keyboardHook = InstallHook(NativeMethods.WhKeyboardLowLevel, _keyboardProcedure);
            var mouseHook = InstallHook(NativeMethods.WhMouseLowLevel, _mouseProcedure);

            // Report a missing hook once; the other hook, and the nativeSap stream, keep working.
            if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
            {
                var error = Marshal.GetLastPInvokeError();
                _logger.LogError("Failed to install the input hooks; the user32 stream is incomplete. Error: {Error}", error);
            }

            try
            {
                // Pump messages: low-level hooks are called while this thread retrieves messages.
                while (!stoppingToken.IsCancellationRequested)
                {
                    NativeMethods.MsgWaitForMultipleObjectsEx(
                        count: 0,
                        handles: [],
                        IdleWaitMilliseconds,
                        NativeMethods.QsAllInput,
                        NativeMethods.MwmoInputAvailable);
                    while (NativeMethods.PeekMessage(out var pending, IntPtr.Zero, 0, 0, NativeMethods.PmRemove))
                    {
                        NativeMethods.TranslateMessage(in pending);
                        NativeMethods.DispatchMessage(in pending);
                    }
                }
            }
            finally
            {
                // Remove the hooks on the thread that installed them.
                if (keyboardHook != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(keyboardHook);
                }

                if (mouseHook != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(mouseHook);
                }
            }
        }
        #endregion
    }
}
