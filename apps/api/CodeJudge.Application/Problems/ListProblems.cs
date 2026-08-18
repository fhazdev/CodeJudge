using CodeJudge.Application.Abstractions;
using CodeJudge.Application.Common;
using FluentValidation;
using MediatR;

namespace CodeJudge.Application.Problems;

public sealed record ListProblemsQuery(int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<ProblemSummaryDto>>;

public sealed class ListProblemsQueryValidator : AbstractValidator<ListProblemsQuery>
{
    public ListProblemsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThan(0);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class ListProblemsQueryHandler(IProblemRepository problems)
    : IRequestHandler<ListProblemsQuery, PagedResult<ProblemSummaryDto>>
{
    public async Task<PagedResult<ProblemSummaryDto>> Handle(
        ListProblemsQuery request, CancellationToken cancellationToken)
    {
        var skip = (request.Page - 1) * request.PageSize;

        var items = await problems.ListAsync(skip, request.PageSize, cancellationToken);
        var total = await problems.CountAsync(cancellationToken);

        return new PagedResult<ProblemSummaryDto>(
            items.Select(problem => problem.ToSummary()).ToList(),
            request.Page,
            request.PageSize,
            total);
    }
}
