using CodeJudge.Application.Abstractions;
using CodeJudge.Application.Common;
using FluentValidation;
using MediatR;

namespace CodeJudge.Application.Submissions;

// --- Get one ---------------------------------------------------------------

public sealed record GetSubmissionByIdQuery(Guid Id) : IRequest<SubmissionDto?>;

public sealed class GetSubmissionByIdQueryHandler(
    ISubmissionRepository submissions,
    IUserRepository users,
    ICurrentUser currentUser)
    : IRequestHandler<GetSubmissionByIdQuery, SubmissionDto?>
{
    public async Task<SubmissionDto?> Handle(
        GetSubmissionByIdQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetOrCreateAsync(
            currentUser.TenantId, currentUser.ObjectId,
            currentUser.Email, currentUser.DisplayName, cancellationToken);

        // Scoped to the caller inside the repository, so there is no path here that could
        // forget the ownership check.
        var submission = await submissions.GetForUserAsync(request.Id, user.Id, cancellationToken);

        return submission?.ToDto(submission.Problem?.Slug ?? string.Empty);
    }
}

// --- List ------------------------------------------------------------------

public sealed record ListSubmissionsQuery(string? ProblemSlug = null, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<SubmissionSummaryDto>>;

public sealed class ListSubmissionsQueryValidator : AbstractValidator<ListSubmissionsQuery>
{
    public ListSubmissionsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThan(0);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);

        RuleFor(q => q.ProblemSlug)
            .Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .When(q => !string.IsNullOrEmpty(q.ProblemSlug));
    }
}

public sealed class ListSubmissionsQueryHandler(
    ISubmissionRepository submissions,
    IProblemRepository problems,
    IUserRepository users,
    ICurrentUser currentUser)
    : IRequestHandler<ListSubmissionsQuery, PagedResult<SubmissionSummaryDto>>
{
    public async Task<PagedResult<SubmissionSummaryDto>> Handle(
        ListSubmissionsQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetOrCreateAsync(
            currentUser.TenantId, currentUser.ObjectId,
            currentUser.Email, currentUser.DisplayName, cancellationToken);

        Guid? problemId = null;
        if (!string.IsNullOrEmpty(request.ProblemSlug))
        {
            var problem = await problems.GetBySlugAsync(request.ProblemSlug, cancellationToken);

            // An unknown slug is an empty page, not an error. Filtering by something that
            // does not exist legitimately matches nothing.
            if (problem is null)
            {
                return new PagedResult<SubmissionSummaryDto>([], request.Page, request.PageSize, 0);
            }

            problemId = problem.Id;
        }

        var skip = (request.Page - 1) * request.PageSize;

        var items = await submissions.ListForUserAsync(
            user.Id, problemId, skip, request.PageSize, cancellationToken);

        var total = await submissions.CountForUserAsync(user.Id, problemId, cancellationToken);

        return new PagedResult<SubmissionSummaryDto>(
            items.Select(s => s.ToSummary(s.Problem?.Slug ?? string.Empty)).ToList(),
            request.Page,
            request.PageSize,
            total);
    }
}
