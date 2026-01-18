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
        var startDate = date.Date;
        var endDate = startDate.AddDays(1);

        // Total visits till now
        var totalVisits = await _context.VisitLogs
            .Where(v => v.VisitTime < endDate)
            .CountAsync();

        // Total visits for the day
        var DailyVisits = await _context.VisitLogs
            .Where(v => v.VisitTime >= startDate && v.VisitTime < endDate)
            .CountAsync();

        // Logged in users for the day
        var loggedInUsers = await _context.VisitLogs
            .Where(v => v.VisitTime >= startDate && v.VisitTime < endDate && v.IsLoggedIn)
            .Select(v => v.UserId)
            .Distinct()
            .CountAsync();

        // Total registered users for the day
        var totalUsers = await _context.Users
            .Where(u => u.RegisteredAt < endDate)
            .CountAsync();

        // Retrieve Total Number of Knowledge Nodes
        // var nodes = await _sqlRepository.GetAllNodeIdsAsync();
        // var KnowledgeNodesCount = nodes.Count();
        var KnowledgeNodesCount = 0;
        // Retrieve Total Number of Study Groups
        var StudyGroupsCount = 0;
        if (_studyGroupService != null)
        {
            try
            {
                var groups = await _studyGroupService.GetAllStudyGroups();
                StudyGroupsCount = groups.Count;
            }
            catch (Exception ex)
            {
                // Neo4j may be offline locally; record 0 and continue.
                // Swallow to avoid crashing the hosted service.
            }
        }

        // // Retrieve Total Number of Active Study Groups
        // var activeGroups = await _studyGroupService.GetActiveStudyGroups();
        // var ActiveStudyGroupsCount = activeGroups.Count;

        // // Retrieve Total Number of Knowledge Node Views
        // var nodeViews = await _knowledgeGraphService.FetchKnowledgeNodeViews();
        // var KnowledgeNodeViewsCount = nodeViews.Count;

        // You can add logic for new vs returning users here (optional)

        // Save the summary in the database
        var summary = new DailySummary
        {
            Date = startDate,
            TotalVisits = totalVisits,
            DailyVisits = DailyVisits,
            LoggedInUsers = loggedInUsers,
            TotalUsers = totalUsers,
            TotalKnowledgeNodes = KnowledgeNodesCount,
            TotalStudyGroups = StudyGroupsCount,
            // WeeklyActiveStudyGroups = ActiveStudyGroupsCount,
            // TotalKnowledgeNodeViews = KnowledgeNodeViewsCount
        };

        _context.DailySummaries.Add(summary);
        await _context.SaveChangesAsync();
    }

    public async Task<object> GetWeeklySummary()
    {
        var today = DateTime.UtcNow.Date;
        var startDate = today.AddDays(-7);

        // Retrieve summaries for the past 7 days
        var weeklySummaries = await _context.DailySummaries
            .Where(s => s.Date >= startDate && s.Date < today)
            .ToListAsync();

        // If no summaries exist, initialize empty values
        if (!weeklySummaries.Any())
        {
            return new
            {
                users = new { todayData = 0, growth = 0, dailyData = new List<int>() },
                loggedInUsers = new { todayData = 0, growth = 0, dailyData = new List<int>() },
                totalVisits = new { todayData = 0, growth = 0, dailyData = new List<int>() },
                dailyVisits = new { todayData = 0, growth = 0, dailyData = new List<int>() },
                newUsers = new { todayData = 0, growth = 0, dailyData = new List<int>() },
                returningUsers = new { todayData = 0, growth = 0, dailyData = new List<int>() },
                totalStudyGroups = new { todayData = 0, growth = 0, dailyData = new List<int>() },
                weeklyActiveStudyGroups = new { todayData = 0, growth = 0, dailyData = new List<int>() },
                totalKnowledgeNodes = new { todayData = 0, growth = 0, dailyData = new List<int>() },
                totalKnowledgeNodeViews = new { todayData = 0, growth = 0, dailyData = new List<int>() }
            };
        }

        // Today's data
        var todaySummary = await _context.DailySummaries
            .Where(s => s.Date == today)
            .FirstOrDefaultAsync();

        // Prepare daily data arrays for charts
        var dailyUsersData = weeklySummaries.Select(s => s.LoggedInUsers).ToList();
        var dailyVisitsData = weeklySummaries.Select(s => s.DailyVisits).ToList();
        var dailyNewUsersData = weeklySummaries.Select(s => s.NewUsers).ToList();
        var dailyReturningUsersData = weeklySummaries.Select(s => s.ReturningUsers).ToList();
        var dailyStudyGroupsData = weeklySummaries.Select(s => s.TotalStudyGroups).ToList();
        var dailyActiveStudyGroupsData = weeklySummaries.Select(s => s.WeeklyActiveStudyGroups).ToList();
        var dailyKnowledgeNodesData = weeklySummaries.Select(s => s.TotalKnowledgeNodes).ToList();
        var dailyKnowledgeNodeViewsData = weeklySummaries.Select(s => s.TotalKnowledgeNodeViews).ToList();

        // Calculate growth using data from the previous week
        var previousStartDate = startDate.AddDays(-7);
        var previousSummaries = await _context.DailySummaries
            .Where(s => s.Date >= previousStartDate && s.Date < startDate)
            .ToListAsync();

        // Handle empty previous summaries
        var previousTotalUsers = previousSummaries.Any() ? previousSummaries.Max(s => s.TotalUsers) : 0;
        var previousDailyVisits = previousSummaries.Any() ? previousSummaries.Sum(s => s.DailyVisits) : 0;
        var previousNewUsers = previousSummaries.Any() ? previousSummaries.Sum(s => s.NewUsers) : 0;
        var previousReturningUsers = previousSummaries.Any() ? previousSummaries.Sum(s => s.ReturningUsers) : 0;
        var previousStudyGroups = previousSummaries.Any() ? previousSummaries.Max(s => s.TotalStudyGroups) : 0;
        var previousActiveStudyGroups = previousSummaries.Any() ? previousSummaries.Max(s => s.WeeklyActiveStudyGroups) : 0;
        var previousKnowledgeNodes = previousSummaries.Any() ? previousSummaries.Max(s => s.TotalKnowledgeNodes) : 0;
        var previousKnowledgeNodeViews = previousSummaries.Any() ? previousSummaries.Sum(s => s.TotalKnowledgeNodeViews) : 0;

        // Calculate growth for each metric
        var growthUsers = ComputeGrowth(todaySummary?.LoggedInUsers ?? 0, previousTotalUsers);
        var growthVisits = ComputeGrowth(todaySummary?.DailyVisits ?? 0, previousDailyVisits);
        var growthNewUsers = ComputeGrowth(todaySummary?.NewUsers ?? 0, previousNewUsers);
        var growthReturningUsers = ComputeGrowth(todaySummary?.ReturningUsers ?? 0, previousReturningUsers);
        var growthStudyGroups = ComputeGrowth(todaySummary?.TotalStudyGroups ?? 0, previousStudyGroups);
        var growthActiveStudyGroups = ComputeGrowth(todaySummary?.WeeklyActiveStudyGroups ?? 0, previousActiveStudyGroups);
        var growthKnowledgeNodes = ComputeGrowth(todaySummary?.TotalKnowledgeNodes ?? 0, previousKnowledgeNodes);
        var growthKnowledgeNodeViews = ComputeGrowth(todaySummary?.TotalKnowledgeNodeViews ?? 0, previousKnowledgeNodeViews);

        // Return the aggregated data
        return new
        {
            users = new
            {
                todayData = todaySummary?.TotalUsers ?? 0,
                growth = growthUsers,
                dailyData = dailyUsersData
            },
            loggedInUsers = new
            {
                todayData = todaySummary?.LoggedInUsers ?? 0,
                growth = growthUsers,
                dailyData = dailyUsersData
            },
            totalVisits = new
            {
                todayData = todaySummary?.TotalVisits ?? 0,
                growth = growthVisits,
                dailyData = dailyVisitsData
            },
            dailyVisits = new
            {
                todayData = todaySummary?.DailyVisits ?? 0,
                growth = growthVisits,
                dailyData = dailyVisitsData
            },
            newUsers = new
            {
                todayData = todaySummary?.NewUsers ?? 0,
                growth = growthNewUsers,
                dailyData = dailyNewUsersData
            },
            returningUsers = new
            {
                todayData = todaySummary?.ReturningUsers ?? 0,
                growth = growthReturningUsers,
                dailyData = dailyReturningUsersData
            },
            totalStudyGroups = new
            {
                todayData = todaySummary?.TotalStudyGroups ?? 0,
                growth = growthStudyGroups,
                dailyData = dailyStudyGroupsData
            },
            weeklyActiveStudyGroups = new
            {
                todayData = todaySummary?.WeeklyActiveStudyGroups ?? 0,
                growth = growthActiveStudyGroups,
                dailyData = dailyActiveStudyGroupsData
            },
            totalKnowledgeNodes = new
            {
                todayData = todaySummary?.TotalKnowledgeNodes ?? 0,
                growth = growthKnowledgeNodes,
                dailyData = dailyKnowledgeNodesData
            },
            totalKnowledgeNodeViews = new
            {
                todayData = todaySummary?.TotalKnowledgeNodeViews ?? 0,
                growth = growthKnowledgeNodeViews,
                dailyData = dailyKnowledgeNodeViewsData
            }
        };
    }

    private double ComputeGrowth(int currentValue, int previousValue)
    {
        if (previousValue == 0)
        {
            return currentValue > 0 ? 100 : 0;
        }
        return ((double)(currentValue - previousValue) / previousValue) * 100;
    }
}
