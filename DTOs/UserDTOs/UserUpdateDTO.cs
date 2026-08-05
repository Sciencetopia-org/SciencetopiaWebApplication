public class UserUpdateDTO
{
    public string? SelfIntroduction { get; set; }
    public string? Gender { get; set; }
    public DateTime BirthDate { get; set; }
    public bool ShowStudyPlansPublicly { get; set; } = true;
    public bool ShowStudyGroupsPublicly { get; set; } = true;
}
