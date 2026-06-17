using FamilyTree.Core.Models;
using FamilyTree.Shared.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Core.Data;

public partial class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Family> Families => Set<Family>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Relationship> Relationships => Set<Relationship>();
    public DbSet<Medium> Media => Set<Medium>();
    public DbSet<AppUser> AppUsers => Users;   // alias — code that uses ctx.AppUsers still works
    public DbSet<UserFamily> UserFamilies => Set<UserFamily>();
    public DbSet<UserInvite> UserInvites => Set<UserInvite>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserActivity> UserActivities => Set<UserActivity>();
    public DbSet<PasswordResetRequest> PasswordResetRequests => Set<PasswordResetRequest>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<UserMessage> UserMessages => Set<UserMessage>();
    public DbSet<Story> Stories => Set<Story>();
    public DbSet<StoryInvite> StoryInvites => Set<StoryInvite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Global Query Filters (soft delete) ────────────────────────────────
        modelBuilder.Entity<Person>().HasQueryFilter(p => p.DeletedAt == null);
        modelBuilder.Entity<Relationship>().HasQueryFilter(r => r.DeletedAt == null);
        modelBuilder.Entity<Medium>().HasQueryFilter(m => m.DeletedAt == null);

        // ── FAMILY ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Family>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasDefaultValueSql("newsequentialid()");
            e.Property(f => f.Name).HasMaxLength(200).IsRequired();
            e.Property(f => f.IsPublic).HasDefaultValue(false);
            e.Property(f => f.RequireApproval).HasDefaultValue(true);
            e.Property(f => f.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // ── PERSON ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Person>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasDefaultValueSql("newsequentialid()");

            e.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
            e.Property(p => p.LastName).HasMaxLength(100).IsRequired();
            e.Property(p => p.MiddleName).HasMaxLength(100);
            e.Property(p => p.MaidenName).HasMaxLength(100);
            e.Property(p => p.BirthPlace).HasMaxLength(200);
            e.Property(p => p.DeathPlace).HasMaxLength(200);
            e.Property(p => p.BiographyNotes).HasMaxLength(5000);
            e.Property(p => p.ProfilePhotoUrl).HasMaxLength(500);

            e.Property(p => p.Gender)
                .HasConversion<string>()
                .HasMaxLength(20);

            e.Property(p => p.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            e.Property(p => p.RowVersion).IsRowVersion();

            e.HasOne(p => p.Family)
                .WithMany(f => f.People)
                .HasForeignKey(p => p.FamilyId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── RELATIONSHIP ──────────────────────────────────────────────────────
        modelBuilder.Entity<Relationship>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("newsequentialid()");

            e.Property(r => r.Type)
                .HasConversion<string>()
                .HasMaxLength(20);

            e.Property(r => r.Notes).HasMaxLength(1000);
            e.Property(r => r.StartDate);
            e.Property(r => r.EndDate);

            e.Property(r => r.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            e.Property(r => r.RowVersion).IsRowVersion();

            e.HasIndex(r => new { r.PersonAId, r.PersonBId, r.Type }).IsUnique();

            e.HasOne(r => r.PersonA)
                .WithMany(p => p.RelationshipPersonAs)
                .HasForeignKey(r => r.PersonAId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.PersonB)
                .WithMany(p => p.RelationshipPersonBs)
                .HasForeignKey(r => r.PersonBId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── MEDIUM ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Medium>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("newsequentialid()");

            e.Property(m => m.Url).HasMaxLength(500).IsRequired();
            e.Property(m => m.FileName).HasMaxLength(255).IsRequired();
            e.Property(m => m.Caption).HasMaxLength(500);
            e.Property(m => m.Type).HasMaxLength(20);
            e.Property(m => m.MimeType).HasMaxLength(100);

            e.Property(m => m.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            e.Property(m => m.RowVersion).IsRowVersion();

            e.HasOne(m => m.Person)
                .WithMany(p => p.Media)
                .HasForeignKey(m => m.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── APP USER ──────────────────────────────────────────────────────────
        // Identity manages: Id, Email (unique index), UserName, PasswordHash, etc.
        modelBuilder.Entity<AppUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(200);
            e.Property(u => u.FeatureFlags).HasColumnType("nvarchar(max)");
            e.Property(u => u.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            e.Property(u => u.PersonClaimStatus)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(PersonClaimStatus.None);

            // One person node can only be claimed by one user
            e.HasIndex(u => u.PersonId)
                .IsUnique()
                .HasFilter("[PersonId] IS NOT NULL");

            e.HasOne(u => u.Person)
                .WithMany()
                .HasForeignKey(u => u.PersonId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── USER FAMILY ───────────────────────────────────────────────────────
        modelBuilder.Entity<UserFamily>(e =>
        {
            e.HasKey(uf => new { uf.UserId, uf.FamilyId });
            e.Property(uf => uf.Role).HasMaxLength(20).IsRequired();
            e.Property(uf => uf.JoinedAt).HasDefaultValueSql("GETUTCDATE()");

            e.HasOne(uf => uf.User)
                .WithMany(u => u.UserFamilies)
                .HasForeignKey(uf => uf.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(uf => uf.Family)
                .WithMany()
                .HasForeignKey(uf => uf.FamilyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── USER INVITE ───────────────────────────────────────────────────────
        modelBuilder.Entity<UserInvite>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasDefaultValueSql("newsequentialid()");
            e.Property(i => i.Email).HasMaxLength(256).IsRequired();
            e.Property(i => i.RoleToGrant).HasMaxLength(20).IsRequired();
            e.Property(i => i.Token).HasMaxLength(128).IsRequired();
            e.Property(i => i.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            e.HasIndex(i => i.Token).IsUnique();

            e.HasOne(i => i.Family)
                .WithMany()
                .HasForeignKey(i => i.FamilyId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(i => i.CreatedByUser)
                .WithMany(u => u.SentInvites)
                .HasForeignKey(i => i.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── AUDIT LOG ─────────────────────────────────────────────────────────
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasDefaultValueSql("newsequentialid()");
            e.Property(a => a.Action).HasMaxLength(50).IsRequired();
            e.Property(a => a.EntityType).HasMaxLength(50).IsRequired();
            e.Property(a => a.IpAddress).HasMaxLength(45);
            e.Property(a => a.OldValue).HasColumnType("nvarchar(max)");
            e.Property(a => a.NewValue).HasColumnType("nvarchar(max)");
            e.Property(a => a.Timestamp).HasDefaultValueSql("GETUTCDATE()");
            e.HasIndex(a => a.Timestamp);

            e.HasOne(a => a.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── USER ACTIVITY ─────────────────────────────────────────────────────
        modelBuilder.Entity<UserActivity>(e =>
        {
            e.HasKey(ua => ua.Id);
            e.Property(ua => ua.Id).HasDefaultValueSql("newsequentialid()");
            e.HasIndex(ua => new { ua.UserId, ua.Date }).IsUnique();

            e.HasOne(ua => ua.User)
                .WithMany(u => u.Activities)
                .HasForeignKey(ua => ua.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── PASSWORD RESET REQUEST ────────────────────────────────────────────
        modelBuilder.Entity<PasswordResetRequest>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("newsequentialid()");
            e.Property(r => r.Email).HasMaxLength(256).IsRequired();
            e.Property(r => r.Token).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(r => r.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // ── IMPORT BATCH ──────────────────────────────────────────────────────
        modelBuilder.Entity<ImportBatch>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Id).HasDefaultValueSql("newsequentialid()");
            e.Property(b => b.Note).HasMaxLength(500);
            e.Property(b => b.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // ── USER MESSAGE ──────────────────────────────────────────────────────
        modelBuilder.Entity<UserMessage>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("newsequentialid()");
            e.Property(m => m.Type).HasMaxLength(50).IsRequired();
            e.Property(m => m.SenderEmail).HasMaxLength(256);
            e.Property(m => m.Title).HasMaxLength(200).IsRequired();
            e.Property(m => m.Body).HasMaxLength(2000);
            e.Property(m => m.Category).HasMaxLength(100);
            e.Property(m => m.Priority).HasMaxLength(100);
            e.Property(m => m.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            e.ToTable("UserMessages");
        });

        modelBuilder.Entity<Person>(e =>
        {
            e.HasOne(p => p.ImportBatch)
                .WithMany()
                .HasForeignKey(p => p.ImportBatchId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── STORY ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Story>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasDefaultValueSql("newsequentialid()");

            e.Property(s => s.UnlinkedPersonName).HasMaxLength(200);
            e.Property(s => s.AuthorName).HasMaxLength(200);
            e.Property(s => s.Title).HasMaxLength(300).IsRequired();
            e.Property(s => s.Body).HasMaxLength(10000).IsRequired();

            e.Property(s => s.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            e.HasOne(s => s.Person)
                .WithMany()
                .HasForeignKey(s => s.PersonId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(s => s.Author)
                .WithMany()
                .HasForeignKey(s => s.AuthorId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(s => s.Invite)
                .WithMany()
                .HasForeignKey(s => s.InviteId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── STORY INVITE ─────────────────────────────────────────────────────
        modelBuilder.Entity<StoryInvite>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasDefaultValueSql("newsequentialid()");

            e.Property(i => i.Token).HasMaxLength(128).IsRequired();
            e.HasIndex(i => i.Token).IsUnique();

            e.Property(i => i.UnlinkedPersonName).HasMaxLength(200);
            e.Property(i => i.InvitedEmail).HasMaxLength(256).IsRequired();
            e.Property(i => i.PersonalNote).HasMaxLength(1000);

            e.Property(i => i.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            e.HasOne(i => i.Person)
                .WithMany()
                .HasForeignKey(i => i.PersonId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(i => i.InvitedByUser)
                .WithMany()
                .HasForeignKey(i => i.InvitedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
