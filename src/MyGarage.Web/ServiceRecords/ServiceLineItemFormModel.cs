using Microsoft.AspNetCore.Http;
using MyGarage.Shared.ServiceRecords;

namespace MyGarage.Web.ServiceRecords;

public sealed class ServicePartRow
{
    public string? Name { get; set; }

    public decimal? Quantity { get; set; }

    public decimal? UnitPrice { get; set; }

    public bool IsBlank => string.IsNullOrWhiteSpace(Name) && Quantity is null && UnitPrice is null;
}

// Form state for adding and editing a line item; the API validates and trims, so text is sent as entered.
public sealed class ServiceLineItemFormModel
{
    public const int MaxPartRows = 25;
    public const int BlankRowsToAdd = 3;

    // Submitted by the "Add more part rows" button, which re-renders the form without saving.
    public const string AddPartRowsField = "add-part-rows";

    public string? Description { get; set; }

    public PricingMode Pricing { get; set; }

    public decimal? FlatCost { get; set; }

    public decimal? LaborHours { get; set; }

    public decimal? LaborRate { get; set; }

    public List<ServicePartRow> Parts { get; set; } = [];

    public static ServiceLineItemFormModel New()
    {
        var model = new ServiceLineItemFormModel();
        model.AddBlankPartRows();
        return model;
    }

    public static ServiceLineItemFormModel From(ServiceLineItemDto line)
    {
        var model = new ServiceLineItemFormModel
        {
            Description = line.Description,
            Pricing = line.FlatCost is null && (line.LaborHours is not null || line.Parts.Count > 0) ? PricingMode.Breakdown : PricingMode.Flat,
            FlatCost = line.FlatCost,
            LaborHours = line.LaborHours,
            LaborRate = line.LaborRate,
            Parts = [.. line.Parts.Select(part => new ServicePartRow { Name = part.Name, Quantity = part.Quantity, UnitPrice = part.UnitPrice })],
        };
        model.AddBlankPartRows();
        return model;
    }

    // A posted model can arrive without rows; the form always shows at least the blank ones.
    public void AddBlankPartRows(int count = BlankRowsToAdd)
    {
        Parts ??= [];
        for (var added = 0; added < count && Parts.Count < MaxPartRows; added++)
        {
            Parts.Add(new ServicePartRow());
        }
    }

    // The rows that are sent, by their position in the form (blank rows are ignored).
    public IReadOnlyList<int> SubmittedRows =>
        [.. (Parts ?? []).Select((row, index) => (row, index)).Where(pair => !pair.row.IsBlank).Select(pair => pair.index)];

    // Only the selected pricing method is sent, so the other group's leftovers never reach the API.
    public ServiceLineItemRequest ToRequest() =>
        Pricing == PricingMode.Flat
            ? new ServiceLineItemRequest { Description = Description, FlatCost = FlatCost }
            : new ServiceLineItemRequest
            {
                Description = Description,
                LaborHours = LaborHours,
                LaborRate = LaborRate,
                Parts = [.. SubmittedRows.Select(index => new ServicePartRequest
                {
                    Name = Parts[index].Name,
                    Quantity = Parts[index].Quantity,
                    UnitPrice = Parts[index].UnitPrice,
                })],
            };

    public static bool IsAddPartRowsRequest(HttpContext? context) =>
        context is { Request.HasFormContentType: true } && context.Request.Form.ContainsKey(AddPartRowsField);
}
