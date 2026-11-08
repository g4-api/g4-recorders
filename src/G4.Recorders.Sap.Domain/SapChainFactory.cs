using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.Native;
using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Builds recorder chains and nodes from SAP GUI Scripting objects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A chain walks <c>Parent</c> from the trigger control up to its window and stops there, so session, connection,
    /// and application objects never appear. The path is returned root-first, matching UIA chains.
    /// </para>
    /// <para>
    /// Nodes never carry field contents: <c>Name</c> reads <c>Text</c> only for captions (buttons, labels, tabs,
    /// windows, and similar), and reads the tooltip for every other control. Password fields are never read.
    /// </para>
    /// <para>
    /// Every method runs on the scripting thread.
    /// </para>
    /// </remarks>
    internal static partial class SapChainFactory
    {
        #region *** Constants    ***
        // Names the framework every SAP node reports, so consumers can tell SAP nodes from UIA and DOM nodes.
        private const string FrameworkId = "SAPGUI";

        // Lists the types whose Text is a caption rather than user data, so it can serve as the node's display name.
        private static readonly HashSet<string> CaptionTypes = new(StringComparer.Ordinal)
        {
            "GuiButton",
            "GuiCheckBox",
            SapGuiTypes.FrameWindow,
            "GuiLabel",
            SapGuiTypes.MainWindow,
            "GuiMenu",
            SapGuiTypes.ModalWindow,
            "GuiRadioButton",
            "GuiTab",
            "GuiTitlebar"
        };

        // Lists the types where a chain stops: they sit above the windows and carry no screen position.
        private static readonly HashSet<string> RootTypes = new(StringComparer.Ordinal)
        {
            SapGuiTypes.Application,
            SapGuiTypes.Connection,
            SapGuiTypes.Session
        };

        // Lists the window types that end a chain as its top window.
        private static readonly HashSet<string> WindowTypes = new(StringComparer.Ordinal)
        {
            SapGuiTypes.FrameWindow,
            SapGuiTypes.MainWindow,
            SapGuiTypes.ModalWindow
        };
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Builds the chain for a control.
        /// </summary>
        /// <param name="component">The trigger control, or <c>null</c>.</param>
        /// <returns>The chain; an empty chain when the control is <c>null</c> or sits above the windows.</returns>
        internal static SapChainModel NewChain(ISapGuiObject component)
        {
            // Walk up from the trigger, collecting nodes until the window or a root type ends the chain.
            var nodes = new List<SapNodeModel>();
            ISapGuiObject topObject = null;
            var current = component;

            while (current is not null)
            {
                var type = SapGuiNavigator.GetString(current, propertyName: "Type");
                if (type.Length == 0 || RootTypes.Contains(type))
                {
                    break;
                }

                nodes.Add(NewNode(current, type));
                if (WindowTypes.Contains(type))
                {
                    topObject = current;
                    break;
                }

                current = SapGuiNavigator.GetObject(current, propertyName: "Parent");
            }

            // A control above the windows yields no chain.
            if (nodes.Count == 0)
            {
                return new SapChainModel();
            }

            // Mark the trigger, stamp the owning process on every node, and turn the path root-first.
            var trigger = nodes[0];
            trigger.IsTriggerElement = true;

            var processId = GetProcessId(topObject);
            nodes.ForEach(i => i.ProcessId = processId);
            nodes.Reverse();

            return new SapChainModel
            {
                Locator = NewLocator(trigger),
                Path = nodes,
                Point = NewCenterPoint(trigger.Bounds),
                TopWindow = topObject is null ? null : nodes[0]
            };
        }

        /// <summary>
        /// Computes the pointer's offset from the trigger control's top-left corner.
        /// </summary>
        /// <param name="chain">The chain whose trigger anchors the offset.</param>
        /// <param name="x">The horizontal pointer coordinate in physical pixels.</param>
        /// <param name="y">The vertical pointer coordinate in physical pixels.</param>
        /// <returns>The offset; zero when the trigger has no usable rectangle.</returns>
        internal static RecorderOffsetModel NewMouseOffset(SapChainModel chain, int x, int y)
        {
            // Anchor on the marked trigger, so ancestor geometry never shifts the recorded point.
            var trigger = chain?.Path?.FindLast(i => i.IsTriggerElement);
            var bounds = trigger?.Bounds;

            if (bounds is null || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return new RecorderOffsetModel();
            }

            return new RecorderOffsetModel
            {
                X = (int)Math.Round(x - bounds.Left, MidpointRounding.AwayFromZero),
                Y = (int)Math.Round(y - bounds.Top, MidpointRounding.AwayFromZero)
            };
        }

        /// <summary>
        /// Builds the node for one scripting object.
        /// </summary>
        /// <param name="component">The scripting object.</param>
        /// <param name="type">The object's SAP type, read once by the caller.</param>
        /// <returns>The node, with SAP values mapped into the shared node fields.</returns>
        internal static SapNodeModel NewNode(ISapGuiObject component, string type)
        {
            // Read identity first; only shells carry a meaningful subtype.
            var id = SapGuiNavigator.GetString(component, propertyName: "Id");
            var name = SapGuiNavigator.GetString(component, propertyName: "Name");
            var subType = type == SapGuiTypes.Shell
                ? SapGuiNavigator.GetString(component, propertyName: "SubType")
                : string.Empty;

            // Use captions as display names, and tooltips for controls whose text is user data.
            var displayName = CaptionTypes.Contains(type)
                ? SapGuiNavigator.GetString(component, propertyName: "Text")
                : SapGuiNavigator.GetString(component, propertyName: "Tooltip");

            return new SapNodeModel
            {
                AutomationId = name,
                Bounds = new RecorderNodeModel<object>.BoundsRectangle
                {
                    Height = SapGuiNavigator.GetLong(component, propertyName: "Height"),
                    Left = SapGuiNavigator.GetLong(component, propertyName: "ScreenLeft"),
                    Top = SapGuiNavigator.GetLong(component, propertyName: "ScreenTop"),
                    Width = SapGuiNavigator.GetLong(component, propertyName: "Width")
                },
                ClassName = subType.Length > 0 ? subType : type,
                ControlType = type,
                ControlTypeId = (int)SapGuiNavigator.GetLong(component, propertyName: "TypeAsNumber"),
                FrameworkId = FrameworkId,
                IsTopWindow = WindowTypes.Contains(type),
                Machine = new RecorderNodeModel<object>.MachineDataModel(),
                Name = type == SapGuiTypes.PasswordField ? string.Empty : displayName,
                Patterns = [],
                Properties = new Dictionary<string, object>
                {
                    ["ContainerType"] = SapGuiNavigator.GetBoolean(component, propertyName: "ContainerType"),
                    ["Id"] = id,
                    ["RelativeId"] = SessionPrefixPattern().Replace(id, string.Empty),
                    ["SubType"] = subType
                }
            };
        }

        // Reads the process that owns a window through its handle; zero when the window or handle is unavailable.
        private static int GetProcessId(ISapGuiObject window)
        {
            // Resolve the handle's owning process; an absent handle yields zero.
            var handle = window is null ? 0 : SapGuiNavigator.GetLong(window, propertyName: "Handle");
            if (handle == 0)
            {
                return 0;
            }

            NativeMethods.GetWindowThreadProcessId(new IntPtr(handle), out var processId);

            return (int)processId;
        }

        // Returns the center of a rectangle, rounded to whole pixels.
        private static RecorderPointModel NewCenterPoint(RecorderNodeModel<object>.BoundsRectangle bounds)
        {
            return new RecorderPointModel
            {
                XPos = (int)Math.Round(bounds.Left + (bounds.Width / 2), MidpointRounding.AwayFromZero),
                YPos = (int)Math.Round(bounds.Top + (bounds.Height / 2), MidpointRounding.AwayFromZero)
            };
        }

        // Builds the XPath the SAP driver resolves through findById: //Type[@id='/app/...'].
        // The identifier is quoted with apostrophes unless it contains one, in which case quotation marks are used.
        private static string NewLocator(SapNodeModel trigger)
        {
            // Read the absolute identifier recorded on the node; without it, no locator is possible.
            var id = trigger.Properties["Id"] as string ?? string.Empty;
            if (id.Length == 0)
            {
                return string.Empty;
            }

            var quote = id.Contains('\'') ? '"' : '\'';

            return $"//{trigger.ControlType}[@id={quote}{id}{quote}]";
        }

        // Matches the connection and session prefix of an absolute identifier, such as /app/con[0]/ses[0]/.
        [GeneratedRegex(@"^/app/con\[\d+\]/ses\[\d+\]/", RegexOptions.CultureInvariant)]
        private static partial Regex SessionPrefixPattern();
        #endregion
    }
}
