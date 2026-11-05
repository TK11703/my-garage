using Microsoft.AspNetCore.Http;
using MyGarage.Shared.Vehicles;
using MyGarage.Shared.Vehicles.StickerImport;

namespace MyGarage.Web.Vehicles.StickerImport;

// One feature proposed from the sticker: shown with a checkbox (selected by default) and, on an edit, the existing features it replaces.
// The rows post back as plain fields (not through the model binder) so the page stays stateless between the import and the save.
public sealed class StickerFeatureChoice
{
    private const int MaxRows = StickerImportLimits.MaxFeaturesPerVehicle;

    public string Name { get; set; } = string.Empty;

    public bool Include { get; set; } = true;

    public List<Guid> ReplacesTagIds { get; set; } = [];

    public List<string> ReplacesNames { get; set; } = [];

    public static StickerFeatureChoice From(StickerFeatureDto feature) =>
        new() { Name = feature.Name, ReplacesTagIds = [.. feature.ReplacesTagIds], ReplacesNames = [.. feature.ReplacesNames] };

    public static string NameField(int index) => $"sticker-feature-name-{index}";

    public static string IncludeField(int index) => $"sticker-feature-include-{index}";

    public static string ReplacesIdField(int index) => $"sticker-feature-replaces-id-{index}";

    public static string ReplacesNameField(int index) => $"sticker-feature-replaces-name-{index}";

    // Rebuilds the rows from a posted form; an unticked row has no include field.
    public static List<StickerFeatureChoice> ReadFrom(IFormCollection form)
    {
        var rows = new List<StickerFeatureChoice>();
        for (var index = 0; index < MaxRows && form.TryGetValue(NameField(index), out var name); index++)
        {
            rows.Add(new StickerFeatureChoice
            {
                Name = name.ToString(),
                Include = form.ContainsKey(IncludeField(index)),
                ReplacesTagIds = [.. form[ReplacesIdField(index)].Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty)],
                ReplacesNames = [.. form[ReplacesNameField(index)].Where(value => !string.IsNullOrEmpty(value)).Select(value => value!)],
            });
        }

        return rows;
    }

    public StickerFeatureRequest ToRequest() => new() { Name = Name, ReplacesTagIds = ReplacesTagIds };
}
