using AutoClient.Data;
using AutoClient.DTOs.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace AutoClient.Services;

/// <summary>
/// Service responsible for aggregating and computing dashboard metrics
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _context;

    public DashboardService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(
        Guid workshopId,
        DateTime from,
        DateTime to,
        Guid? workerId = null)
    {
        // Normalize dates: cap upper bound at current UTC time
        var nowUtc = DateTime.UtcNow;
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        // Ensure 'to' does not exceed current time
        if (toUtc > nowUtc)
        {
            toUtc = nowUtc;
        }

        // Base query for all services in the workshop
        var baseQuery = _context.Services
            .AsNoTracking()
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .Include(s => s.Worker)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId);

        // Apply worker filter if specified
        if (workerId.HasValue)
        {
            baseQuery = baseQuery.Where(s => s.WorkerId == workerId.Value);
        }

        // Define service status categories
        // Completed: Has ExitDate or Cost (tolerant definition)
        var completedQuery = baseQuery.Where(s => s.ExitDate != null || s.Cost != null);

        // Pending: No ExitDate and no Cost - filter within the normalized date range
        var pendingQuery = baseQuery
            .Where(s => s.ExitDate == null && s.Cost == null)
            .Where(s => s.Date >= fromUtc && s.Date <= toUtc);

        // Filter completed services by date range (using ExitDate if available, otherwise Date)
        var completedInRangeQuery = completedQuery.Where(s =>
            ((s.ExitDate ?? s.Date) >= fromUtc) && ((s.ExitDate ?? s.Date) <= toUtc));

        // 1. Service counts (database-level aggregation)
        var completedCount = await completedInRangeQuery.CountAsync();
        var pendingCount = await pendingQuery.CountAsync();

        // In-progress: Pending services with assigned worker
        var inProgressCount = await pendingQuery
            .Where(s => s.WorkerId != null)
            .CountAsync();

        // Overdue: Pending services older than 7 days (configurable threshold)
        var overdueCutoff = DateTime.UtcNow.AddDays(-7);
        var overdueCount = await pendingQuery
            .Where(s => s.Date < overdueCutoff)
            .CountAsync();

        // 2. Financial metrics
        var totalRevenue = await completedInRangeQuery
            .Select(s => (decimal?)(s.Cost ?? 0))
            .SumAsync() ?? 0;

        var averageTicketValue = completedCount > 0
            ? totalRevenue / completedCount
            : 0;

        // 3. Time-based metrics - Average days to complete
        var completedWithDates = await completedInRangeQuery
            .Where(s => s.ExitDate != null)
            .Select(s => new
            {
                EntryDate = s.Date,
                ExitDate = s.ExitDate!.Value
            })
            .ToListAsync();

        var averageDaysToComplete = completedWithDates.Any()
            ? completedWithDates.Average(s => (s.ExitDate - s.EntryDate).TotalDays)
            : 0;

        // 4. Top worker (most completed services in range)
        var topWorker = await completedInRangeQuery
            .Where(s => s.WorkerId != null)
            .GroupBy(s => new { s.WorkerId, s.Worker!.Name })
            .Select(g => new
            {
                Name = g.Key.Name,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .FirstOrDefaultAsync();

        // 5. Top services (most frequent service types in range)
        var topServices = await completedInRangeQuery
            .GroupBy(s => s.ServiceType)
            .Select(g => new ServiceTypeCount
            {
                ServiceType = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync();

        // 6. Next actions - Services requiring immediate attention
        var nextActions = await pendingQuery
            .OrderBy(s => s.Date) // Oldest first
            .Take(10) // Limit to 10 most urgent
            .Select(s => new NextActionDto
            {
                ServiceId = s.Id,
                PlateNumber = s.Vehicle.PlateNumber,
                ClientName = s.Vehicle.Client.Name,
                ServiceName = s.ServiceType,
                Status = s.WorkerId != null ? "En progreso" : "Pendiente",
                DaysOpen = (int)(DateTime.UtcNow - s.Date).TotalDays,
                EntryDate = s.Date,
                ExitDate = s.ExitDate,
                ExpectedDate = s.NextServiceDate
            })
            .ToListAsync();

        // Build and return the summary
        return new DashboardSummaryDto
        {
            From = from,
            To = to,
            CompletedCount = completedCount,
            PendingCount = pendingCount,
            InProgressCount = inProgressCount,
            OverdueCount = overdueCount,
            TotalRevenue = totalRevenue,
            AverageTicketValue = averageTicketValue,
            AverageDaysToComplete = averageDaysToComplete,
            TopWorkerName = topWorker?.Name,
            TopWorkerServiceCount = topWorker?.Count ?? 0,
            TopServices = topServices,
            NextActions = nextActions
        };
    }
}
