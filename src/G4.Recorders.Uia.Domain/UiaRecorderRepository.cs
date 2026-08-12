using G4.Recorders.Common.Domain.Models;

using G4.Recorders.Uia.Domain.Extensions;
using G4.Recorders.Uia.Domain.Middlewares;
using G4.Recorders.Uia.Domain.Models;

using UIAutomationClient;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace G4.Recorders.Uia.Domain
{
    /// <summary>
    /// Represents a repository for accessing UI Automation elements and their ancestor chains, and the OS-level
    /// screenshot, pointer, and window-focus primitives that support grounding a coordinate against them.
    /// </summary>
    /// <param name="cursorPositionProvider">
    /// Reads back the physical cursor position after <see cref="MovePointer"/> settles it, reusing the same
    /// physical-cursor P/Invoke this repository's own current-pointer peek path already depends on instead of
    /// duplicating it.
    /// </param>
    /// <param name="identityAttributes">
    /// The ordered identity attributes (primary first) used for locator generation, typically bound from the
    /// <c>G4:Uia:IdentityAttributes</c> configuration. A null or empty value applies the extension defaults.
    /// </param>
    public class UiaRecorderRepository(
        IUiaCursorPositionProvider cursorPositionProvider,
        IReadOnlyList<string> identityAttributes) : IUiaRecorderRepository
    {
        #region *** Constants    ***
        // Restores a minimized window before a foreground-focus attempt, matching Win32's SW_RESTORE value.
        private const int ShowWindowRestore = 9;
        #endregion

        #region *** Fields       ***
        // Reads the settled physical cursor position after MovePointer, shared with the current-pointer peek path.
        private readonly IUiaCursorPositionProvider _cursorPositionProvider = cursorPositionProvider;

        // The ordered identity attributes used to build UIA locators; null lets the extension apply its defaults.
        private readonly IReadOnlyList<string> _identityAttributes = identityAttributes;
        #endregion

        #region *** Constructors ***
        /// <summary>
        /// Initializes a new instance that builds UIA locators with the default identity attributes.
        /// </summary>
        /// <param name="cursorPositionProvider">
        /// Reads back the physical cursor position after <see cref="MovePointer"/> settles it, reusing the same
        /// physical-cursor P/Invoke this repository's own current-pointer peek path already depends on instead of
        /// duplicating it.
        /// </param>
        public UiaRecorderRepository(IUiaCursorPositionProvider cursorPositionProvider)
            : this(cursorPositionProvider, identityAttributes: null)
        { }
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public RecorderScreenshotModel GetScreenshot(bool metricsOnly)
        {
            // Enable per-monitor DPI awareness before reading virtual-screen bounds so multi-monitor origins and
            // dimensions are accurate. Idempotent - returns false without throwing if already set by the host.
            Application.SetHighDpiMode(highDpiMode: HighDpiMode.PerMonitorV2);

            // Read the virtual-desktop bounds first, since both the metrics-only and full-capture paths need them.
            var virtualScreen = SystemInformation.VirtualScreen;
            var screenshot = new RecorderScreenshotModel
            {
                Height = virtualScreen.Height,
                OriginX = virtualScreen.X,
                OriginY = virtualScreen.Y,
                Width = virtualScreen.Width
            };

            // Skip the actual pixel capture entirely when the caller only needs coordinate-space metrics.
            if (metricsOnly)
            {
                return screenshot;
            }

            // Capture the full virtual desktop into an in-memory bitmap sized to its reported bounds, disposing the
            // bitmap and graphics context as soon as the pixels are encoded.
            using var bitmap = new Bitmap(width: virtualScreen.Width, height: virtualScreen.Height, format: PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.CopyFromScreen(
                sourceX: virtualScreen.X,
                sourceY: virtualScreen.Y,
                destinationX: 0,
                destinationY: 0,
                blockRegionSize: virtualScreen.Size);

            // Encode losslessly and inline as base64, because a remote caller cannot reach a path on this
            // recorder's own disk the way a local script could.
            using var pngStream = new MemoryStream();
            bitmap.Save(stream: pngStream, format: ImageFormat.Png);
            screenshot.ImageBase64 = Convert.ToBase64String(pngStream.ToArray());

            return screenshot;
        }

        /// <inheritdoc />
        public RecorderPointModel MovePointer(int x, int y)
        {
            // Move the physical cursor so downstream hover-based UIA state reflects the requested screen position.
            SetPhysicalCursorPos(x, y);

            // Read the settled position back through the existing cursor provider rather than duplicating its
            // physical-cursor P/Invoke declaration; fall back to the requested point if the read-back fails.
            return _cursorPositionProvider.GetCurrent(out var settledPoint)
                ? settledPoint
                : new RecorderPointModel { XPos = x, YPos = y };
        }

        /// <inheritdoc />
        public UiaChainModel GetElementChain()
        {
            // Create a new instance of the UI Automation engine.
            var automation = new CUIAutomation8();

            // Get the element that currently has keyboard focus.
            var element = automation.GetFocusedElement();

            // Build the ancestor chain for the focused element,
            // or return a new empty model if no element was found.
            var chain = automation.NewAncestorChain(element, _identityAttributes) ?? new UiaChainModel();

            // Keep the canonical absolute path as the executable locator because removing ancestors can
            // reintroduce ambiguity when identical branches exist elsewhere below the same window.
            chain.FallbackLocator = chain.ResolveLocator(_identityAttributes);
            chain.Locator = chain.FallbackLocator;

            // Indicate that this chain was triggered by a focus action.
            chain.Trigger = "Focus";

            // Return the ancestor chain model.
            return chain;
        }

        /// <inheritdoc />
        public UiaChainModel GetElementChain(int x, int y)
        {
            // Initialize the UI Automation engine.
            var automation = new CUIAutomation8();

            // Get the element directly under the given screen coordinates.
            var element = automation.ElementFromPoint(pt: new tagPOINT { x = x, y = y });

            // If an element was found, build and return its ancestor chain; otherwise return null.
            var chain = automation.NewAncestorChain(element, _identityAttributes) ?? new UiaChainModel();

            // Set the point information in the chain model if it exists.
            chain.Point = new RecorderPointModel { XPos = x, YPos = y };

            // Keep the canonical absolute path as the executable locator because removing ancestors can
            // reintroduce ambiguity when identical branches exist elsewhere below the same window.
            chain.FallbackLocator = chain.ResolveLocator(_identityAttributes);
            chain.Locator = chain.FallbackLocator;

            // Indicate that this chain was triggered by a hover action.
            chain.Trigger = "Hover";

            // Return the constructed ancestor chain.
            return chain;
        }

        /// <inheritdoc />
        public UiaChainModel ResolveGroundedElement(int x, int y, bool skipOffset)
        {
            // Physically settle the cursor first so hover-based UIA state matches the chain this call returns.
            MovePointer(x, y);

            // Reuse the existing coordinate lookup path so chain construction stays identical to a direct call.
            var chain = GetElementChain(x, y);

            // Skip the offset computation entirely when the caller only needs the chain itself.
            if (skipOffset)
            {
                return chain;
            }

            // Resolve the pointer's pixel offset from the trigger element's top-left corner using the exact math
            // already used for live mouse-event recording, so grounding and recorded clicks agree on offset semantics.
            chain.Offset = MouseTargetResolver.ResolveOffset(chain, x, y);

            return chain;
        }

        /// <inheritdoc />
        public RecorderWindowFocusModel SetWindowFocus(string windowTitle, string processName)
        {
            // Require at least one identifier before touching any process or window handle.
            var hasWindowTitle = !string.IsNullOrWhiteSpace(windowTitle);
            var hasProcessName = !string.IsNullOrWhiteSpace(processName);

            if (!hasWindowTitle && !hasProcessName)
            {
                var message = "SetWindowFocus requires windowTitle and/or processName.";
                throw new ArgumentException(message, nameof(windowTitle));
            }

            // Restrict candidates to processes exposing a real top-level window before applying caller filters.
            var candidateProcesses = Process
                .GetProcesses()
                .Where(process => process.MainWindowHandle != IntPtr.Zero);

            // Apply each supplied identifier independently so an omitted one never narrows the candidate set.
            if (hasWindowTitle)
            {
                candidateProcesses = candidateProcesses.Where(process =>
                    process.MainWindowTitle.Contains(windowTitle, StringComparison.OrdinalIgnoreCase));
            }

            if (hasProcessName)
            {
                candidateProcesses = candidateProcesses.Where(process =>
                    string.Equals(process.ProcessName, processName, StringComparison.Ordinal));
            }

            var matchedProcesses = candidateProcesses.ToArray();

            // Treat zero or more than one match as "not found" rather than guessing which process the caller meant -
            // this mirrors the same packaged/UWP dual-process ambiguity already handled on the client-side guard.
            if (matchedProcesses.Length != 1)
            {
                return new RecorderWindowFocusModel();
            }

            var targetProcess = matchedProcesses[0];
            var isFocused = false;

            // Attempt the restore-and-focus sequence defensively because the target process can exit between
            // resolution and this point.
            try
            {
                if (IsIconic(windowHandle: targetProcess.MainWindowHandle))
                {
                    ShowWindow(windowHandle: targetProcess.MainWindowHandle, commandShow: ShowWindowRestore);
                }

                SetForegroundWindow(windowHandle: targetProcess.MainWindowHandle);

                // Verify independently rather than trusting SetForegroundWindow's own return value, because Windows
                // can silently deny a foreground-focus request depending on which process currently owns it.
                isFocused = GetForegroundWindow() == targetProcess.MainWindowHandle;
            }
            catch (Exception)
            {
                isFocused = false;
            }

            return new RecorderWindowFocusModel
            {
                Found = true,
                Focused = isFocused,
                ProcessId = targetProcess.Id,
                ProcessName = targetProcess.ProcessName,
                WindowTitle = targetProcess.MainWindowTitle
            };
        }

        // Imports the Windows foreground-window query used to verify a focus attempt independently of
        // SetForegroundWindow's own, sometimes-unreliable, return value.
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        // Imports the Windows minimized-state query consulted before a focus attempt restores the target window.
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr windowHandle);

        // Imports the Windows foreground-focus request used by SetWindowFocus. Windows can silently deny this
        // depending on which process currently owns the foreground lock, which is why the caller verifies the
        // outcome through GetForegroundWindow rather than this call's own return value.
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        // Imports the Windows physical-cursor placement API used by MovePointer. Physical, not DPI-virtualized,
        // coordinates keep this consistent with IUiaCursorPositionProvider's own GetPhysicalCursorPos convention.
        [DllImport("user32.dll")]
        private static extern bool SetPhysicalCursorPos(int x, int y);

        // Imports the Windows window-state command used to restore a minimized window before it is focused.
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr windowHandle, int commandShow);
        #endregion
    }
}
