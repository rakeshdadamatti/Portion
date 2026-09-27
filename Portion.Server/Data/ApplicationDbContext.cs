using Microsoft.EntityFrameworkCore;
using Portion.Server.Entities;
using System.Text.Json;

namespace Portion.Server.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Resume> Resumes => Set<Resume>();
        public DbSet<ResumeChunk> ResumeChunks => Set<ResumeChunk>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var isPostgres = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false;

            if (isPostgres)
            {
                modelBuilder.HasPostgresExtension("vector");
            }

            modelBuilder.Entity<ResumeChunk>(entity =>
            {
                entity.HasKey(e => e.Id);

                if (isPostgres)
                {
                    entity.Property(e => e.Embedding).HasColumnType("vector(768)");
                }
                else
                {
                    // For SQLite, store float array serialized as JSON string
                    entity.Property(e => e.Embedding)
                        .HasConversion(
                            v => v == null ? null : JsonSerializer.Serialize(v.ToArray(), (JsonSerializerOptions?)null),
                            v => string.IsNullOrEmpty(v) ? null : new Pgvector.Vector(JsonSerializer.Deserialize<float[]>(v, (JsonSerializerOptions?)null)!)
                        );
                }

                entity.HasOne(e => e.Resume)
                      .WithMany(r => r.Chunks)
                      .HasForeignKey(e => e.ResumeId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
