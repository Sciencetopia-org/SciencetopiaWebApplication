public class ForgotUsernameDTO
{
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }

    // Add validation attributes as necessary
    // Example: [EmailAddress] for Email and [Phone] for PhoneNumber
}
