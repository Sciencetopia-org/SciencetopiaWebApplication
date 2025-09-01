namespace Sciencetopia.DTOs;

public class EffectivePermissionsDto
{
    public bool CanView { get; set; }
    public bool CanComment { get; set; }
    public bool CanEdit { get; set; }
    public bool CanPublish { get; set; }
    public bool CohortManage { get; set; }
    public bool CohortInvite { get; set; }
}

