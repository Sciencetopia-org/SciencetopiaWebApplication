namespace Sciencetopia.DTOs;

public class EffectivePermissionsDto
{
    public string Role { get; set; } = "Viewer";
    public bool CanView { get; set; }
    public bool CanComment { get; set; }
    public bool CanEdit { get; set; }
    public bool CanPublish { get; set; }
    public bool AllowCohortSharing { get; set; }
    public bool CanAdoptPlanToCohort { get; set; }
    public bool CohortManage { get; set; }
    public bool CohortInvite { get; set; }
    public string? CohortPermission { get; set; }
}
