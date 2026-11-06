namespace MyGarage.Web.Dashboard;

// One colored layer of the stacked bars: a vehicle, or the vehicles that did not get a color of their own.
public sealed record SpendSeries(string Key, string Name, string CssClass);

public sealed record StackedSegment(SpendSeries Series, decimal Amount);

// One month's bar: its layers from the bottom up.
public sealed record StackedMonth(DateOnly Month, IReadOnlyList<StackedSegment> Segments)
{
    public decimal Total => Segments.Sum(segment => segment.Amount);
}
