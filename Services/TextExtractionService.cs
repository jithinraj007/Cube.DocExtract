using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace TaskOne.Services
{
    public interface ITextExtractionService
    {
        string DetectFileType(string filePath);
        string ExtractText(string filePath, out string detectedType);
    }

    public class TextExtractionService : ITextExtractionService
    {
        private readonly IOcrService _ocrService;
        private readonly ILogger<TextExtractionService> _logger;

        public TextExtractionService(IOcrService ocrService, ILogger<TextExtractionService> logger)
        {
            _ocrService = ocrService;
            _logger = logger;
        }

        public string DetectFileType(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return "Unknown";
            }

            try
            {
                // Read magic bytes
                byte[] buffer = new byte[8];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int bytesRead = fs.Read(buffer, 0, buffer.Length);
                    if (bytesRead < 4) return "Unknown";
                }

                // Check PDF magic bytes: %PDF
                if (buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46)
                {
                    return "PDF";
                }

                // Check PNG magic bytes: 89 50 4E 47
                if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
                {
                    return "Image (PNG)";
                }

                // Check JPEG magic bytes: FF D8 FF
                if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
                {
                    return "Image (JPEG)";
                }

                // Check ZIP (XLSX, standard ZIP header): 50 4B 03 04
                if (buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04)
                {
                    // Check if it's likely an XLSX/Office file by extension
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext == ".xlsx") return "Excel (XLSX)";
                    if (ext == ".xls") return "Excel (XLS)";
                    return "ZIP/Archive";
                }

                // Check old XLS (Microsoft Compound Document File): D0 CF 11 E0 A1 B1 1A E1
                if (buffer[0] == 0xD0 && buffer[1] == 0xCF && buffer[2] == 0x11 && buffer[3] == 0xE0)
                {
                    return "Excel (XLS)";
                }

                // Fallback to extension check
                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                return extension switch
                {
                    ".pdf" => "PDF",
                    ".png" => "Image (PNG)",
                    ".jpg" or ".jpeg" => "Image (JPEG)",
                    ".xlsx" => "Excel (XLSX)",
                    ".xls" => "Excel (XLS)",
                    _ => "Unknown"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting file type for: {Path}", filePath);
                return "Unknown";
            }
        }

        public string ExtractText(string filePath, out string detectedType)
        {
            detectedType = DetectFileType(filePath);
            _logger.LogInformation("Processing file of detected type: {Type}", detectedType);

            if (detectedType == "PDF")
            {
                return ExtractTextFromPdf(filePath, out detectedType);
            }
            else if (detectedType.StartsWith("Image"))
            {
                return _ocrService.ExtractTextFromImageFile(filePath);
            }
            else if (detectedType.StartsWith("Excel"))
            {
                // Excel extraction is handled directly by the spreadsheet parser,
                // but we can return a structural placeholder here if called as general text
                return "[Excel Document - Parsed via Spreadsheet Extraction Engine]";
            }
            else
            {
                throw new NotSupportedException($"Unsupported file format: {detectedType}");
            }
        }

        private string ExtractTextFromPdf(string filePath, out string finalType)
        {
            finalType = "PDF";
            var textBuilder = new StringBuilder();
            bool hasDigitalText = false;

            try
            {
                using (var pdf = PdfDocument.Open(filePath))
                {
                    foreach (var page in pdf.GetPages())
                    {
                        string pageText = page.Text;
                        if (!string.IsNullOrWhiteSpace(pageText))
                        {
                            hasDigitalText = true;
                            textBuilder.AppendLine($"--- PAGE {page.Number} ---");
                            textBuilder.AppendLine(pageText);
                        }
                    }

                    // If we found significant digital text, return it immediately
                    if (hasDigitalText && textBuilder.Length > 100)
                    {
                        _logger.LogInformation("Natively extracted digital text from PDF.");
                        return textBuilder.ToString();
                    }

                    // If there is no digital text, we treat this as a Scanned PDF and OCR it
                    _logger.LogInformation("No digital text found in PDF. Extracting embedded page images for OCR...");
                    finalType = "Scanned PDF";
                    textBuilder.Clear();

                    int pageIndex = 1;
                    foreach (var page in pdf.GetPages())
                    {
                        textBuilder.AppendLine($"--- PAGE {pageIndex} (OCR) ---");
                        var images = page.GetImages().ToList();
                        
                        if (images.Count > 0)
                        {
                            foreach (var image in images)
                            {
                                byte[]? imageBytes = null;
                                if (image.TryGetPng(out var pngBytes))
                                {
                                    imageBytes = pngBytes;
                                }
                                else
                                {
                                    imageBytes = image.RawBytes.ToArray();
                                }

                                if (imageBytes != null)
                                {
                                    string ocrText = _ocrService.ExtractTextFromImage(imageBytes);
                                    if (!string.IsNullOrWhiteSpace(ocrText))
                                    {
                                        textBuilder.AppendLine(ocrText);
                                    }
                                }
                            }
                        }
                        else
                        {
                            _logger.LogWarning("No embedded images found on scanned PDF page {Page}", pageIndex);
                        }
                        pageIndex++;
                    }
                }

                return textBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF file: {Path}", filePath);
                throw;
            }
        }
    }
}
