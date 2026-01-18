namespace Sciencetopia.DTOs;

public class JoinCohortRequest
{
    public bool? ShareMetrics { get; set; }
    public string? MigrationStrategy { get; set; } // reserved, ignored for join
}

public class SwitchCohortRequest
{
    public Guid ToCohortId { get; set; }
    public Guid? FromCohortId { get; set; }
    public string? MigrationStrategy { get; set; }
    public bool? ShareMetrics { get; set; }
}

public class JoinCohortResponse
{
    public Guid PlanId { get; set; }
    public Guid CohortId { get; set; }
}

public class SwitchCohortResponse
{
    public Guid PlanId { get; set; }
    public Guid? FromCohortId { get; set; }
    public Guid ToCohortId { get; set; }
}

