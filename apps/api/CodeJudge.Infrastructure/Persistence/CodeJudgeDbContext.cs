using CodeJudge.Domain.Entities;
using CodeJudge.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CodeJudge.Infrastructure.Persistence;

public class CodeJudgeDbContext(DbContextOptions<CodeJudgeDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Problem> Problems => Set<Problem>();
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<Submission> Submissions => Set<Submission>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.EntraObjectId).IsRequired();
            e.Property(x => x.EntraTenantId).IsRequired();

            // Identity is the (tid, oid) pair. A unique index on oid alone would
            // eventually collide across tenants.
            e.HasIndex(x => new { x.EntraTenantId, x.EntraObjectId }).IsUnique();
        });

        b.Entity<Problem>(e =>
        {
            e.ToTable("problems");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Difficulty).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Slug).HasMaxLength(128);
            e.Property(x => x.Title).HasMaxLength(256);

            e.HasMany(x => x.TestCases)
             .WithOne(x => x.Problem)
             .HasForeignKey(x => x.ProblemId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TestCase>(e =>
        {
            e.ToTable("test_cases");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProblemId, x.Ordinal }).IsUnique();
        });

        b.Entity<Submission>(e =>
        {
            e.ToTable("submissions");
            e.HasKey(x => x.Id);

            // Stored by name. Persisting the ordinal would mean that reordering the enum
            // silently rewrites the meaning of every historical row.
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Language).HasMaxLength(32);

            e.HasIndex(x => new { x.UserId, x.ProblemId, x.CreatedAt })
             .IsDescending(false, false, true);

            e.HasOne(x => x.User)
             .WithMany(x => x.Submissions)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Problem)
             .WithMany()
             .HasForeignKey(x => x.ProblemId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // snake_case every column so the schema reads naturally in psql.
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                sb.Append(name[i]);
            }
        }
        return sb.ToString();
    }
}
