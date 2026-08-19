using CodeJudge.Application.Abstractions;
using CodeJudge.Domain.Entities;
using CodeJudge.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CodeJudge.Application.Submissions;

public sealed record CreateSubmissionCommand(string ProblemSlug, string Language, string Code)
    : IRequest<Guid?>;

public sealed class CreateSubmissionCommandValidator : AbstractValidator<CreateSubmissionCommand>
{
    /// <summary>
    /// Generous for a solution, mean for an abuse vector. Nothing legitimate approaches
    /// this, and an uncapped body is a free denial of service against both the database
    /// and the compiler.
    /// </summary>
    public const int MaxCodeLength = 64 * 1024;

    public CreateSubmissionCommandValidator()
    {
        RuleFor(c => c.ProblemSlug)
            .NotEmpty()
            .MaximumLength(128)
            .Matches("^[a-z0-9]+(-[a-z0-9]+)*$");

        RuleFor(c => c.Code)
            .NotEmpty()
            .MaximumLength(MaxCodeLength)
            .WithMessage($"Submissions are limited to {MaxCodeLength / 1024} KB.");

        // v1 is C# only. Accepting anything else would enqueue work the judge cannot do,
        // and the submission would sit in Queued forever.
        RuleFor(c => c.Language)
            .NotEmpty()
            .Must(language => string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Only 'csharp' is supported.");
    }
}

/// <summary>Returns null when no problem has that slug, which the controller turns into a 404.</summary>
public sealed class CreateSubmissionCommandHandler(
    IProblemRepository problems,
    ISubmissionRepository submissions,
    IUserRepository users,
    ISubmissionQueue queue,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<CreateSubmissionCommandHandler> logger)
    : IRequestHandler<CreateSubmissionCommand, Guid?>
{
    public async Task<Guid?> Handle(CreateSubmissionCommand request, CancellationToken cancellationToken)
    {
        var problem = await problems.GetBySlugAsync(request.ProblemSlug, cancellationToken);
        if (problem is null)
        {
            return null;
        }

        var user = await users.GetOrCreateAsync(
            currentUser.TenantId, currentUser.ObjectId,
            currentUser.Email, currentUser.DisplayName, cancellationToken);

        var submission = new Submission
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            ProblemId = problem.Id,
            Language = "csharp",
            Code = request.Code,
            Status = SubmissionStatus.Queued,
            CreatedAt = clock.UtcNow
        };

        await submissions.AddAsync(submission, cancellationToken);
        await submissions.SaveChangesAsync(cancellationToken);

        // Write first, then enqueue. The reverse order risks a message arriving before
        // the row it points at exists, which the judge would see as a phantom id.
        //
        // This ordering has its own failure mode: if the enqueue fails, the row is stranded
        // in Queued with nothing coming to judge it. Rather than leave the user watching a
        // spinner forever, it is marked InternalError immediately. The rigorous fix is a
        // transactional outbox, which is more machinery than a demo-scale project earns;
        // this is the honest, visible compromise.
        try
        {
            await queue.EnqueueAsync(submission.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue submission {SubmissionId}", submission.Id);

            submission.Status = SubmissionStatus.InternalError;
            submission.StderrExcerpt = "Could not queue this submission for judging. Please try again.";
            submission.CompletedAt = clock.UtcNow;
            await submissions.SaveChangesAsync(cancellationToken);
        }

        return submission.Id;
    }
}
