using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;

public class DailySummaryService
{
    private readonly ApplicationDbContext _context;
    private readonly StudyGroupService? _studyGroupService;
    // private readonly KnowledgeGraphService? _knowledgeGraphService;
    public DailySummaryService(ApplicationDbContext context, StudyGroupService? studyGroupService)
    {
        _context = context;
        _studyGroupService = studyGroupService;
    }

    public async Task GenerateDailySummary(DateTime date)
    {
        var snapshot = await BuildDailySnapshot(date.Date);

        // Save the summary in the database
        var summary = new DailySummary
        {
            Date = snapshot.Date,
            TotalVisits = snapshot.TotalVisits,
            DailyVisits = snapshot.DailyVisits,
            LoggedInUsers = snapshot.LoggedInUsers,
            TotalUsers = snapshot.TotalUsers,
            NewUsers = snapshot.NewUsers,
            ReturningUsers = snapshot.ReturningUsers,
            TotalKnowledgeNodes = snapshot.TotalKnowledgeNodes,
            TotalStudyGroups = snapshot.TotalStudyGroups,
            WeeklyActiveStudyGroups = snapshot.WeeklyActiveStudyGroups,
            TotalKnowledgeNodeViews = snapshot.TotalKnowledgeNodeViews
        };

        _context.DailySummaries.Add(summary);
        await _context.SaveChangesAsync();
    }

    public async Task<object> GetWeeklySummary()
    {
        var today = DateTime.UtcNow.Date;
        var startDate = today.AddDays(-7);

        var weeklySummaries = new List<DailySnapshot>();
        for (var date = startDate.AddDays(1); date <= today; date = date.AddDays(1))
        {
            weeklySummaries.Add(await BuildDailySnapshot(date));
        }

        var todaySummary = weeklySummaries.Last();

        // Prepare daily data arrays for charts
        var totalUsersData = weeklySummaries.Select(s => s.TotalUsers).ToList();
        var dailyLoggedInUsersData = weeklySummaries.Select(s => s.LoggedInUsers).ToList();
        var totalVisitsData = weeklySummaries.Select(s => s.TotalVisits).ToList();
        var dailyVisitsData = weeklySummaries.Select(s => s.DailyVisits).ToList();
        var dailyNewUsersData = weeklySummaries.Select(s => s.NewUsers).ToList();
        var dailyReturningUsersData = weeklySummaries.Select(s => s.ReturningUsers).ToList();
        var dailyStudyGroupsData = weeklySummaries.Select(s => s.TotalStudyGroups).ToList();
        var dailyActiveStudyGroupsData = weeklySummaries.Select(s => s.WeeklyActiveStudyGroups).ToList();
        var dailyKnowledgeNodesData = weeklySummaries.Select(s => s.TotalKnowledgeNodes).ToList();
        var dailyKnowledgeNodeViewsData = weeklySummaries.Select(s => s.TotalKnowledgeNodeViews).ToList();

        // Calculate growth using data from the previous week
        var previousStartDate = startDate.AddDays(-7);
        var previousSummaries = new List<DailySnapshot>();
        for (var date = previousStartDate.AddDays(1); date <= startDate; date = date.AddDays(1))
        {
            previousSummaries.Add(await BuildDailySnapshot(date));
        }

        // Handle empty previous summaries
        var previousTotalUsers = previousSummaries.Any() ? previousSummaries.Max(s => s.TotalUsers) : 0;
        var previousLoggedInUsers = previousSummaries.Any() ? previousSummaries.Sum(s => s.LoggedInUsers) : 0;
        var previousTotalVisits = previousSummaries.Any() ? previousSummaries.Max(s => s.TotalVisits) : 0;
        var previousDailyVisits = previousSummaries.Any() ? previousSummaries.Sum(s => s.DailyVisits) : 0;
        var previousNewUsers = previousSummaries.Any() ? previousSummaries.Sum(s => s.NewUsers) : 0;
        var previousReturningUsers = previousSummaries.Any() ? previousSummaries.Sum(s => s.ReturningUsers) : 0;
        var previousStudyGroups = previousSummaries.Any() ? previousSummaries.Max(s => s.TotalStudyGroups) : 0;
        var previousActiveStudyGroups = previousSummaries.Any() ? previousSummaries.Max(s => s.WeeklyActiveStudyGroups) : 0;
        var previousKnowledgeNodes = previousSummaries.Any() ? previousSummaries.Max(s => s.TotalKnowledgeNodes) : 0;
        var previousKnowledgeNodeViews = previousSummaries.Any() ? previousSummaries.Sum(s => s.TotalKnowledgeNodeViews) : 0;

        // Calculate growth for each metric
        var growthUsers = ComputeGrowth(todaySummary.TotalUsers, previousTotalUsers);
        var growthLoggedInUsers = ComputeGrowth(weeklySummaries.Sum(s => s.LoggedInUsers), previousLoggedInUsers);
        var growthTotalVisits = ComputeGrowth(todaySummary.TotalVisits, previousTotalVisits);
        var growthVisits = ComputeGrowth(weeklySummaries.Sum(s => s.DailyVisits), previousDailyVisits);
        var growthNewUsers = ComputeGrowth(weeklySummaries.Sum(s => s.NewUsers), previousNewUsers);
        var growthReturningUsers = ComputeGrowth(weeklySummaries.Sum(s => s.ReturningUsers), previousReturningUsers);
        var growthStudyGroups = ComputeGrowth(todaySummary.TotalStudyGroups, previousStudyGroups);
        var growthActiveStudyGroups = ComputeGrowth(todaySummary.WeeklyActiveStudyGroups, previousActiveStudyGroups);
        var growthKnowledgeNodes = ComputeGrowth(todaySummary.TotalKnowledgeNodes, previousKnowledgeNodes);
        var growthKnowledgeNodeViews = ComputeGrowth(weeklySummaries.Sum(s => s.TotalKnowledgeNodeViews), previousKnowledgeNodeViews);

        // Return the aggregated data
        return new
        {
            users = new
            {
                todayData = todaySummary.TotalUsers,
                growth = growthUsers,
                dailyData = totalUsersData
            },
            loggedInUsers = new
            {
                todayData = todaySummary.LoggedInUsers,
                growth = growthLoggedInUsers,
                dailyData = dailyLoggedInUsersData
            },
            totalVisits = new
            {
                todayData = todaySummary.TotalVisits,
                growth = growthTotalVisits,
                dailyData = totalVisitsData
            },
            dailyVisits = new
            {
                todayData = todaySummary.DailyVisits,
                growth = growthVisits,
                dailyData = dailyVisitsData
            },
            newUsers = new
            {
                todayData = todaySummary.NewUsers,
                growth = growthNewUsers,
                dailyData = dailyNewUsersData
            },
            returningUsers = new
            {
                todayData = todaySummary.ReturningUsers,
                growth = growthReturningUsers,
                dailyData = dailyReturningUsersData
            },
            totalStudyGroups = new
            {
                todayData = todaySummary.TotalStudyGroups,
                growth = growthStudyGroups,
                dailyData = dailyStudyGroupsData
            },
            weeklyActiveStudyGroups = new
            {
                todayData = todaySummary.WeeklyActiveStudyGroups,
                growth = growthActiveStudyGroups,
                dailyData = dailyActiveStudyGroupsData
            },
            totalKnowledgeNodes = new
            {
                todayData = todaySummary.TotalKnowledgeNodes,
                growth = growthKnowledgeNodes,
                dailyData = dailyKnowledgeNodesData
            },
            totalKnowledgeNodeViews = new
            {
                todayData = todaySummary.TotalKnowledgeNodeViews,
                growth = growthKnowledgeNodeViews,
                dailyData = dailyKnowledgeNodeViewsData
            }
        };
    }

