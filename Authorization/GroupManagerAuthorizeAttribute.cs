using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Sciencetopia.Services;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Sciencetopia.Authorization
{
    public class GroupManagerAuthorizeAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly StudyGroupService _studyGroupService;

        public GroupManagerAuthorizeAttribute(StudyGroupService studyGroupService)
        {
            _studyGroupService = studyGroupService;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var httpContext = context.HttpContext;
            var userId = httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var studyGroupId = context.RouteData.Values["studyGroupId"]?.ToString();

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(studyGroupId))
            {
                context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
                return;
            }

            var isManager = await _studyGroupService.IsUserManagerAsync(studyGroupId, userId);
            if (!isManager)
            {
                context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
            }
        }
    }
}
