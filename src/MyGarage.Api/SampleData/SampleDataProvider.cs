using MyGarage.Shared.SampleData;

namespace MyGarage.Api.SampleData;

// Fixed placeholder data; replaced by persisted data in later features.
public static class SampleDataProvider
{
    private static readonly SampleItemDto[] Items =
        Enumerable.Range(1, 10)
            .Select(id => new SampleItemDto { Id = id, Name = $"Sample item {id}" })
            .ToArray();

    public static IReadOnlyList<SampleItemDto> GetItems(int count) => Items.Take(count).ToArray();
}
