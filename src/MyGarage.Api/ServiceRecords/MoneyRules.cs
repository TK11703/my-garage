namespace MyGarage.Api.ServiceRecords;

public static class MoneyRules
{
    public const decimal MaxAmount = 9_999_999.99m;

    public static bool HasAtMostTwoDecimals(decimal? value) => value is null || decimal.Round(value.Value, 2) == value.Value;
}
