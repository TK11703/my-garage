using MyGarage.Web.Common;

namespace MyGarage.Web.Vehicles;

public static class VehicleTabs
{
    public static readonly (string Id, string Label, IconName Icon)[] All =
    [
        ("details", "Details", IconName.InfoCircle),
        ("media", "Media", IconName.Image),
        ("description", "Description", IconName.FileText),
        ("service-records", "Maintenance", IconName.Wrench),
    ];

    // Anything that is not a known tab means the first one.
    public static string Normalize(string? tab) => All.Any(known => known.Id == tab) ? tab! : "details";

    public const string ServiceRecords = "service-records";

    // The window-sticker shortcut is the first tab of the add and edit forms (feature 010).
    public const string Import = "import";

    public static readonly (string Id, string Label, IconName Icon) ImportTab = (Import, "Import from sticker", IconName.CloudUpload);

    // The edit form has no service records pane; they are managed on their own pages.
    public static readonly (string Id, string Label, IconName Icon)[] Edit = [.. All.Where(tab => tab.Id != ServiceRecords)];

    // Details is the default tab of the edit form even though Import from sticker is listed first.
    public static string NormalizeForEdit(string? tab) => tab == Import || Edit.Any(known => known.Id == tab) ? tab! : "details";

    // The tabs of the add or edit form: the import tab first when the form offers it.
    public static IReadOnlyList<(string Id, string Label, IconName Icon)> ForForm(bool withImport, bool withAttachments)
    {
        IEnumerable<(string Id, string Label, IconName Icon)> tabs = withAttachments ? Edit : Edit.Take(1);
        return withImport ? [ImportTab, .. tabs] : [.. tabs];
    }
}
