using Microsoft.EntityFrameworkCore;
using FMB.Application.Interfaces;
using FMB.Domain.Entities;

namespace FMB.Infrastructure.Persistence
{
    public class TiffinTrackerDbContext : DbContext, ITiffinTrackerDbContext
    {
        public TiffinTrackerDbContext(DbContextOptions<TiffinTrackerDbContext> options) : base(options) { }

        public DbSet<Household> Households => Set<Household>();
        public DbSet<Distributor> Distributors => Set<Distributor>();
        public DbSet<DailyMenu> DailyMenus => Set<DailyMenu>();
        public DbSet<TiffinLog> TiffinLogs => Set<TiffinLog>();
        public DbSet<ExpenseRecord> ExpenseRecords => Set<ExpenseRecord>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<QueryRecord> QueryRecords => Set<QueryRecord>();
        public DbSet<FeedbackRecord> FeedbackRecords => Set<FeedbackRecord>();
        public DbSet<SystemUser> SystemUsers => Set<SystemUser>();
        public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Household>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasMaxLength(20);
                entity.HasIndex(e => e.ThaliNumber).IsUnique();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(150);
                entity.Property(e => e.TagId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.PasswordHash).HasMaxLength(500);
                entity.HasIndex(e => e.TagId).IsUnique();
            });

            modelBuilder.Entity<Distributor>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasMaxLength(20);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(150);
            });

            modelBuilder.Entity<Household>()
                .HasOne(household => household.Distributor)
                .WithMany(distributor => distributor.Households)
                .HasForeignKey(household => household.DistributorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DailyMenu>(entity =>
            {
                entity.HasKey(e => e.Date);
            });

            modelBuilder.Entity<ExpenseRecord>(entity =>
            {
                entity.HasKey(e => e.Date);
                entity.Property(e => e.Groceries).HasPrecision(18, 2);
                entity.Property(e => e.Meat).HasPrecision(18, 2);
                entity.Property(e => e.Gas).HasPrecision(18, 2);
                entity.Property(e => e.Labour).HasPrecision(18, 2);
                entity.Property(e => e.Packaging).HasPrecision(18, 2);
                entity.Property(e => e.Other).HasPrecision(18, 2);
                entity.Property(e => e.TotalExpense).HasPrecision(18, 2);
            });

            modelBuilder.Entity<TiffinLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.HouseholdId).HasMaxLength(20);
                entity.HasIndex(e => new { e.Date, e.HouseholdId }).IsUnique();
            });

            modelBuilder.Entity<FeedbackRecord>(entity =>
            {
                entity.HasKey(record => record.Id);
                entity.Property(record => record.MenuName).IsRequired().HasMaxLength(200);
                entity.Property(record => record.ItsId).IsRequired().HasMaxLength(20);
                entity.Property(record => record.MemberName).IsRequired().HasMaxLength(150);
                entity.Property(record => record.Zone).IsRequired().HasMaxLength(100);
                entity.Property(record => record.Quantity).IsRequired().HasMaxLength(30);
                entity.Property(record => record.Quality).IsRequired().HasMaxLength(30);
                entity.Property(record => record.Comment).HasMaxLength(1000);
                entity.HasIndex(record => new { record.Date, record.ItsId });
            });

            modelBuilder.Entity<SystemUser>(entity =>
            {
                entity.HasKey(user => user.ItsId);
                entity.Property(user => user.ItsId).HasMaxLength(20);
                entity.Property(user => user.Name).IsRequired().HasMaxLength(150);
                entity.Property(user => user.Role).IsRequired().HasMaxLength(50);
                entity.Property(user => user.Phone).IsRequired().HasMaxLength(30);
                entity.Property(user => user.Status).IsRequired().HasMaxLength(20);
                entity.Property(user => user.PinHash).IsRequired().HasMaxLength(500);
            });

            modelBuilder.Entity<RoleDefinition>(entity =>
            {
                entity.HasKey(role => role.Name);
                entity.Property(role => role.Name).IsRequired().HasMaxLength(50);
                entity.Property(role => role.PermissionsJson).IsRequired();
                entity.Property(role => role.DashboardWidgetsJson).IsRequired();
            });
        }
    }
}
