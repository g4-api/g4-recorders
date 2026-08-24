using System.Collections.Generic;

using UIAutomationClient;

namespace G4.Recorders.Uia.Domain.Models
{
    public static class Cache
    {
        /// <summary>
        /// Maps UI Automation control type IDs to their corresponding friendly names.
        /// </summary>
        public static Dictionary<int, string> ControlTypeNames => new()
        {
            { UIA_ControlTypeIds.UIA_AppBarControlTypeId, "AppBar" },
            { UIA_ControlTypeIds.UIA_ButtonControlTypeId, "Button" },
            { UIA_ControlTypeIds.UIA_CalendarControlTypeId, "Calendar" },
            { UIA_ControlTypeIds.UIA_CheckBoxControlTypeId, "CheckBox" },
            { UIA_ControlTypeIds.UIA_ComboBoxControlTypeId, "ComboBox" },
            { UIA_ControlTypeIds.UIA_CustomControlTypeId, "Custom" },
            { UIA_ControlTypeIds.UIA_DataGridControlTypeId, "DataGrid" },
            { UIA_ControlTypeIds.UIA_DataItemControlTypeId, "DataItem" },
            { UIA_ControlTypeIds.UIA_DocumentControlTypeId, "Document" },
            { UIA_ControlTypeIds.UIA_EditControlTypeId, "Edit" },
            { UIA_ControlTypeIds.UIA_GroupControlTypeId, "Group" },
            { UIA_ControlTypeIds.UIA_HeaderControlTypeId, "Header" },
            { UIA_ControlTypeIds.UIA_HeaderItemControlTypeId, "HeaderItem" },
            { UIA_ControlTypeIds.UIA_HyperlinkControlTypeId, "Hyperlink" },
            { UIA_ControlTypeIds.UIA_ImageControlTypeId, "Image" },
            { UIA_ControlTypeIds.UIA_ListControlTypeId, "List" },
            { UIA_ControlTypeIds.UIA_ListItemControlTypeId, "ListItem" },
            { UIA_ControlTypeIds.UIA_MenuControlTypeId, "Menu" },
            { UIA_ControlTypeIds.UIA_MenuBarControlTypeId, "MenuBar" },
            { UIA_ControlTypeIds.UIA_MenuItemControlTypeId, "MenuItem" },
            { UIA_ControlTypeIds.UIA_PaneControlTypeId, "Pane" },
            { UIA_ControlTypeIds.UIA_ProgressBarControlTypeId, "ProgressBar" },
            { UIA_ControlTypeIds.UIA_RadioButtonControlTypeId, "RadioButton" },
            { UIA_ControlTypeIds.UIA_ScrollBarControlTypeId, "ScrollBar" },
            { UIA_ControlTypeIds.UIA_SeparatorControlTypeId, "Separator" },
            { UIA_ControlTypeIds.UIA_SemanticZoomControlTypeId, "SemanticZoom" },
            { UIA_ControlTypeIds.UIA_SliderControlTypeId, "Slider" },
            { UIA_ControlTypeIds.UIA_SpinnerControlTypeId, "Spinner" },
            { UIA_ControlTypeIds.UIA_SplitButtonControlTypeId, "SplitButton" },
            { UIA_ControlTypeIds.UIA_StatusBarControlTypeId, "StatusBar" },
            { UIA_ControlTypeIds.UIA_TabControlTypeId, "Tab" },
            { UIA_ControlTypeIds.UIA_TabItemControlTypeId, "TabItem" },
            { UIA_ControlTypeIds.UIA_TableControlTypeId, "Table" },
            { UIA_ControlTypeIds.UIA_TextControlTypeId, "Text" },
            { UIA_ControlTypeIds.UIA_ThumbControlTypeId, "Thumb" },
            { UIA_ControlTypeIds.UIA_TitleBarControlTypeId, "TitleBar" },
            { UIA_ControlTypeIds.UIA_ToolBarControlTypeId, "ToolBar" },
            { UIA_ControlTypeIds.UIA_ToolTipControlTypeId, "ToolTip" },
            { UIA_ControlTypeIds.UIA_TreeControlTypeId, "Tree" },
            { UIA_ControlTypeIds.UIA_TreeItemControlTypeId, "TreeItem" },
            { UIA_ControlTypeIds.UIA_WindowControlTypeId, "Window" },
        };

    }
}
