using ClubcardLoyalty.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubcardLoyalty.Api.Data;

public class LoyaltyDbContext : DbContext
{
    public LoyaltyDbContext(DbContextOptions<LoyaltyDbContext> options) : base(options)
    {
    }

    public DbSet<ClubcardAccount> ClubcardAccounts => Set<ClubcardAccount>();
    public DbSet<PointsTransaction> PointsTransactions => Set<PointsTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClubcardAccount>(e =>
        {
            e.HasKey(a => a.CardId);
        });

        modelBuilder.Entity<PointsTransaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => new { t.CardId, t.IdempotencyKey }).IsUnique();
            e.HasIndex(t => t.CardId); // под выборку истории транзакций по карте
        });

        base.OnModelCreating(modelBuilder);
    }
}
