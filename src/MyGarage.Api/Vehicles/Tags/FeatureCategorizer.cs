using System.Text.Json;
using Microsoft.Extensions.Options;
using MyGarage.Api.Common;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.Tags;

// Bound from FeatureCategories. The endpoint and deployment are the ones email intake already uses (EmailIntake:Extraction).
public sealed class FeatureCategorizationOptions
{
    public const string SectionName = "FeatureCategories";

    // A person may be waiting for the page that follows adding a feature, so one call is bounded tightly.
    public int TimeoutSeconds { get; set; } = 30;

    // How many pending feature names go to the model in one call.
    public int BatchSize { get; set; } = 100;

    // The background sweep that retries features the inline call could not categorize.
    public bool SweepEnabled { get; set; } = true;

    public int SweepMinutes { get; set; } = 10;
}

// Categories line up with the names asked about; null where the model gave no usable answer.
public sealed record FeatureCategorization(IReadOnlyList<string?> Categories, string? FailureReason)
{
    public bool IsFailure => FailureReason is not null;

    public static FeatureCategorization Failed(string reason) => new([], reason);
}

public interface IFeatureCategorizer
{
    bool IsEnabled { get; }

    // Returns a failed result (FailureReason set) for service errors instead of throwing.
    Task<FeatureCategorization> CategorizeAsync(IReadOnlyList<string> names, CancellationToken cancellationToken);
}

// Sorts feature names into the fixed categories through the shared Responses client. The model gets no tools and only the names.
public sealed class FeatureCategorizer : IFeatureCategorizer
{
    private static readonly JsonSerializerOptions AnswerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private readonly ResponsesApiClient client;
    private readonly IOptions<FeatureCategorizationOptions> options;
    private readonly ILogger<FeatureCategorizer> logger;

    public FeatureCategorizer(
        HttpClient httpClient,
        IOptions<ExtractionOptions> extractionOptions,
        IOptions<FeatureCategorizationOptions> options,
        Azure.Core.TokenCredential credential,
        ILogger<FeatureCategorizer> logger)
    {
        client = new ResponsesApiClient(httpClient, extractionOptions, credential);
        this.options = options;
        this.logger = logger;
    }

    public bool IsEnabled => client.IsEnabled;

    public async Task<FeatureCategorization> CategorizeAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return new FeatureCategorization([], null);
        }

        var call = new ResponsesCall(
            FeatureCategorySchema.Instructions,
            FeatureCategorySchema.Name,
            FeatureCategorySchema.Json,
            BuildInputText(names),
            null,
            TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds)));

        var outcome = await client.SendAsync(call, cancellationToken);
        if (outcome.IsFailure)
        {
            logger.LogWarning("Feature categorization failed with {Reason} (HTTP {StatusCode})", outcome.FailureReason, outcome.StatusCode);
            return FeatureCategorization.Failed(outcome.FailureReason!);
        }

        var parsed = Parse(outcome.AnswerText!, names.Count);
        if (parsed is null)
        {
            logger.LogWarning("Feature categorization failed with {Reason} (HTTP {StatusCode})", "invalid_output", outcome.StatusCode);
            return FeatureCategorization.Failed("invalid_output");
        }

        return new FeatureCategorization(parsed, null);
    }

    private static string BuildInputText(IReadOnlyList<string> names)
    {
        var text = new System.Text.StringBuilder("Categorize these vehicle features (numbered):");
        for (var i = 0; i < names.Count; i++)
        {
            text.Append('\n').Append(i + 1).Append(". ").Append(names[i]);
        }

        return text.ToString();
    }

    // Each answer is checked against the fixed categories; an index outside the list or an unknown category is dropped.
    private static string?[]? Parse(string answerJson, int count)
    {
        try
        {
            var answer = JsonSerializer.Deserialize<Answer>(answerJson, AnswerOptions);
            if (answer?.Items is null)
            {
                return null;
            }

            var categories = new string?[count];
            foreach (var item in answer.Items)
            {
                if (item.Index >= 1 && item.Index <= count && FeatureCategories.Normalize(item.Category) is { } category)
                {
                    categories[item.Index - 1] ??= category;
                }
            }

            return categories;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Answer(List<AnswerItem>? Items);

    private sealed record AnswerItem(int Index, string? Category);
}
