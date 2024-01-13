public class RegisterDTO
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Password { get; set; }
     public bool VerifyByEmail { get; set; } // User's choice for verification method
}