    private async Task<DailySnapshot> BuildDailySnapshot(DateTime date)
    {
        var startDate = date.Date;
        var endDate = startDate.AddDays(1);
        var activeGroupWindowStart = endDate.AddDays(-7);

        var totalVisits = await _context.VisitLogs
            .AsNoTracking()
            .CountAsync(v => v.VisitTime < endDate);

        var dailyVisits = await _context.VisitLogs
            .AsNoTracking()
            .CountAsync(v => v.VisitTime >= startDate && v.VisitTime < endDate);

        var loggedInUsers = await _context.VisitLogs
            .AsNoTracking()
            .Where(v => v.VisitTime >= startDate && v.VisitTime < endDate && v.IsLoggedIn && v.UserId != null)
            .Select(v => v.UserId)
            .Distinct()
            .CountAsync();

        var totalUsers = await _context.Users
            .AsNoTracking()
            .CountAsync(u => u.RegisteredAt < endDate);

        var newUsers = await _context.Users
            .AsNoTracking()
            .CountAsync(u => u.RegisteredAt >= startDate && u.RegisteredAt < endDate);

        var returningUsers = await (
            from visit in _context.VisitLogs.AsNoTracking()
            join user in _context.Users.AsNoTracking() on visit.UserId equals user.Id
            where visit.VisitTime >= startDate
                && visit.VisitTime < endDate
                && visit.IsLoggedIn
                && user.RegisteredAt < startDate
            select user.Id
        ).Distinct().CountAsync();

        var totalStudyGroups = await _context.StudyGroups
            .AsNoTracking()
            .CountAsync(g =>
                g.CreatedAt < endDate &&
                (g.Status == null || g.Status == "" || g.Status == "approved" || g.Status == "Approved" || g.Status == "active" || g.Status == "Active"));

        var weeklyActiveStudyGroups = await (
            from membership in _context.UserGroups.AsNoTracking()
            join studyGroup in _context.StudyGroups.AsNoTracking() on membership.GroupId equals studyGroup.Id
            where membership.JoinedAt >= activeGroupWindowStart
                && membership.JoinedAt < endDate
                && (membership.Status == null || membership.Status == "" || membership.Status == "Active" || membership.Status == "active")
                && (studyGroup.Status == null || studyGroup.Status == "" || studyGroup.Status == "approved" || studyGroup.Status == "Approved" || studyGroup.Status == "active" || studyGroup.Status == "Active")
            select studyGroup.Id
        ).Distinct().CountAsync();

        var totalKnowledgeNodes = await _context.KnowledgeNodes
            .AsNoTracking()
            .CountAsync(n =>
                n.StableId != Guid.Empty &&
                n.IsCurrent &&
                n.Status == "Current" &&
                (n.PublishedAt == null || n.PublishedAt < endDate));

        return new DailySnapshot(
            startDate,
            totalUsers,
            loggedInUsers,
            totalVisits,
            dailyVisits,
            newUsers,
            returningUsers,
            totalStudyGroups,
            weeklyActiveStudyGroups,
            totalKnowledgeNodes,
            0);
    }

    private double ComputeGrowth(int currentValue, int previousValue)
    {
        if (previousValue == 0)
        {
            return currentValue > 0 ? 100 : 0;
        }
        return ((double)(currentValue - previousValue) / previousValue) * 100;
    }

    private sealed record DailySnapshot(
        DateTime Date,
        int TotalUsers,
        int LoggedInUsers,
        int TotalVisits,
        int DailyVisits,
        int NewUsers,
        int ReturningUsers,
        int TotalStudyGroups,
        int WeeklyActiveStudyGroups,
        int TotalKnowledgeNodes,
        int TotalKnowledgeNodeViews);
}
