using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.Native;
using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Default <see cref="ISapRecorderRepository"/>: resolves SAP controls through the scripting host, and provides the
    /// desktop primitives (screenshot, pointer, window focus) the UIA recorder provides.
    /// </summary>
    /// <remarks>
    /// The desktop is read on the calling thread, then the SAP lookup runs on the scripting thread with the window
    /// handle it needs. Results are COM-free chains. The process is per-monitor DPI aware, so every coordinate is in
    /// physical desktop pixels, the same space SAP GUI Scripting uses.
    /// </remarks>
    /// <param name="scriptingHost">The host that owns the SAP GUI Scripting attachment.</param>
    /// <param name="cursorPositionProvider">Reads the cursor back after a pointer move settles it.</param>
    public sealed class SapRecorderRepository(
        ISapScriptingHost scriptingHost,
        ISapCursorPositionProvider cursorPositionProvider) : ISapRecorderRepository
    {
        #region *** Fields       ***
        // Reads the settled cursor position after a pointer move.
        private readonly ISapCursorPositionProvider _cursorPositionProvider = cursorPositionProvider;

        // Runs every SAP lookup on the scripting thread.
        private readonly ISapScriptingHost _scriptingHost = scriptingHost;
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public SapChainModel GetElementChain()
        {
            // Read the foreground window here, then resolve its SAP focus on the scripting thread.
            var foregroundWindow = SapDesktop.GetForegroundRootWindow();

            return _scriptingHost.Invoke(application => SapChainFactory.NewChain(
                SapGuiNavigator.GetFocused(application, foregroundWindow)));
        }

        /// <inheritdoc />
        public SapChainModel GetElementChain(int x, int y)
        {
            // Read the window at the point here, then hit-test its session on the scripting thread.
            var rootWindow = SapDesktop.GetRootWindow(x, y);

            return _scriptingHost.Invoke(application => SapChainFactory.NewChain(
                SapGuiNavigator.FindByPosition(application, rootWindow, x, y)));
        }

        /// <inheritdoc />
        public RecorderScreenshotModel GetScreenshot(bool metricsOnly)
        {
            // Describe the virtual desktop; the manifest's per-monitor awareness keeps these in physical pixels.
            var virtualScreen = SystemInformation.VirtualScreen;
            var screenshot = new RecorderScreenshotModel
            {
                Height = virtualScreen.Height,
                MimeType = "image/png",
                OriginX = virtualScreen.X,
                OriginY = virtualScreen.Y,
                Width = virtualScreen.Width
            };

            // Skip the pixel capture when the caller only needs the coordinate space.
            if (metricsOnly)
            {
                return screenshot;
            }

            // Capture the whole virtual desktop and encode it losslessly inline, because remote callers cannot read a
            // file on the recorder host.
            using var bitmap = new Bitmap(virtualScreen.Width, virtualScreen.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(virtualScreen.X, virtualScreen.Y, destinationX: 0, destinationY: 0, virtualScreen.Size);
            }

            using var pngStream = new MemoryStream();
            bitmap.Save(pngStream, ImageFormat.Png);
            screenshot.ImageBase64 = Convert.ToBase64String(pngStream.ToArray());

            return screenshot;
        }

        /// <inheritdoc />
        public RecorderPointModel MovePointer(int x, int y)
        {
            // Move the physical cursor, then read back where it settled; fall back to the request when the read fails.
            NativeMethods.SetPhysicalCursorPos(x, y);

            return _cursorPositionProvider.GetCurrent(out var settledPoint)
                ? settledPoint
                : new RecorderPointModel { XPos = x, YPos = y };
        }

        /// <inheritdoc />
        public SapChainModel ResolveGroundedElement(int x, int y, bool skipOffset)
        {
            // Settle the cursor first, then resolve where it actually is, so clamping cannot separate pointer and chain.
            var settledPoint = MovePointer(x, y);
            var chain = GetElementChain(settledPoint.XPos, settledPoint.YPos);

            // Add the pointer's offset from the control, with the same math recorded clicks use, unless skipped.
            if (!skipOffset)
            {
                chain.MouseOffset = SapChainFactory.NewMouseOffset(chain, settledPoint.XPos, settledPoint.YPos);
            }

            return chain;
        }

        /// <inheritdoc />
        public RecorderWindowFocusModel SetWindowFocus(string windowTitle, string processName)
        {
            // Require at least one identifier before touching any process.
            var hasWindowTitle = !string.IsNullOrWhiteSpace(windowTitle);
            var hasProcessName = !string.IsNullOrWhiteSpace(processName);
            if (!hasWindowTitle && !hasProcessName)
            {
                throw new ArgumentException("SetWindowFocus requires windowTitle and/or processName.", nameof(windowTitle));
            }

            // Match processes with a top-level window; each supplied identifier narrows the set, and both must match.
            var matchedProcesses = Process
                .GetProcesses()
                .Where(i => i.MainWindowHandle != IntPtr.Zero)
                .Where(i => !hasWindowTitle || i.MainWindowTitle.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
                .Where(i => !hasProcessName || string.Equals(i.ProcessName, processName, StringComparison.Ordinal))
                .ToArray();

            // Report no match and several matches alike, rather than guessing which window the caller meant.
            if (matchedProcesses.Length != 1)
            {
                return new RecorderWindowFocusModel();
            }

            var target = matchedProcesses[0];

            return new RecorderWindowFocusModel
            {
                Focused = SetForeground(target.MainWindowHandle),
                Found = true,
                ProcessId = target.Id,
                ProcessName = target.ProcessName,
                WindowTitle = target.MainWindowTitle
            };
        }

        // Restores and focuses a window, verifying the outcome independently, because Windows can refuse silently.
        private static bool SetForeground(IntPtr window)
        {
            try
            {
                // Restore a minimized window first; a minimized window cannot hold the foreground.
                if (NativeMethods.IsIconic(window))
                {
                    NativeMethods.ShowWindow(window, NativeMethods.SwRestore);
                }

                NativeMethods.SetForegroundWindow(window);

                return NativeMethods.GetForegroundWindow() == window;
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process exited between matching and focusing.
                return false;
            }
        }
        #endregion
    }
}
