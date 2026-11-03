namespace MyGarage.Api.Common;

public static class MoneyRules
{
    public const decimal MaxAmount = 9_999_999.99m;

    public static bool HasAtMostDecimals(decimal? value, int places) => value is null || decimal.Round(value.Value, places) == value.Value;

    public static bool HasAtMostTwoDecimals(decimal? value) => HasAtMostDecimals(value, 2);
}
