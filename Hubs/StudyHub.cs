using Microsoft.AspNetCore.SignalR;

namespace Sciencetopia.Hubs;

public class StudyHub : Hub
{
    public async Task JoinCohort(Guid cohortId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"cohort:{cohortId}");
    }

    public async Task LeaveCohort(Guid cohortId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"cohort:{cohortId}");
    }
}

