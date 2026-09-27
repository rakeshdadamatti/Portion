using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;
using Portion.Domain.Entities;

namespace Portion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ResumeChunk" /> onto the frozen <c>ResumeChunks</c> table.
/// </summary>
/// <remarks>
/// The embedding is the one genuinely provider-dependent column. On PostgreSQL it maps to a native
/// <c>vector(768)</c> so that <c>CosineDistance</c> can be pushed into SQL. On SQLite there is no
/// vector type, so the <c>float[]</c> is stored as a JSON array in a TEXT column, matching the
/// original schema and keeping SQLite a genuinely zero-configuration fallback.
/// </remarks>
public sealed class ResumeChunkConfiguration : IEntityTypeConfiguration<ResumeChunk>
{
    /// <summary>Dimension of the embedding vectors produced by the configured embedding model.</summary>
    public const int EmbeddingDimensions = 768;

    private readonly bool _usePostgresVector;

    /// <summary>Creates the configuration for a known provider.</summary>
    public ResumeChunkConfiguration(bool usePostgresVector) => _usePostgresVector = usePostgresVector;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ResumeChunk> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ResumeChunks");

        builder.HasKey(c => c.Id).HasName("PK_ResumeChunks");

        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.TextContent).IsRequired();
        builder.Property(c => c.ChunkIndex).IsRequired();

        if (_usePostgresVector)
        {
            builder.Property(c => c.Embedding).HasColumnType($"vector({EmbeddingDimensions})");
        }
        else
        {
            builder.Property(c => c.Embedding)
                   .HasColumnType("TEXT")
                   .HasConversion(
                       // Expression trees cannot contain pattern-matching operators, so these are
                       // written as plain comparisons.
                       vector => vector == null ? null : JsonSerializer.Serialize(vector.ToArray()),
                       json => string.IsNullOrEmpty(json) ? null : new Vector(JsonSerializer.Deserialize<float[]>(json)!));
        }

        builder.HasOne(c => c.Resume)
               .WithMany(r => r.Chunks)
               .HasForeignKey(c => c.ResumeId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.ResumeId, c.ChunkIndex })
               .HasDatabaseName("IX_ResumeChunks_ResumeId_ChunkIndex");
    }
}
