namespace Sciencetopia.DTOs;

public class EnrollmentMeDto
{
    public Guid? ActiveCohortId { get; set; }
    public string? Role { get; set; } // Owner/Admin/Member/null
    public long? JoinedAt { get; set; } // epoch ms if from Neo4j
    public List<Guid> ArchivedCohortIds { get; set; } = new();
}

