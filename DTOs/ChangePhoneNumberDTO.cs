using System.ComponentModel.DataAnnotations;

public class ChangePhoneNumberDTO
{
    [Required]
    [Phone]
    public string NewPhoneNumber { get; set; }
}
