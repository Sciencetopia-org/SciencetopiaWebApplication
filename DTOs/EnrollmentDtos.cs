namespace Sciencetopia.DTOs;

public class EnrollmentMeDto
{
    public Guid? ActiveCohortId { get; set; }
    public string? Role { get; set; } // manager/member/viewer/null
    public long? JoinedAt { get; set; } // epoch ms if from Neo4j
    public List<Guid> ArchivedCohortIds { get; set; } = new();
}

