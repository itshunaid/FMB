using Microsoft.EntityFrameworkCore;
using FMB.Domain.Entities;

namespace FMB.Application.Interfaces
{
    public interface ITiffinTrackerDbContext
    {
        DbSet<Household> Households { get; }
        DbSet<Distributor> Distributors { get; }
        DbSet<DailyMenu> DailyMenus { get; }
        DbSet<TiffinLog> TiffinLogs { get; }
        DbSet<ExpenseRecord> ExpenseRecords { get; }
        DbSet<AuditLog> AuditLogs { get; }
        DbSet<QueryRecord> QueryRecords { get; }
        DbSet<FeedbackRecord> FeedbackRecords { get; }
        DbSet<SystemUser> SystemUsers { get; }
        DbSet<RoleDefinition> RoleDefinitions { get; }
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
