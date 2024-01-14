using Microsoft.AspNetCore.Identity;
using Sciencetopia.Models;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class CustomUserValidator : UserValidator<ApplicationUser>
{
    public override async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        // Collect errors here. Start with the errors from the base class validation
        var errors = new List<IdentityError>();

        // Define a regular expression for your username validation
        // This regex allows alphanumeric characters, international characters (including Chinese), and some special characters
        string usernameRegex = @"^[\p{L}\p{N}_\-.@]+$";
        
        // Regex with Unicode character support
        Regex regex = new Regex(usernameRegex, RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Validate the username with the regex
        if (!regex.IsMatch(user.UserName))
        {
            errors.Add(new IdentityError
            {
                Description = "Username can only contain letters, numbers, '-', '_', '.', '@', and international characters."
            });
        }

        // Return success if no errors, otherwise return failure with the list of errors
        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }
}
