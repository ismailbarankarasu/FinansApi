using Microsoft.EntityFrameworkCore;

namespace FinansApi.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        ChangeTracker.Tracked += CaptureOriginalValues;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<TransactionEntry> Transactions => Set<TransactionEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        AccountingModel.Configure(modelBuilder);
        modelBuilder.Entity<Category>().HasIndex(x => new { x.CompanyId, x.Name, x.Type }).IsUnique();
        modelBuilder.Entity<Category>().HasOne<FinansApi.Data.Accounting.Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TransactionEntry>().HasOne<FinansApi.Data.Accounting.Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TransactionEntry>().HasOne<FinansApi.Data.Accounting.FiscalPeriod>().WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(x => x.Email).IsUnique();
                entity.Property(x => x.Email).HasMaxLength(160);
                entity.Property(x => x.FullName).HasMaxLength(120);
            });
        modelBuilder.Entity<Category>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(80);
                entity.HasOne(x => x.User).WithMany(x => x.Categories).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            });
        modelBuilder.Entity<TransactionEntry>(entity =>
            {
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.Description).HasMaxLength(250);
                entity.HasOne(x => x.User).WithMany(x => x.Transactions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(x => x.Category).WithMany(x => x.Transactions).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            });
    }

}
