using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Infrastructure.Data;

public sealed class AppDbContext : IdentityDbContext<IdentityUser, IdentityRole, string>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<PolicyVersion> PolicyVersions => Set<PolicyVersion>();
    public DbSet<PolicyChunk> PolicyChunks => Set<PolicyChunk>();
    public DbSet<PolicyChunkEmbedding> PolicyChunkEmbeddings => Set<PolicyChunkEmbedding>();
    public DbSet<CoverageItem> CoverageItems => Set<CoverageItem>();
    public DbSet<Exclusion> Exclusions => Set<Exclusion>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimDocument> ClaimDocuments => Set<ClaimDocument>();
    public DbSet<AdjudicationRun> AdjudicationRuns => Set<AdjudicationRun>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<ApprovalItem> ApprovalItems => Set<ApprovalItem>();
    public DbSet<ApprovalHistory> ApprovalHistories => Set<ApprovalHistory>();
    public DbSet<Anomaly> Anomalies => Set<Anomaly>();
    public DbSet<Decision> Decisions => Set<Decision>();
    public DbSet<DecisionLetter> DecisionLetters => Set<DecisionLetter>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TraceRecordEntity> TraceRecords => Set<TraceRecordEntity>();
    public DbSet<UsageRecordEntity> UsageRecords => Set<UsageRecordEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("vector");

        ConfigurePolicy(modelBuilder);
        ConfigureClaims(modelBuilder);
        ConfigureRuns(modelBuilder);
        ConfigureApproval(modelBuilder);
        ConfigureTrace(modelBuilder);
    }

    private static void ConfigurePolicy(ModelBuilder b)
    {
        b.Entity<Policy>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PolicyNumber).IsRequired().HasMaxLength(64);
            e.HasIndex(x => x.PolicyNumber).IsUnique();
            e.HasIndex(x => x.ProductLine);
            e.HasMany(x => x.Versions).WithOne(v => v.Policy).HasForeignKey(v => v.PolicyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Claims).WithOne().HasForeignKey(c => c.PolicyNumber)
                .HasPrincipalKey(x => x.PolicyNumber)
                .IsRequired(false);
        });

        b.Entity<PolicyVersion>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EffectiveDate).IsRequired();
            e.HasIndex(x => new { x.PolicyId, x.Version }).IsUnique();
            e.HasIndex(x => x.EffectiveDate);
            e.HasOne(x => x.Policy).WithMany(p => p.Versions).HasForeignKey(x => x.PolicyId);
            e.HasMany(x => x.Chunks).WithOne(c => c.PolicyVersion).HasForeignKey(c => c.PolicyVersionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.CoverageItems).WithOne(c => c.PolicyVersion).HasForeignKey(c => c.PolicyVersionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Exclusions).WithOne(x => x.PolicyVersion).HasForeignKey(x => x.PolicyVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PolicyChunk>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.Section).IsRequired().HasMaxLength(256);
            e.Property(x => x.Clause).IsRequired().HasMaxLength(256);
            e.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
            e.Property(x => x.Embedding)
                .HasConversion(
                    v => v == null ? null : new Pgvector.Vector(v),
                    v => v == null ? null : v.ToArray())
                .HasColumnType("vector(768)");
            e.HasIndex(x => new { x.PolicyVersionId, x.ContentHash }).IsUnique();
            e.HasIndex(x => new { x.PolicyVersionId, x.Section });
            e.HasMany(x => x.Embeddings).WithOne(x => x.PolicyChunk).HasForeignKey(x => x.PolicyChunkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PolicyChunkEmbedding>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).IsRequired().HasMaxLength(32);
            e.Property(x => x.Model).IsRequired().HasMaxLength(128);
            e.Property(x => x.Vector).IsRequired().HasConversion(
                v => new Pgvector.Vector(v), v => v.ToArray()).HasColumnType("vector(768)");
            e.HasIndex(x => new { x.PolicyChunkId, x.Provider, x.Model }).IsUnique();
            e.HasIndex(x => x.Provider);
        });

        b.Entity<CoverageItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).IsRequired().HasMaxLength(64);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.HasIndex(x => new { x.PolicyVersionId, x.Code }).IsUnique();
            e.HasOne(x => x.PolicyVersion).WithMany(v => v.CoverageItems).HasForeignKey(x => x.PolicyVersionId);
        });

        b.Entity<Exclusion>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).IsRequired().HasMaxLength(64);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.Description).IsRequired();
            e.HasIndex(x => new { x.PolicyVersionId, x.Code }).IsUnique();
            e.HasOne(x => x.PolicyVersion).WithMany(v => v.Exclusions).HasForeignKey(x => x.PolicyVersionId);
        });
    }

    private static void ConfigureClaims(ModelBuilder b)
    {
        b.Entity<Claim>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ClaimNumber).IsRequired().HasMaxLength(64);
            e.HasIndex(x => x.ClaimNumber).IsUnique();
            e.HasIndex(x => x.PolicyNumber);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasMany(x => x.Documents).WithOne(d => d.Claim).HasForeignKey(d => d.ClaimId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Runs).WithOne(r => r.Claim).HasForeignKey(r => r.ClaimId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ClaimDocument>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).IsRequired();
            e.Property(x => x.ContentType).IsRequired().HasMaxLength(128);
        });
    }

    private static void ConfigureRuns(ModelBuilder b)
    {
        b.Entity<AdjudicationRun>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.ClaimId);
            e.HasIndex(x => x.PolicyVersionId);
            e.HasMany(x => x.AgentRuns).WithOne(r => r.AdjudicationRun).HasForeignKey(r => r.AdjudicationRunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ApprovalItems).WithOne().HasForeignKey(i => i.RunId);
            e.HasMany(x => x.Anomalies).WithOne(a => a.AdjudicationRun).HasForeignKey(a => a.AdjudicationRunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Decisions).WithOne(d => d.AdjudicationRun).HasForeignKey(d => d.AdjudicationRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AgentRun>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AgentType).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.AdjudicationRunId);
        });

        b.Entity<Anomaly>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.AdjudicationRunId);
        });

        b.Entity<Decision>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DecisionType).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.AdjudicationRunId);
            e.HasOne(x => x.Letter).WithOne(l => l.Decision).HasForeignKey<DecisionLetter>(l => l.DecisionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DecisionLetter>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.LetterText).IsRequired();
        });
    }

    private static void ConfigureApproval(ModelBuilder b)
    {
        b.Entity<ApprovalItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.AssignedTo).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SLADeadline);
            e.HasIndex(x => x.AssignedTo);
            e.HasIndex(x => x.RunId);
            e.HasMany(x => x.History).WithOne(h => h.ApprovalItem).HasForeignKey(h => h.ApprovalItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ApprovalHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.ApprovalItemId);
            e.HasIndex(x => x.ReviewerId);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).IsRequired().HasMaxLength(64);
            e.Property(x => x.Action).IsRequired();
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.RunId);
            e.HasIndex(x => x.CreatedAt);
        });
    }

    private static void ConfigureTrace(ModelBuilder b)
    {
        b.Entity<TraceRecordEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RunId).IsRequired();
            e.Property(x => x.EntityType).IsRequired();
            e.Property(x => x.Action).IsRequired();
            e.HasIndex(x => x.RunId);
        });

        b.Entity<UsageRecordEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Scope).IsRequired();
            e.Property(x => x.Provider).IsRequired();
            e.Property(x => x.Model).IsRequired();
            e.HasIndex(x => x.RunId);
        });
    }
}

public sealed class TraceRecordEntity
{
    public Guid Id { get; set; }
    public required string RunId { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public required string Action { get; set; }
    public string? ActorId { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? Message { get; set; }
}

public sealed class UsageRecordEntity
{
    public Guid Id { get; set; }
    public required string Scope { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? RunId { get; set; }
    public string? CorrelationId { get; set; }
}
