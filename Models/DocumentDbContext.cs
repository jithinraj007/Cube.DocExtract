using Microsoft.EntityFrameworkCore;

namespace TaskOne.Models
{
    public class DocumentDbContext : DbContext
    {
        public DocumentDbContext(DbContextOptions<DocumentDbContext> options) : base(options)
        {
        }

        public DbSet<UploadedDocument> UploadedDocuments { get; set; }
        public DbSet<DocumentMetadata> DocumentMetadata { get; set; }
        public DbSet<LineItem> LineItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure one-to-one relationship between UploadedDocument and DocumentMetadata
            modelBuilder.Entity<UploadedDocument>()
                .HasOne(d => d.Metadata)
                .WithOne(m => m.Document)
                .HasForeignKey<DocumentMetadata>(m => m.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure one-to-many relationship between UploadedDocument and LineItem
            modelBuilder.Entity<UploadedDocument>()
                .HasMany(d => d.LineItems)
                .WithOne(l => l.Document)
                .HasForeignKey(l => l.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<LineItem>()
                .HasOne(l => l.Metadata)
                .WithMany()
                .HasForeignKey(l => l.MetadataId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
