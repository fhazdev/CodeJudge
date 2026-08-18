using CodeJudge.Application.Common;
using CodeJudge.Application.Problems;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

namespace CodeJudge.Api.Controllers;

/// <summary>
/// Controllers stay thin (D8): bind, dispatch to MediatR, map the result to a status code.
/// No business logic, no DbContext, no branching beyond turning a handler result into
/// 200 or 404. The moment one of these starts making decisions, the ceremony of both
/// patterns is being paid for the benefit of neither.
/// </summary>
[ApiController]
[Route("api/problems")]
[Authorize]
// A valid token is not enough on its own: it must have been issued for this API, carrying
// the scope the SPA asked for. RequiredScope parses the space-separated scp claim
// properly, which a plain RequireClaim check would get wrong the moment a second scope
// is added.
[RequiredScope("access_as_user")]
[Produces("application/json")]
public sealed class ProblemsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProblemSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<ProblemSummaryDto>>> List(
        [FromQuery] ListProblemsQuery query, CancellationToken cancellationToken) =>
        Ok(await sender.Send(query, cancellationToken));

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(ProblemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProblemDetailDto>> Get(
        string slug, CancellationToken cancellationToken) =>
        await sender.Send(new GetProblemBySlugQuery(slug), cancellationToken) is { } problem
            ? Ok(problem)
            : NotFound();
}
