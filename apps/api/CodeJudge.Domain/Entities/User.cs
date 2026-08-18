namespace CodeJudge.Domain.Entities;

public class User
{
    public Guid Id { get; set; }

    /// <summary>
    /// The token's <c>oid</c> claim. Unique only *within* a tenant, which is why identity
    /// here is the (<see cref="EntraTenantId"/>, <see cref="EntraObjectId"/>) pair and not
    /// this column alone. See section 6 of the build plan.
    /// </summary>
    public required string EntraObjectId { get; set; }

    /// <summary>
    /// The token's <c>tid</c> claim. Personal Microsoft accounts always report
    /// 9188040d-6c67-4c5b-b112-36a304b66dad.
    /// </summary>
    public required string EntraTenantId { get; set; }

    public string? Email { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Submission> Submissions { get; set; } = [];
}
