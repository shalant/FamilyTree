using FamilyTree.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyTree.Core.Data;

public partial class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Family> Families => Set<Family>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Relationship> Relationships => Set<Relationship>();
    public DbSet<Medium> Media => Set<Medium>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // FAMILY
        modelBuilder.Entity<Family>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasDefaultValueSql("newsequentialid()");
            e.Property(f => f.Name).HasMaxLength(200).IsRequired();
            e.Property(f => f.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // PERSON
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

            // IMPORTANT: store Gender enum as string
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

        // RELATIONSHIP
        modelBuilder.Entity<Relationship>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("newsequentialid()");

            // IMPORTANT: store RelationshipType enum as string
            e.Property(r => r.Type)
                .HasConversion<string>()
                .HasMaxLength(20);

            e.Property(r => r.Notes).HasMaxLength(1000);

            // StartDate / EndDate are fine with no config
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

        // MEDIUM
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
    }
}
