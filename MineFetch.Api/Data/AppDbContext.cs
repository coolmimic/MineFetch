using Microsoft.EntityFrameworkCore;
using MineFetch.Entities.Models;

namespace MineFetch.Api.Data;

/// <summary>
/// 应用数据库上下文
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<LotteryResult> LotteryResults => Set<LotteryResult>();
    public DbSet<TelegramGroup> TelegramGroups => Set<TelegramGroup>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // LotteryResult
        modelBuilder.Entity<LotteryResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PeriodId).IsUnique();
            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => e.CreatedAt);
            
            entity.Property(e => e.PeriodId).HasMaxLength(50).IsRequired();
            
            // 忽略计算属性
            entity.Ignore(e => e.Size);
            entity.Ignore(e => e.Parity);

            entity.HasOne(e => e.Group)
                  .WithMany(g => g.LotteryResults)
                  .HasForeignKey(e => e.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // TelegramGroup
        modelBuilder.Entity<TelegramGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
        });

        // User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ChatId);
            entity.Property(e => e.Username).HasMaxLength(100);
            entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastName).HasMaxLength(100);
        });

        // UserSetting
        modelBuilder.Entity<UserSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.GroupId, e.RuleType, e.BetType }).IsUnique();
            
            entity.HasOne(e => e.User)
                  .WithMany(u => u.Settings)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Group)
                  .WithMany(g => g.UserSettings)
                  .HasForeignKey(e => e.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
