using MyGarage.Api.Vehicles.StickerImport;

namespace MyGarage.Api.Tests.Common;

// Stands in for the model during endpoint tests: answers what the test sets and records what it was asked.
public sealed class FakeStickerExtractor : IStickerExtractor
{
    public sealed record Call(string ContentType, string FileName, long Bytes, IReadOnlyList<string>? ExistingFeatures);

    private readonly List<Call> calls = [];

    public bool IsEnabled { get; set; } = true;

    public StickerExtraction Result { get; set; } = Sticker();

    public IReadOnlyList<Call> Calls
    {
        get
        {
            lock (calls)
            {
                return [.. calls];
            }
        }
    }

    public static StickerExtraction Sticker() =>
        new()
        {
            IsWindowSticker = true,
            VehiclesDetected = 1,
            Year = 2024,
            Make = "Ford",
            Model = "F-150",
            Trim = "Lariat",
            Vin = "1FTFW1E50NFA00001",
            BodyStyle = "Pickup",
            Engine = "3.5L V6",
            Horsepower = 400,
            FuelType = "Gasoline",
            Transmission = "Automatic",
            Drivetrain = "4WD",
            ExteriorColor = "Agate Black",
            Seats = 5,
            Doors = 4,
            WheelSizeInches = 20m,
            TireSize = "275/55R20",
            Features = [new ExtractedFeature("Heated Seats", []), new ExtractedFeature("Panoramic Sunroof", [1])],
        };

    // Back to the default enabled state and answer, with no recorded calls; the factory is shared by a test class's tests.
    public void Reset()
    {
        lock (calls)
        {
            calls.Clear();
        }

        IsEnabled = true;
        Result = Sticker();
    }

    public async Task<StickerExtraction> ExtractAsync(StickerInput input, CancellationToken cancellationToken)
    {
        using var counted = new MemoryStream();
        await input.Content.CopyToAsync(counted, cancellationToken);
        lock (calls)
        {
            calls.Add(new Call(input.ContentType, input.FileName, counted.Length, input.ExistingFeatures));
        }

        return Result;
    }
}
