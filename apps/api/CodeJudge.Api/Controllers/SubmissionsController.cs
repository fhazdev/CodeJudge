using CodeJudge.Application.Common;
using CodeJudge.Application.Submissions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

namespace CodeJudge.Api.Controllers;

[ApiController]
[Route("api/submissions")]
[Authorize]
[RequiredScope("access_as_user")]
[Produces("application/json")]
public sealed class SubmissionsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Accepts a submission and hands it to the judge. Returns 202, not 201: the resource
    /// exists but the work it represents has not happened yet, and the client is expected
    /// to poll the Location header until the status is terminal.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSubmissionCommand command, CancellationToken cancellationToken)
    {
        var id = await sender.Send(command, cancellationToken);

        // Null means no problem has that slug. A submission against a problem that does
        // not exist is a 404 on the problem, not a validation error on the body.
        if (id is null)
        {
            return NotFound();
        }

        var submission = await sender.Send(new GetSubmissionByIdQuery(id.Value), cancellationToken);

        // AcceptedAtAction rather than a bare Accepted: the SPA reads Location to know
        // where to poll, instead of constructing the URL itself.
        return AcceptedAtAction(nameof(Get), new { id = id.Value }, submission);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubmissionDto>> Get(Guid id, CancellationToken cancellationToken) =>
        await sender.Send(new GetSubmissionByIdQuery(id), cancellationToken) is { } submission
            ? Ok(submission)
            : NotFound();

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SubmissionSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SubmissionSummaryDto>>> List(
        [FromQuery] ListSubmissionsQuery query, CancellationToken cancellationToken) =>
        Ok(await sender.Send(query, cancellationToken));
}
