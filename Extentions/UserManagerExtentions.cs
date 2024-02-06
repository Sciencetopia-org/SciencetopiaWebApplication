using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Models;

namespace Sciencetopia.Extensions
{
    public static class UserManagerExtensions
    {
        public static async Task<ApplicationUser> FindByPhoneNumberAsync(this UserManager<ApplicationUser> userManager, string phoneNumber)
        {
            if (userManager != null && userManager.Users != null)
            {
                var user = await userManager.Users.FirstOrDefaultAsync(x => x.PhoneNumber == phoneNumber);
                if (user != null)
                {
                    return user;
                }
            }
            return null;
        }
    }
}
