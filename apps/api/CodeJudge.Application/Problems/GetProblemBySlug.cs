using CodeJudge.Application.Abstractions;
using FluentValidation;
using MediatR;

namespace CodeJudge.Application.Problems;

public sealed record GetProblemBySlugQuery(string Slug) : IRequest<ProblemDetailDto?>;

public sealed class GetProblemBySlugQueryValidator : AbstractValidator<GetProblemBySlugQuery>
{
    public GetProblemBySlugQueryValidator()
    {
        RuleFor(q => q.Slug)
            .NotEmpty()
            .MaximumLength(128)
            .Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .WithMessage("Slug must be lowercase words separated by single hyphens.");
    }
}

/// <summary>
/// Returns null rather than throwing when nothing matches. A missing problem is an
/// ordinary outcome of a user editing the URL, not an exceptional one, and the controller
/// turns null into a 404.
/// </summary>
public sealed class GetProblemBySlugQueryHandler(IProblemRepository problems)
    : IRequestHandler<GetProblemBySlugQuery, ProblemDetailDto?>
{
    public async Task<ProblemDetailDto?> Handle(
        GetProblemBySlugQuery request, CancellationToken cancellationToken)
    {
        var problem = await problems.GetBySlugAsync(request.Slug, cancellationToken);

        return problem?.ToDetail();
    }
}
