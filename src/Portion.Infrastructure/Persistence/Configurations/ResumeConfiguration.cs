using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portion.Domain.Entities;

namespace Portion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Resume" /> onto the frozen <c>Resumes</c> table.
/// </summary>
/// <remarks>
/// Property names, types and nullability are deliberately unchanged from the original
/// hand-created schema: <c>Id</c> TEXT, <c>CandidateName</c>/<c>FilePath</c>/<c>FileHash</c> TEXT
/// NOT NULL, <c>Status</c> INTEGER, <c>FailureReason</c> TEXT NULL, and both timestamps TEXT NOT NULL.
/// Adding a column here would require a migration of every existing developer database.
/// </remarks>
public sealed class ResumeConfiguration : IEntityTypeConfiguration<Resume>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Resume> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Resumes");

        builder.HasKey(r => r.Id).HasName("PK_Resumes");

        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.CandidateName).IsRequired();
        builder.Property(r => r.FilePath).IsRequired();
        builder.Property(r => r.FileHash).IsRequired();
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.LastSyncedAt).IsRequired();

        builder.HasIndex(r => r.FileHash).HasDatabaseName("IX_Resumes_FileHash");
        builder.HasIndex(r => r.CreatedAt).HasDatabaseName("IX_Resumes_CreatedAt");
    }
}
