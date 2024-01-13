using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Models;

namespace Sciencetopia.Extensions
{
    public static class UserManagerExtensions
    {
        public static async Task<ApplicationUser> FindByPhoneNumberAsync(this UserManager<ApplicationUser> userManager, string phoneNumber)
        {
            return await userManager?.Users?.FirstOrDefaultAsync(x => x.PhoneNumber == phoneNumber);
        }
    }

}
