using System.Linq.Expressions;
using FluentValidation;

namespace MyGarage.Api.Common;

public sealed record PagingQuery(int Page = 1, int? PageSize = null);

public static class PagingRules
{
    public const int MaxPageSize = 50;

    public static void AddPagingRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int>> page,
        Expression<Func<T, int?>> pageSize)
    {
        validator.RuleFor(page).GreaterThanOrEqualTo(1);
        validator.RuleFor(pageSize).InclusiveBetween(1, MaxPageSize);
    }
}

public sealed class PagingQueryValidator : AbstractValidator<PagingQuery>
{
    public PagingQueryValidator() => this.AddPagingRules(query => query.Page, query => query.PageSize);
}
