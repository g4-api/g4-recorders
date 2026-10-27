using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using G4.Recorders.Sap.Domain.Native;

namespace G4.Recorders.Sap.Domain.Scripting
{
    /// <summary>
    /// Runs queued work on one single-threaded apartment (STA) thread that also pumps window messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SAP GUI Scripting objects are apartment-threaded COM objects hosted by <c>saplogon.exe</c>. Every scripting
    /// call made by the recorder runs here, so all references stay in one apartment and calls never interleave.
    /// </para>
    /// <para>
    /// The thread pumps messages between work items because SAP GUI raises session events, such as <c>Change</c>,
    /// as incoming COM calls; an STA thread receives them only while it dispatches window messages. Event handlers
    /// therefore run on this thread too, between work items or during an outgoing call.
    /// </para>
    /// <para>
    /// Disposal stops the loop and waits a bounded time for the thread to exit. A thread stuck in a hung COM call is
    /// abandoned; it is a background thread and ends with the process.
    /// </para>
    /// </remarks>
    internal sealed class SapComThread : IDisposable
    {
        #region *** Constants    ***
        // Bounds how long disposal waits for the thread to leave its loop.
        private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(5);

        // Caps one idle wait, so the loop rechecks its stop flag even when no message or work arrives.
        private const uint IdleWaitMilliseconds = 250;
        #endregion

        #region *** Fields       ***
        // Holds pending work items in submission order.
        private readonly ConcurrentQueue<Action> _queue = new();

        // Wakes the loop when work is queued or disposal starts.
        private readonly AutoResetEvent _signal = new(initialState: false);

        // Owns every scripting reference and receives every SAP event.
        private readonly Thread _thread;

        // Records disposal so late callers receive ObjectDisposedException instead of a lost work item.
        private int _isDisposed;
        #endregion

        #region *** Constructors ***
        /// <summary>
        /// Initializes a new instance of the <see cref="SapComThread"/> class and starts its STA thread.
        /// </summary>
        /// <param name="threadName">The thread name shown in debuggers and dumps.</param>
        internal SapComThread(string threadName)
        {
            // Start a background STA thread so a hung SAP call never keeps the process alive.
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = threadName
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets a value indicating whether the calling thread is the scripting thread.
        /// </summary>
        internal bool IsCurrentThread => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public void Dispose()
        {
            // Allow only the first caller to stop the loop.
            if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            {
                return;
            }

            // Wake the loop so it sees the stop flag, then wait a bounded time for it to exit.
            _signal.Set();
            var isJoined = IsCurrentThread || _thread.Join(JoinTimeout);

            // Release the wake handle only when no thread can still wait on it.
            if (isJoined)
            {
                _signal.Dispose();
            }
        }

        /// <summary>
        /// Runs an operation on the scripting thread and waits for its result.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="operation">The operation; it may call SAP GUI Scripting.</param>
        /// <param name="timeout">How long the caller waits; the operation keeps running after a timeout.</param>
        /// <returns>The operation's result; its exception is rethrown unwrapped.</returns>
        internal T Invoke<T>(Func<T> operation, TimeSpan timeout)
        {
            // Require the function and reject work after shutdown.
            ArgumentNullException.ThrowIfNull(argument: operation);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) == 1, this);

            // Run nested calls inline; queueing them deadlocks the thread waiting on itself.
            if (IsCurrentThread)
            {
                return operation();
            }

            // Queue the work with a completion source that carries its result or exception back to the caller.
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                try
                {
                    completion.TrySetResult(operation());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });

            try
            {
                // Block the caller until the scripting thread finishes; the original exception is rethrown unwrapped.
                return completion.Task.WaitAsync(timeout).GetAwaiter().GetResult();
            }
            catch (TimeoutException exception)
            {
                // Name the budget in the message; the work item keeps running and its outcome is discarded.
                var message = $"The SAP GUI Scripting operation did not complete within {timeout.TotalMilliseconds} ms.";
                throw new TimeoutException(message, exception);
            }
        }

        /// <summary>
        /// Queues an operation on the scripting thread without waiting for it.
        /// </summary>
        /// <param name="operation">The operation; an exception it throws is discarded, so it handles its own failures.</param>
        internal void Post(Action operation)
        {
            // Require the action and reject work after shutdown.
            ArgumentNullException.ThrowIfNull(argument: operation);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) == 1, this);

            // Queue the work, then wake the loop.
            _queue.Enqueue(operation);
            _signal.Set();
        }

        // Dispatches every queued window message, which delivers incoming COM calls such as SAP events.
        private static void DispatchMessages()
        {
            // Drain the queue without blocking; WM_QUIT is ignored because the loop stops through its own flag.
            while (NativeMethods.PeekMessage(out var message, IntPtr.Zero, 0, 0, NativeMethods.PmRemove))
            {
                NativeMethods.TranslateMessage(in message);
                NativeMethods.DispatchMessage(in message);
            }
        }

        // Thread body: waits for work or messages, pumps messages, then runs queued work, until disposal.
        // Work items capture their own exceptions; a stray one is discarded so the thread keeps serving SAP events.
        private void RunLoop()
        {
            // Wait on the wake handle and on the message queue together, so events and work both end the wait.
            var handles = new[] { _signal.SafeWaitHandle.DangerousGetHandle() };

            while (Volatile.Read(ref _isDisposed) == 0)
            {
                NativeMethods.MsgWaitForMultipleObjectsEx(
                    count: 1,
                    handles,
                    IdleWaitMilliseconds,
                    NativeMethods.QsAllInput,
                    NativeMethods.MwmoInputAvailable);

                // Deliver pending messages first, so queued work sees the latest SAP state.
                DispatchMessages();

                // Run the work queued so far; work queued meanwhile wakes the next wait immediately.
                while (_queue.TryDequeue(out var workItem))
                {
                    InvokeWorkItem(workItem);
                }
            }
        }

        // Runs one work item and discards its exception, so a failing item never ends the scripting thread.
        private static void InvokeWorkItem(Action workItem)
        {
            try
            {
                workItem();
            }
            catch (Exception)
            {
                // Posted work owns its failure handling; anything that escapes is dropped by design.
            }
        }
        #endregion
    }
}
