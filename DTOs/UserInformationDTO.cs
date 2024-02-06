using System.ComponentModel.DataAnnotations;

public class UserInformationDTO
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? SelfIntroduction { get; set; }
    public string? Gender { get; set; }
    
    public DateTime? Birth { get; set; }
    public string? PhoneNumber { get; set; }
    // Add other fields as needed

    // Formatted BirthDate for display
    public string? FormattedBirthDate => Birth?.ToString("yyyy-MM-dd");
}
