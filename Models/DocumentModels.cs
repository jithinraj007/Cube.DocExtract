using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TaskOne.Models
{
    public class UploadedDocument
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string FileType { get; set; } = string.Empty; // "PDF", "Scanned PDF", "Image", "Excel"

        [Required]
        [StringLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        public DateTime UploadDate { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = DocumentStatus.Pending; // Pending, Processing, Completed, Failed

        public string? ErrorMessage { get; set; }

        public int? ProcessingTimeMs { get; set; }

        public string? RawText { get; set; }

        // Navigation properties
        public virtual DocumentMetadata? Metadata { get; set; }
        public virtual ICollection<LineItem> LineItems { get; set; } = new List<LineItem>();
    }

    public class DocumentMetadata
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int DocumentId { get; set; }

        [ForeignKey("DocumentId")]
        [JsonIgnore]
        public virtual UploadedDocument? Document { get; set; }

        [StringLength(100)]
        public string? PoNumber { get; set; }

        public string? VendorDetails { get; set; }

        public DateTime? PoDate { get; set; }

        public DateTime? DeliveryDate { get; set; }

        public string? DeliverTo { get; set; }
    }

    public class LineItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int DocumentId { get; set; }

        [ForeignKey("DocumentId")]
        [JsonIgnore]
        public virtual UploadedDocument? Document { get; set; }

        [StringLength(100)]
        public string? PoNumber { get; set; }

        public string? Item { get; set; }

        [Column(TypeName = "decimal(18, 4)")]
        public decimal? Quantity { get; set; }

        [Column(TypeName = "decimal(18, 4)")]
        public decimal? Rate { get; set; }

        [Column(TypeName = "decimal(18, 4)")]
        public decimal? TaxPercent { get; set; }

        [Column(TypeName = "decimal(18, 4)")]
        public decimal? TaxAmount { get; set; }

        [Column(TypeName = "decimal(18, 4)")]
        public decimal? Amount { get; set; }
    }

    public static class DocumentStatus
    {
        public const string Pending = "Pending";
        public const string Processing = "Processing";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
    }
}
