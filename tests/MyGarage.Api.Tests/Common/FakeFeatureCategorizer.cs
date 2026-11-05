using MyGarage.Api.Vehicles.Tags;

namespace MyGarage.Api.Tests.Common;

// Stands in for the model during endpoint tests: answers what the test sets and records the names it was asked about.
public sealed class FakeFeatureCategorizer : IFeatureCategorizer
{
    private readonly List<IReadOnlyList<string>> calls = [];

    public bool IsEnabled { get; set; } = true;

    // The reason to fail every call with; null answers normally.
    public string? FailureReason { get; set; }

    // The category for a name; null leaves it without an answer. The default puts everything under Other.
    public Func<string, string?> Answer { get; set; } = _ => "Other";

    public IReadOnlyList<IReadOnlyList<string>> Calls
    {
        get
        {
            lock (calls)
            {
                return [.. calls];
            }
        }
    }

    // Back to the default enabled state and answer, with no recorded calls; the factory is shared by a test class's tests.
    public void Reset()
    {
        lock (calls)
        {
            calls.Clear();
        }

        IsEnabled = true;
        FailureReason = null;
        Answer = _ => "Other";
    }

    public Task<FeatureCategorization> CategorizeAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        lock (calls)
        {
            calls.Add(names);
        }

        return Task.FromResult(FailureReason is { } reason
            ? FeatureCategorization.Failed(reason)
            : new FeatureCategorization([.. names.Select(Answer)], null));
    }
}
