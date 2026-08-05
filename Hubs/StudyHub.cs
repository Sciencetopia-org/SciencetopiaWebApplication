using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Sciencetopia.Data;

namespace Sciencetopia.Hubs;

[Authorize]
public class StudyHub : Hub
{
    private readonly ApplicationDbContext _db;

    public StudyHub(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task JoinCohort(Guid cohortId)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId))
        {
            throw new HubException("Authentication required");
        }

        var isMember = await _db.UserGroups.AsNoTracking()
            .AnyAsync(x => x.GroupId == cohortId
                && x.UserId == userId
                && (string.IsNullOrEmpty(x.Status) || x.Status == "Active" || x.Status == "active"));

        if (!isMember)
        {
            throw new HubException("Not authorized for this cohort");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"cohort:{cohortId}");
    }

    public async Task LeaveCohort(Guid cohortId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"cohort:{cohortId}");
    }
}

