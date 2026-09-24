using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Markup;

namespace MiniDesk.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly ResourceManager Manager = new("MiniDesk.Resources.Strings", typeof(LocalizationService).Assembly);
    private static readonly CultureInfo SystemFormatCulture = CultureInfo.CurrentCulture;
    private static readonly string[] ResourceKeys =
    [
        "Nav_Home", "Nav_Groups", "Nav_Snapshots", "Nav_Appearance", "Nav_Quick", "Nav_About",
        "Home_Title", "Home_Subtitle", "Home_Start", "Home_Organize", "Home_OrganizeDesc", "Home_Collapse", "Home_CollapseDesc", "Home_Snapshot", "Home_SnapshotDesc", "Home_Appearance", "Home_AppearanceDesc",
        "Groups_Title", "Groups_Subtitle", "Groups_New", "Groups_Name", "Groups_Items", "Groups_State", "Groups_AutoCollapse", "Groups_Actions", "Groups_Rename", "Groups_Delete",
        "State_Expanded", "State_Collapsed", "State_IconOnly", "Snapshots_Title", "Snapshots_Subtitle", "Snapshots_Create", "Snapshots_Manual", "Snapshots_Restore", "Snapshots_Preview", "Snapshots_Delete",
        "Appearance_Title", "Appearance_Subtitle", "Appearance_Theme", "Theme_System", "Theme_SystemDesc", "Theme_Light", "Theme_LightDesc", "Theme_Dark", "Theme_DarkDesc", "Appearance_CollapseStyle", "Collapse_Capsule", "Collapse_Vertical", "Collapse_IconOnly", "Appearance_Parameters", "Appearance_Opacity", "Appearance_Radius", "Appearance_HeaderHeight", "Appearance_ShowIcon", "Appearance_ShowBadge", "Appearance_AttachDesktop", "Appearance_Preview", "Appearance_Documents", "Appearance_Pictures", "Appearance_Development",
        "Appearance_ThemeHint", "Appearance_PreviewHint", "Appearance_CollapseHint", "Appearance_Monitors", "Appearance_MonitorsHint", "Appearance_DetectMonitors", "Appearance_RememberMonitorLayout", "Appearance_MoveToPrimary", "Appearance_RestoreOnReconnect", "Appearance_ParametersHint", "Appearance_LanguageRegion", "Appearance_LanguageRegionHint", "Appearance_FollowSystem", "Appearance_RuntimeTitle", "Appearance_RuntimeDesc", "Preview_Expanded", "Preview_Collapsed", "Preview_IconOnly",
        "Quick_Title", "Quick_Subtitle", "Quick_ExpandAll", "Quick_ExpandAllDesc", "Quick_CollapseAll", "Quick_CollapseAllDesc", "Quick_OpenConfig", "Quick_OpenConfigDesc", "Quick_StartupRecovery", "Quick_Startup", "Quick_StartupDesc", "Quick_ExplorerRecovery", "Quick_ExplorerRecoveryDesc",
        "About_Tagline", "About_Language", "Language_ZhCN", "Language_EnUS", "Sidebar_Line1", "Sidebar_Line2", "Appearance_Language", "Appearance_RegionFormat",
        "Common_Cancel", "Common_Confirm", "Common_Close", "Common_Rename", "Common_Delete", "Common_Settings",
        "Group_Collapse", "Group_EmptyHint", "Group_RemoveItem", "Group_Expand", "Group_CollapseCapsule", "Group_CollapseVertical", "Group_CollapseIcon", "Group_Delete",
        "Create_Title", "Create_Subtitle", "Create_Name", "Create_NamePlaceholder", "Create_SelectIcon", "Create_SystemIcons", "Create_UploadIcon", "Create_UploadTitle", "Create_UploadFormats", "Create_UploadDrop", "Create_ChooseFile", "Create_Preview", "Create_PreviewHint", "Create_DefaultState", "Create_ShowDesktop", "Create_ShowDesktopHint", "Create_Action",
        "Add_Title", "Add_SelectGroup", "Add_CreateAndAdd", "SnapshotPreview_Title", "SnapshotPreview_Hint", "Tray_Open", "Tray_NewGroup", "Tray_Exit", "Tray_Tooltip",
        "Message_DeleteGroupTitle", "Message_DeleteGroup", "Message_DeleteBlocked", "Message_DeleteBlockedTitle", "Message_RestoreBlocked", "Message_RestoreBlockedTitle", "Message_IconSaveFailed", "Message_ItemMissing", "Message_AddFailed", "Message_OpenFailed", "Message_RemoveFailed", "Message_RemoveFailedTitle", "Message_StartupFailed",
        "Prompt_RenameGroup", "Prompt_GroupName", "Prompt_CreateSnapshot", "Prompt_SnapshotName", "Prompt_DefaultSnapshot", "Prompt_RestoreSnapshot", "Prompt_RestoreSnapshotTitle", "Prompt_DeleteSnapshot", "Prompt_DeleteSnapshotTitle", "Snapshot_BeforeDelete", "Snapshot_BeforeRestore",
        "Default_NewGroup", "Default_Music", "Default_Development", "Default_Communication", "Default_Office", "Default_TemporaryFiles",
        "Icon_Browser", "Icon_Game", "Icon_Picture", "Icon_Document", "Icon_Download", "Icon_Favorite", "Icon_Custom",
        "Upload_SelectTitle", "Upload_Filter", "Upload_PreviewFailed", "Snapshot_Contains", "Snapshot_GroupsSuffix", "Snapshot_IconsSuffix", "Snapshot_IconCountFormat",
        "Shell_Add", "Shell_NewGroup", "Error_IconMissing", "Error_IconFormat", "Error_IconSize", "Error_IconCorrupt", "Error_ExecutableMissing", "Default_Snapshot"
    ];

    public static LocalizationService Current { get; } = new();
    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");
    public CultureInfo FormatCulture { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CultureChanged;

    public string this[string key] => Get(key);
    public string Get(string key) => Manager.GetString(key, Culture) ?? key;
    public string Format(string key, params object?[] args) => string.Format(FormatCulture, Get(key), args);

    public void SetCulture(string? name)
    {
        var supported = string.Equals(name, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";
        Culture = CultureInfo.GetCultureInfo(supported);
        FormatCulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        if (Application.Current is { } app)
        {
            app.Resources["CurrentLanguage"] = XmlLanguage.GetLanguage(Culture.IetfLanguageTag);
            foreach (var key in ResourceKeys) app.Resources[key] = Get(key);
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetRegionCulture(string? name)
    {
        FormatCulture = string.Equals(name, "system", StringComparison.OrdinalIgnoreCase)
            ? SystemFormatCulture
            : CultureInfo.GetCultureInfo(name is "en-US" ? "en-US" : "zh-CN");
        CultureInfo.CurrentCulture = FormatCulture;
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = FormatCulture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        if (Application.Current is { } app)
            app.Resources["CurrentLanguage"] = XmlLanguage.GetLanguage(FormatCulture.IetfLanguageTag);
    }
}
