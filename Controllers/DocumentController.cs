using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskOne.Models;
using TaskOne.Services;

namespace TaskOne.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentController : ControllerBase
    {
        private readonly DocumentDbContext _dbContext;
        private readonly ITextExtractionService _extractionService;
        private readonly IDocumentParserService _parserService;
        private readonly IExportService _exportService;
        private readonly IOcrService _ocrService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DocumentController> _logger;
        private readonly string _uploadFolder;

        public DocumentController(
            DocumentDbContext dbContext,
            ITextExtractionService extractionService,
            IDocumentParserService parserService,
            IExportService exportService,
            IOcrService ocrService,
            IServiceScopeFactory scopeFactory,
            ILogger<DocumentController> logger)
        {
            _dbContext = dbContext;
            _extractionService = extractionService;
            _parserService = parserService;
            _exportService = exportService;
            _ocrService = ocrService;
            _scopeFactory = scopeFactory;
            _logger = logger;
            
            _uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(_uploadFolder))
            {
                Directory.CreateDirectory(_uploadFolder);
            }
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetDocuments()
        {
            try
            {
                var docs = await _dbContext.UploadedDocuments
                    .Include(d => d.Metadata)
                    .Include(d => d.LineItems)
                    .OrderByDescending(d => d.UploadDate)
                    .ToListAsync();

                _logger.LogInformation("Successfully retrieved {Count} documents from database", docs.Count);
                return Ok(docs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing documents. Exception: {ExceptionMessage}", ex.Message);
                return StatusCode(500, new { message = "Error loading documents from database.", error = ex.Message });
            }
        }

        [HttpGet("status/{id}")]
        public async Task<IActionResult> GetDocumentStatus(int id)
        {
            var doc = await _dbContext.UploadedDocuments.FindAsync(id);
            if (doc == null) return NotFound();
            return Ok(new { status = doc.Status, errorMessage = doc.ErrorMessage });
        }

        [HttpPost("upload")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> UploadDocument(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "No file selected or file is empty." });
            }

            try
            {
                string originalName = file.FileName;
                string cleanName = Path.GetFileName(originalName);
                string uniqueName = $"{Guid.NewGuid()}_{cleanName}";
                string savePath = Path.Combine(_uploadFolder, uniqueName);

                using (var stream = new FileStream(savePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Add to database in PENDING status
                var doc = new UploadedDocument
                {
                    FileName = cleanName,
                    FileType = "Detecting...",
                    FilePath = Path.Combine("uploads", uniqueName),
                    Status = DocumentStatus.Pending,
                    UploadDate = DateTime.UtcNow
                };

                _dbContext.UploadedDocuments.Add(doc);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Saved file {Name} to disk and database. ID: {Id}", cleanName, doc.Id);

                // Launch background processing worker
                _ = Task.Run(() => ProcessDocumentAsync(doc.Id, savePath));

                return Ok(doc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading document.");
                return StatusCode(500, new { message = "Error uploading document: " + ex.Message });
            }
        }

        private async Task ProcessDocumentAsync(int documentId, string filePath)
        {
            var stopwatch = Stopwatch.StartNew();

            // Create a scope to resolve DbContext inside thread
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<DocumentController>>();

            var doc = await db.UploadedDocuments.FindAsync(documentId);
            if (doc == null) return;

            try
            {
                // Update Status to Processing
                doc.Status = DocumentStatus.Processing;
                await db.SaveChangesAsync();

                // 1. Initialize OCR if needed (fully offline safety download check)
                await _ocrService.EnsureInitializedAsync();

                // 2. Perform file detection and text extraction
                string finalFileType;
                DocumentMetadata? metadata = null;
                List<LineItem> lineItems = new List<LineItem>();
                string rawText = "";

                string detectedType = _extractionService.DetectFileType(filePath);
                doc.FileType = detectedType;

                if (detectedType.StartsWith("Excel"))
                {
                    finalFileType = detectedType;
                    logger.LogInformation("Parsing Excel spreadsheet directly: {Path}", filePath);
                    var parsed = _parserService.ParseExcel(filePath);
                    metadata = parsed.Metadata;
                    lineItems = parsed.LineItems;
                    rawText = "[Excel Data parsed natively into structured columns]";
                }
                else
                {
                    logger.LogInformation("Extracting text from PDF/Image: {Path}", filePath);
                    rawText = _extractionService.ExtractText(filePath, out finalFileType);
                    doc.FileType = finalFileType;
                    logger.LogInformation("Extracted {TextLength} chars from document", rawText?.Length ?? 0);

                    logger.LogInformation("Parsing text patterns: {Path}", filePath);
                    var parsed = _parserService.ParseRawText(rawText);
                    metadata = parsed.Metadata;
                    lineItems = parsed.LineItems;

                    logger.LogInformation("Parse complete. PoNumber={Po}, PoDate={PoDate}, DeliveryDate={DelDate}, VendorCount={Vendor}, DeliverTo={DelTo}, LineItems={Items}",
                        metadata?.PoNumber ?? "NULL",
                        metadata?.PoDate?.ToString("yyyy-MM-dd") ?? "NULL",
                        metadata?.DeliveryDate?.ToString("yyyy-MM-dd") ?? "NULL",
                        metadata?.VendorDetails?.Length ?? 0,
                        metadata?.DeliverTo?.Length ?? 0,
                        lineItems?.Count ?? 0);
                }

                // 3. Save extracted models
                if (metadata != null)
                {
                    metadata.DocumentId = documentId;
                    db.DocumentMetadata.Add(metadata);
                    await db.SaveChangesAsync();  // ✅ Save metadata first to get the ID
                }

                // Now link line items to both document and metadata
                foreach (var item in lineItems)
                {
                    item.DocumentId = documentId;
                    if (metadata != null)
                    {
                        item.MetadataId = metadata.Id;  // ✅ Link to metadata
                    }
                    db.LineItems.Add(item);
                }

                stopwatch.Stop();
                doc.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
                doc.RawText = rawText;
                doc.Status = DocumentStatus.Completed;

                await db.SaveChangesAsync();  // ✅ Final save with all data
                logger.LogInformation("Successfully completed processing document ID {Id} in {Ms}ms", documentId, doc.ProcessingTimeMs);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.LogError(ex, "Failed to process document ID {Id}", documentId);

                doc.Status = DocumentStatus.Failed;
                doc.ErrorMessage = ex.Message;
                doc.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;

                await db.SaveChangesAsync();
            }
        }

        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            var doc = await _dbContext.UploadedDocuments.FindAsync(id);
            if (doc == null) return NotFound();

            try
            {
                // Delete physical file
                string fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", doc.FilePath);
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }

                // Cascade delete in Database
                _dbContext.UploadedDocuments.Remove(doc);
                await _dbContext.SaveChangesAsync();

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting document ID {Id}", id);
                return StatusCode(500, new { message = "Error deleting document." });
            }
        }

        public class SaveDocumentModel
        {
            public int DocumentId { get; set; }
            public string? PoNumber { get; set; }
            public string? VendorDetails { get; set; }
            public string? PoDate { get; set; }
            public string? DeliveryDate { get; set; }
            public string? DeliverTo { get; set; }
            public List<LineItemEditModel> LineItems { get; set; } = new List<LineItemEditModel>();
        }

        public class LineItemEditModel
        {
            public string? Item { get; set; }
            public decimal? Quantity { get; set; }
            public decimal? Rate { get; set; }
            public decimal? TaxPercent { get; set; }
            public decimal? TaxAmount { get; set; }
            public decimal? Amount { get; set; }
        }

        [HttpPost("save")]
        public async Task<IActionResult> SaveDocument([FromBody] SaveDocumentModel model)
        {
            if (model == null) return BadRequest();

            try
            {
                var doc = await _dbContext.UploadedDocuments
                    .Include(d => d.Metadata)
                    .Include(d => d.LineItems)
                    .FirstOrDefaultAsync(d => d.Id == model.DocumentId);

                if (doc == null) return NotFound();

                // Update Metadata
                if (doc.Metadata == null)
                {
                    doc.Metadata = new DocumentMetadata { DocumentId = model.DocumentId };
                    _dbContext.DocumentMetadata.Add(doc.Metadata);
                }

                doc.Metadata.PoNumber = model.PoNumber;
                doc.Metadata.VendorDetails = model.VendorDetails;
                
                if (DateTime.TryParse(model.PoDate, out var pod)) doc.Metadata.PoDate = pod;
                else doc.Metadata.PoDate = null;

                if (DateTime.TryParse(model.DeliveryDate, out var dd)) doc.Metadata.DeliveryDate = dd;
                else doc.Metadata.DeliveryDate = null;

                doc.Metadata.DeliverTo = model.DeliverTo;

                // Update Line Items: Delete old and add new edits
                _dbContext.LineItems.RemoveRange(doc.LineItems);
                doc.LineItems.Clear();

                foreach (var item in model.LineItems)
                {
                    var li = new LineItem
                    {
                        DocumentId = model.DocumentId,
                        PoNumber = model.PoNumber,
                        Item = item.Item,
                        Quantity = item.Quantity,
                        Rate = item.Rate,
                        TaxPercent = item.TaxPercent,
                        TaxAmount = item.TaxAmount,
                        Amount = item.Amount
                    };
                    
                    // Re-calculate math if empty
                    if (li.Amount == null && li.Quantity != null && li.Rate != null)
                    {
                        li.Amount = li.Quantity * li.Rate;
                    }
                    if (li.TaxAmount == null && li.TaxPercent != null && li.Amount != null)
                    {
                        li.TaxAmount = li.Amount * (li.TaxPercent / 100);
                    }

                    doc.LineItems.Add(li);
                }

                // If document had failed previously, clear error and complete it
                if (doc.Status == DocumentStatus.Failed)
                {
                    doc.Status = DocumentStatus.Completed;
                    doc.ErrorMessage = null;
                }

                await _dbContext.SaveChangesAsync();
                return Ok(doc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving edited document details.");
                return StatusCode(500, new { message = "Error saving changes: " + ex.Message });
            }
        }

        [HttpPost("export")]
        public async Task<IActionResult> ExportData([FromForm] string format, [FromForm] string ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
            {
                return BadRequest("No documents selected.");
            }

            try
            {
                var idList = ids.Split(',').Select(int.Parse).ToList();
                var docs = await _dbContext.UploadedDocuments
                    .Include(d => d.Metadata)
                    .Include(d => d.LineItems)
                    .Where(d => idList.Contains(d.Id))
                    .ToListAsync();

                if (docs.Count == 0) return BadRequest("Selected documents not found.");

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                if (format.ToLowerInvariant() == "excel")
                {
                    var fileBytes = _exportService.ExportToExcel(docs);
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"extraction_export_{timestamp}.xlsx");
                }
                else if (format.ToLowerInvariant() == "csv")
                {
                    var zipBytes = _exportService.ExportToCsvZip(docs);
                    return File(zipBytes, "application/zip", $"extraction_export_{timestamp}.zip");
                }
                else if (format.ToLowerInvariant() == "json")
                {
                    var jsonStr = _exportService.ExportToJson(docs);
                    var fileBytes = System.Text.Encoding.UTF8.GetBytes(jsonStr);
                    return File(fileBytes, "application/json", $"extraction_export_{timestamp}.json");
                }
                else
                {
                    return BadRequest("Unsupported export format.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting selected records.");
                return StatusCode(500, new { message = "Error exporting data." });
            }
        }
    }
}
