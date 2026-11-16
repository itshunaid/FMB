using MediatR;
using Microsoft.EntityFrameworkCore;
using FMB.Application.Interfaces;

namespace FMB.Application.Queries
{
    public record DashboardStatsDto(
        int TotalActive,
        int Collected,
        int Pending,
        int OnHold,
        int CollectionRatePercent,
        string TodayMenu
    );

    public record GetDashboardStatsQuery(DateTime Date) : IRequest<DashboardStatsDto>;

    public class GetDashboardStatsQueryHandler : IRequestHandler<GetDashboardStatsQuery, DashboardStatsDto>
    {
        private readonly ITiffinTrackerDbContext _context;

        public GetDashboardStatsQueryHandler(ITiffinTrackerDbContext context)
        {
            _context = context;
        }

        public async Task<DashboardStatsDto> Handle(GetDashboardStatsQuery request, CancellationToken cancellationToken)
        {
            var targetDate = request.Date.Date;
            var totalHouseholds = await _context.Households
                .CountAsync(h => h.Status != null && h.Status.Trim().ToLower() == "active", cancellationToken);
            var onHold = await _context.Households
                .CountAsync(h => h.Status != null && h.Status.Trim().ToLower() == "on hold", cancellationToken);
            var collected = await (
                from log in _context.TiffinLogs
                join household in _context.Households on log.HouseholdId equals household.Id
                where log.Date >= targetDate
                    && log.Date < targetDate.AddDays(1)
                    && log.Status != null
                    && log.Status.Trim().ToLower() == "collected"
                    && household.Status != null
                    && household.Status.Trim().ToLower() == "active"
                select log.HouseholdId)
                .Distinct()
                .CountAsync(cancellationToken);
            var pending = Math.Max(0, totalHouseholds - collected);
            var rate = totalHouseholds > 0 ? (int)Math.Round((double)collected / totalHouseholds * 100) : 0;

            var menu = await _context.DailyMenus.FirstOrDefaultAsync(m => m.Date == targetDate, cancellationToken);

            return new DashboardStatsDto(
                totalHouseholds,
                collected,
                pending,
                onHold,
                rate,
                string.IsNullOrWhiteSpace(menu?.MenuName)
                    ? "No menu recorded for this date"
                    : menu.MenuName
            );
        }
    }
}
