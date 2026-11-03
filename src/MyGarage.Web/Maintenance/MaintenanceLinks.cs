namespace MyGarage.Web.Maintenance;

// Which accordion item of the Maintenance tab starts open.
public enum MaintenanceSection
{
    Records,
    Fuel,
    Compliance,
    Documents,
}

// Each list on the Maintenance tab pages on its own query value.
public enum MaintenanceList
{
    Records,
    Fuel,
    Inspections,
    Registrations,
    Insurance,
    Taxes,
    Documents,
}

public static class MaintenanceLinks
{
    // The default section and page 1 are left out so addresses stay tidy.
    public static string TabHref(Guid vehicleId, MaintenanceSection section = MaintenanceSection.Records, MaintenanceList? list = null, int page = 1)
    {
        var href = $"/vehicles/{vehicleId}?tab=service-records";
        if (section != MaintenanceSection.Records)
        {
            href += $"&section={SectionValue(section)}";
        }

        if (list is { } pagedList && page > 1)
        {
            href += $"&{PageParameter(pagedList)}={page}";
        }

        return href;
    }

    // Anything unrecognized means the default section.
    public static MaintenanceSection ParseSection(string? value) =>
        value switch
        {
            "fuel" => MaintenanceSection.Fuel,
            "compliance" => MaintenanceSection.Compliance,
            "documents" => MaintenanceSection.Documents,
            _ => MaintenanceSection.Records,
        };

    // Read as text so a bad query value means the first page rather than a binding error.
    public static int ParsePage(string? value) => int.TryParse(value, out var page) ? Math.Max(1, page) : 1;

    private static string SectionValue(MaintenanceSection section) =>
        section switch
        {
            MaintenanceSection.Fuel => "fuel",
            MaintenanceSection.Compliance => "compliance",
            MaintenanceSection.Documents => "documents",
            _ => "records",
        };

    private static string PageParameter(MaintenanceList list) =>
        list switch
        {
            MaintenanceList.Records => "recordsPage",
            MaintenanceList.Fuel => "fuelPage",
            MaintenanceList.Inspections => "inspectionsPage",
            MaintenanceList.Registrations => "registrationsPage",
            MaintenanceList.Insurance => "insurancePage",
            MaintenanceList.Documents => "documentsPage",
            _ => "taxesPage",
        };
}
