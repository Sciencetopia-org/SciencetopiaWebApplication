using System.ComponentModel.DataAnnotations;

public class VerifyPhoneNumberDTO
{
    [Required]
    [Phone]
    public string? PhoneNumber { get; set; }

    [Required]
    public string? Token { get; set; }
}
