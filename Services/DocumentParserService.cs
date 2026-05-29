using System;
using System.IO;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using ExcelDataReader;
using TaskOne.Models;

namespace TaskOne.Services
{
    public interface IDocumentParserService
    {
        (DocumentMetadata Metadata, List<LineItem> LineItems) ParseRawText(string rawText);
        (DocumentMetadata Metadata, List<LineItem> LineItems) ParseExcel(string filePath);
    }

    public class DocumentParserService : IDocumentParserService
    {
        private readonly ILogger<DocumentParserService> _logger;

        public DocumentParserService(ILogger<DocumentParserService> logger)
        {
            _logger = logger;
        }

        #region Raw Text Parser (PDF & Images)

        public (DocumentMetadata Metadata, List<LineItem> LineItems) ParseRawText(string rawText)
        {
            var metadata = new DocumentMetadata();
            var lineItems = new List<LineItem>();

            if (string.IsNullOrWhiteSpace(rawText))
            {
                _logger.LogWarning("Raw text is empty or null");
                return (metadata, lineItems);
            }

            _logger.LogInformation("Starting ParseRawText. Text length: {Length} chars, {LineCount} lines", 
                rawText.Length, rawText.Split('\n').Length);

            string[] lines = rawText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                                    .Select(l => l.Trim())
                                    .ToArray();

            // Log first 10 lines for debugging
            _logger.LogInformation("First 10 lines of extracted text:");
            for (int i = 0; i < Math.Min(10, lines.Length); i++)
            {
                _logger.LogInformation("  Line {Index}: {Content}", i, lines[i]);
            }

            // 1. Extract PO Number
            metadata.PoNumber = ExtractPoNumber(lines);
            _logger.LogInformation("Extracted PoNumber: {PoNumber}", metadata.PoNumber ?? "NULL");

            // 2. Extract Dates (PO Date & Delivery Date)
            metadata.PoDate = ExtractPoDate(lines);
            metadata.DeliveryDate = ExtractDeliveryDate(lines);
            _logger.LogInformation("Extracted PoDate: {PoDate}, DeliveryDate: {DeliveryDate}", 
                metadata.PoDate?.ToString("yyyy-MM-dd") ?? "NULL",
                metadata.DeliveryDate?.ToString("yyyy-MM-dd") ?? "NULL");

            // 3. Extract Vendor Details
            metadata.VendorDetails = ExtractVendorDetails(lines);
            _logger.LogInformation("Extracted VendorDetails: {VendorDetails}", metadata.VendorDetails ?? "NULL");

            // 4. Extract Deliver To
            metadata.DeliverTo = ExtractDeliverTo(lines);
            _logger.LogInformation("Extracted DeliverTo: {DeliverTo}", metadata.DeliverTo ?? "NULL");

            // 5. Extract Line Items
            lineItems = ExtractLineItemsFromText(lines, metadata.PoNumber);
            _logger.LogInformation("Extracted {LineItemCount} line items", lineItems.Count);

            return (metadata, lineItems);
        }

        private string? ExtractPoNumber(string[] lines)
        {
            // Common PO Number Regex patterns
            var patterns = new[]
            {
                new Regex(@"(?i)(?:p\.?o\.?\s*number|purchase\s*order(?:\s*no\.?)?|po\s*#|order\s*no\.?)\s*[:#-]?\s*([a-zA-Z0-9-]+)", RegexOptions.Compiled),
                new Regex(@"(?i)\bpo\s*([a-zA-Z0-9-]+)\b", RegexOptions.Compiled),
                new Regex(@"\b(PO-\d+|PO\d{4,})\b", RegexOptions.Compiled)
            };

            foreach (var line in lines)
            {
                foreach (var pattern in patterns)
                {
                    var match = pattern.Match(line);
                    if (match.Success)
                    {
                        string val = match.Groups[1].Value.Trim(':', '-', ' ', '#');
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            _logger.LogInformation("Extracted PO Number: {Po}", val);
                            return val;
                        }
                    }
                }
            }

            return null;
        }

        private DateTime? ExtractPoDate(string[] lines)
        {
            _logger.LogInformation("Starting ExtractPoDate");
            // First try keyword-based extraction with specific keywords
            var poDateKeywords = new[] { "po date", "order date", "issue date", "date :", "dated :" };
            var result = ExtractDateWithProximity(lines, poDateKeywords);
            if (result.HasValue)
            {
                _logger.LogInformation("Extracted PO Date from keywords: {Date}", result.Value);
                return result;
            }

            _logger.LogWarning("No PO date found via keywords, trying header area extraction");

            // Second try: Extract FIRST date found in first 10 lines (typically PO header area)
            result = ExtractFirstDateInRange(lines, 0, Math.Min(10, lines.Length));
            if (result.HasValue)
            {
                _logger.LogInformation("Extracted PO Date from header area: {Date}", result.Value);
                return result;
            }

            // Third try: Extract FIRST date found anywhere in first 25 lines
            result = ExtractFirstDateInRange(lines, 0, Math.Min(25, lines.Length));
            if (result.HasValue)
            {
                _logger.LogInformation("Extracted PO Date from first 25 lines: {Date}", result.Value);
                return result;
            }

            _logger.LogWarning("Could not extract any PO date");
            return null;
        }

        private DateTime? ExtractDeliveryDate(string[] lines)
        {
            _logger.LogInformation("Starting ExtractDeliveryDate");
            var deliveryKeywords = new[] { "delivery date", "due date", "deliver by", "ship date", "delivery:", "delivery date :" };
            var result = ExtractDateWithProximity(lines, deliveryKeywords);
            if (result.HasValue)
            {
                _logger.LogInformation("Extracted Delivery Date from keywords: {Date}", result.Value);
                return result;
            }

            _logger.LogWarning("No delivery date found via keywords, trying header area extraction");

            // Fallback 1: Try to find SECOND date in header area (lines 0-15)
            result = ExtractNthDateInRange(lines, 0, Math.Min(15, lines.Length), 2);
            if (result.HasValue)
            {
                _logger.LogInformation("Extracted Delivery Date as 2nd date from header area: {Date}", result.Value);
                return result;
            }

            // Fallback 2: Try to find ANY date after the PO date keyword in first 20 lines
            for (int i = 0; i < Math.Min(20, lines.Length); i++)
            {
                if (lines[i].ToLowerInvariant().Contains("date"))
                {
                    // Look for dates on next 5 lines
                    for (int j = i + 1; j < Math.Min(i + 6, lines.Length); j++)
                    {
                        result = ExtractDateFromLine(lines[j]);
                        if (result.HasValue && IsValidBusinessDate(result.Value))
                        {
                            _logger.LogInformation("Extracted Delivery Date from 'date' keyword area line +{Offset}: {Date}", j - i, result.Value);
                            return result;
                        }
                    }
                }
            }

            _logger.LogWarning("Could not extract any delivery date");
            return null;
        }

        /// <summary>
        /// Extract a date from a single line
        /// </summary>
        private DateTime? ExtractDateFromLine(string line)
        {
            var dateRegex = new Regex(
                @"\b(" +
                @"\d{1,2}[/\.-]\d{1,2}[/\.-]\d{2,4}|" +
                @"\d{4}[/\.-]\d{1,2}[/\.-]\d{1,2}|" +
                @"\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{2,4}|" +
                @"(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{1,2},?\s+\d{4}" +
                @")\b", 
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var match = dateRegex.Match(line);
            if (match.Success)
            {
                return TryParseDate(match.Value);
            }
            return null;
        }

        /// <summary>
        /// Extract the first (Nth) date found in a range of lines
        /// </summary>
        private DateTime? ExtractNthDateInRange(string[] lines, int startIdx, int endIdx, int nthDate)
        {
            var dateRegex = new Regex(
                @"\b(" +
                @"\d{1,2}[/\.-]\d{1,2}[/\.-]\d{2,4}|" +
                @"\d{4}[/\.-]\d{1,2}[/\.-]\d{1,2}|" +
                @"\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{2,4}|" +
                @"(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{1,2},?\s+\d{4}" +
                @")\b", 
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            int dateCount = 0;
            for (int i = startIdx; i < endIdx && i < lines.Length; i++)
            {
                var matches = dateRegex.Matches(lines[i]);
                foreach (Match match in matches)
                {
                    dateCount++;
                    if (dateCount == nthDate)
                    {
                        var parsed = TryParseDate(match.Value);
                        if (parsed.HasValue && IsValidBusinessDate(parsed.Value))
                        {
                            return parsed;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Extract the first date found in a range of lines
        /// </summary>
        private DateTime? ExtractFirstDateInRange(string[] lines, int startIdx, int endIdx)
        {
            return ExtractNthDateInRange(lines, startIdx, endIdx, 1);
        }
        private DateTime? ExtractDateWithProximity(string[] lines, string[] keywords)
        {
            // Enhanced date formats to support various international formats
            var dateRegex = new Regex(
                @"\b(" +
                @"\d{1,2}[/\.-]\d{1,2}[/\.-]\d{2,4}|" +                                    // 01/03/2025, 01-03-2025, 01.03.2025
                @"\d{4}[/\.-]\d{1,2}[/\.-]\d{1,2}|" +                                      // 2025/03/01
                @"\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{2,4}|" +  // 01 Mar 2025
                @"(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{1,2},?\s+\d{4}" +  // Mar 01, 2025 or Mar 01 2025
                @")\b", 
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            DateTime? extractedDate = null;

            // First pass: Look for keyword-based date extraction
            for (int i = 0; i < lines.Length; i++)
            {
                string lineLower = lines[i].ToLowerInvariant();
                if (keywords.Any(kw => lineLower.Contains(kw)))
                {
                    // Check same line first
                    var match = dateRegex.Match(lines[i]);
                    if (match.Success)
                    {
                        extractedDate = TryParseDate(match.Value);
                        if (extractedDate.HasValue && IsValidBusinessDate(extractedDate.Value))
                        {
                            _logger.LogInformation("Extracted date from keyword line: {Date}", extractedDate.Value);
                            return extractedDate;
                        }
                    }

                    // Check next 3 lines for date
                    for (int offset = 1; offset <= 3 && (i + offset) < lines.Length; offset++)
                    {
                        var nextLineMatch = dateRegex.Match(lines[i + offset]);
                        if (nextLineMatch.Success)
                        {
                            extractedDate = TryParseDate(nextLineMatch.Value);
                            if (extractedDate.HasValue && IsValidBusinessDate(extractedDate.Value))
                            {
                                _logger.LogInformation("Extracted date from line +{Offset}: {Date}", offset, extractedDate.Value);
                                return extractedDate;
                            }
                        }
                    }
                }
            }

            // Fallback: search any date in first 20 lines
            for (int i = 0; i < Math.Min(20, lines.Length); i++)
            {
                var match = dateRegex.Match(lines[i]);
                if (match.Success)
                {
                    extractedDate = TryParseDate(match.Value);
                    if (extractedDate.HasValue && IsValidBusinessDate(extractedDate.Value))
                    {
                        _logger.LogInformation("Extracted fallback date from line {Index}: {Date}", i, extractedDate.Value);
                        return extractedDate;
                    }
                }
            }

            _logger.LogWarning("No valid date found for keywords: {Keywords}", string.Join(", ", keywords));
            return null;
        }

        /// <summary>
        /// Try to parse a date string with multiple format attempts
        /// </summary>
        private DateTime? TryParseDate(string dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString))
                return null;

            // Try multiple date formats and culture settings
            string[] formats = new[]
            {
                "dd/MM/yyyy",   // 01/03/2025
                "dd-MM-yyyy",   // 01-03-2025
                "dd.MM.yyyy",   // 01.03.2025
                "yyyy/MM/dd",   // 2025/03/01
                "yyyy-MM-dd",   // 2025-03-01
                "yyyy.MM.dd",   // 2025.03.01
                "dd MMM yyyy",  // 01 Mar 2025
                "dd MMMM yyyy", // 01 March 2025
                "MMM dd, yyyy", // Mar 01, 2025
                "MMM dd yyyy",  // Mar 01 2025
                "MMMM dd, yyyy", // March 01, 2025
                "MMMM dd yyyy",  // March 01 2025
                "dd/MM/yy",     // 01/03/25
                "MM/dd/yyyy",   // 03/01/2025 (US format)
                "MM/dd/yy",     // 03/01/25 (US format)
            };

            // Try parsing with invariant culture first
            if (DateTime.TryParseExact(dateString.Trim(), formats, 
                System.Globalization.CultureInfo.InvariantCulture, 
                System.Globalization.DateTimeStyles.None, out DateTime result))
            {
                return result;
            }

            // Try with current culture as fallback
            if (DateTime.TryParse(dateString.Trim(), out DateTime fallbackResult))
            {
                return fallbackResult;
            }

            _logger.LogWarning("Could not parse date string: {DateString}", dateString);
            return null;
        }

        /// <summary>
        /// Validate that extracted date is reasonable for business document (year 2000-2050)
        /// </summary>
        private bool IsValidBusinessDate(DateTime date)
        {
            return date.Year >= 2000 && date.Year <= 2050;
        }


        private string? ExtractVendorDetails(string[] lines)
        {
            var vendorKeywords = new[] { "vendor", "vendor address", "vendor details", "supplier", "from:", "sold by", "invoice from", "vendor name" };
            var result = ExtractAddressBlock(lines, vendorKeywords, defaultSearchStart: 0, defaultSearchEnd: 15);
            if (!string.IsNullOrWhiteSpace(result))
            {
                _logger.LogInformation("Extracted Vendor Details: {Vendor}", result);
                return result;
            }
            return null;
        }

        private string? ExtractDeliverTo(string[] lines)
        {
            var deliverKeywords = new[] { "deliver to", "ship to", "delivery address", "delivery to", "recipient", "bill to", "invoice to", "deliver-to" };
            var result = ExtractAddressBlock(lines, deliverKeywords, defaultSearchStart: 5, defaultSearchEnd: 25);
            if (!string.IsNullOrWhiteSpace(result))
            {
                _logger.LogInformation("Extracted Deliver To: {DeliverTo}", result);
                return result;
            }
            return null;
        }

        private string? ExtractAddressBlock(string[] lines, string[] keywords, int defaultSearchStart, int defaultSearchEnd)
        {
            // First pass: Look for exact keyword match
            for (int i = 0; i < lines.Length; i++)
            {
                string lineLower = lines[i].ToLowerInvariant();

                // Check for keyword match
                if (keywords.Any(kw => lineLower.Contains(kw.ToLowerInvariant())))
                {
                    var blockLines = new List<string>();

                    // If keyword is on its own line, take the next lines
                    if (lineLower.Trim().Length <= 30)  // It's just a label
                    {
                        for (int offset = 1; offset <= 5 && (i + offset) < lines.Length; offset++)
                        {
                            string target = lines[i + offset].Trim();

                            // Stop conditions
                            if (string.IsNullOrWhiteSpace(target)) break;
                            if (target.Length < 3) break;
                            if (keywords.Any(kw => target.ToLowerInvariant().Contains(kw.ToLowerInvariant()))) break;

                            blockLines.Add(target);
                            if (blockLines.Count >= 4) break;
                        }
                    }
                    else
                    {
                        // Keyword is inline with data, extract everything after the keyword
                        int keywordIdx = lineLower.IndexOf(keywords.First(kw => lineLower.Contains(kw.ToLowerInvariant())));
                        string remainder = lines[i].Substring(keywordIdx).Trim();

                        if (remainder.Length > 3)
                        {
                            blockLines.Add(remainder);
                        }

                        // Add following lines as well
                        for (int offset = 1; offset <= 3 && (i + offset) < lines.Length; offset++)
                        {
                            string target = lines[i + offset].Trim();
                            if (string.IsNullOrWhiteSpace(target) || target.Length < 3) break;
                            if (keywords.Any(kw => target.ToLowerInvariant().Contains(kw.ToLowerInvariant()))) break;
                            blockLines.Add(target);
                        }
                    }

                    if (blockLines.Count > 0)
                    {
                        var result = string.Join(", ", blockLines.Where(l => !string.IsNullOrWhiteSpace(l)));
                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            return result;
                        }
                    }
                }
            }

            // Fallback: First 3-4 non-empty lines in search area
            int start = Math.Min(defaultSearchStart, lines.Length);
            int end = Math.Min(defaultSearchEnd, lines.Length);
            var fallbackBlock = new List<string>();

            for (int i = start; i < end; i++)
            {
                string line = lines[i].Trim();
                if (!string.IsNullOrWhiteSpace(line) && line.Length > 3 && !keywords.Any(kw => line.ToLowerInvariant().Contains(kw.ToLowerInvariant())))
                {
                    fallbackBlock.Add(line);
                    if (fallbackBlock.Count >= 4) break;
                }
            }

            if (fallbackBlock.Count > 0)
            {
                return string.Join(", ", fallbackBlock);
            }

            return null;
        }

        private List<LineItem> ExtractLineItemsFromText(string[] lines, string? poNumber)
        {
            var lineItems = new List<LineItem>();
            int headerIdx = -1;

            // Search for table headers
            var headerKeywords = new[] { "item", "description", "qty", "quantity", "rate", "price", "amount", "total" };
            for (int i = 0; i < lines.Length; i++)
            {
                string lineLower = lines[i].ToLowerInvariant();
                int matchCount = headerKeywords.Count(kw => lineLower.Contains(kw));
                if (matchCount >= 2 && (lineLower.Contains("qty") || lineLower.Contains("quantity") || lineLower.Contains("amount")))
                {
                    headerIdx = i;
                    _logger.LogInformation("Detected table header at line {Idx}: {Content}", i, lines[i]);
                    break;
                }
            }

            if (headerIdx == -1)
            {
                // Fallback: look for a row with multiple numeric fields that sum up
                headerIdx = 10; // Default start
            }

            // Process lines below the header
            for (int i = headerIdx + 1; i < lines.Length; i++)
            {
                string line = lines[i];

                // Stop conditions
                string lineLower = line.ToLowerInvariant();
                if (lineLower.Contains("subtotal") || lineLower.Contains("grand total") || lineLower.Contains("total due") || lineLower.Contains("tax summary") || lineLower.Contains("payment terms"))
                {
                    _logger.LogInformation("Reached table boundary at line {Idx}: {Content}", i, line);
                    break;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                // Match decimal numbers in the line
                // Matches negative or positive numbers with optional decimals
                var numberRegex = new Regex(@"-?\b\d+(?:,\d{3})*(?:\.\d+)?\b", RegexOptions.Compiled);
                var matches = numberRegex.Matches(line);

                if (matches.Count >= 2)
                {
                    // Parse all decimal values
                    var values = new List<decimal>();
                    foreach (Match match in matches)
                    {
                        if (decimal.TryParse(match.Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
                        {
                            values.Add(val);
                        }
                    }

                    if (values.Count >= 2)
                    {
                        // Identify Description (everything before the first number)
                        string firstNumStr = matches[0].Value;
                        int numStartIdx = line.IndexOf(firstNumStr);
                        string description = numStartIdx > 0 ? line.Substring(0, numStartIdx).Trim(' ', '-', '.', '\t', ',') : "Line Item";

                        if (string.IsNullOrWhiteSpace(description) || description.Length < 2)
                        {
                            description = "Item Details";
                        }

                        var item = new LineItem
                        {
                            PoNumber = poNumber,
                            Item = description
                        };

                        // Mathematical heuristics to assign values:
                        // Typical formats:
                        // 1. Qty, Rate, Amount
                        // 2. Qty, Rate, Tax%, Tax, Amount
                        // 3. ItemNo, Qty, Rate, Amount
                        
                        bool parsed = false;

                        // Try to find if Qty * Rate = Amount (or close)
                        // Let's test combinations
                        for (int q = 0; q < values.Count; q++)
                        {
                            for (int r = 0; r < values.Count; r++)
                            {
                                if (q == r) continue;
                                for (int a = 0; a < values.Count; a++)
                                {
                                    if (a == q || a == r) continue;

                                    decimal qty = values[q];
                                    decimal rate = values[r];
                                    decimal amt = values[a];

                                    // Simple multiplication check
                                    if (qty > 0 && rate > 0 && Math.Abs((qty * rate) - amt) < 0.05m)
                                    {
                                        item.Quantity = qty;
                                        item.Rate = rate;
                                        item.Amount = amt;

                                        // See if we have Tax% and TaxAmount in the remaining numbers
                                        var remaining = values.Where((v, idx) => idx != q && idx != r && idx != a).ToList();
                                        if (remaining.Count >= 1)
                                        {
                                            // Proximity mapping
                                            item.TaxAmount = remaining[0];
                                            if (remaining.Count >= 2)
                                            {
                                                item.TaxPercent = remaining[1];
                                            }
                                        }

                                        parsed = true;
                                        break;
                                    }
                                }
                                if (parsed) break;
                            }
                            if (parsed) break;
                        }

                        // Fallback mapping if math check failed (assume standard order: Qty, Rate, Amount)
                        if (!parsed)
                        {
                            // Strip item numbers from first value if it's an integer and matches index
                            int valStart = 0;
                            if (values.Count >= 4 && values[0] == (lineItems.Count + 1))
                            {
                                valStart = 1; // Skip item index
                            }

                            if (values.Count - valStart >= 3)
                            {
                                item.Quantity = values[valStart];
                                item.Rate = values[valStart + 1];
                                item.Amount = values[valStart + 2];

                                if (values.Count - valStart >= 5)
                                {
                                    item.TaxPercent = values[valStart + 2];
                                    item.TaxAmount = values[valStart + 3];
                                    item.Amount = values[valStart + 4];
                                }
                                parsed = true;
                            }
                            else if (values.Count - valStart == 2)
                            {
                                item.Quantity = values[valStart];
                                item.Amount = values[valStart + 1];
                                item.Rate = item.Quantity > 0 ? item.Amount / item.Quantity : 0;
                                parsed = true;
                            }
                        }

                        if (parsed && item.Quantity > 0)
                        {
                            // Auto calculate missing taxes
                            if (item.TaxPercent != null && item.TaxAmount == null)
                            {
                                item.TaxAmount = item.Amount * (item.TaxPercent / 100);
                            }
                            else if (item.TaxAmount != null && item.TaxPercent == null && item.Amount > 0)
                            {
                                item.TaxPercent = Math.Round((item.TaxAmount.Value / item.Amount.Value) * 100, 2);
                            }

                            lineItems.Add(item);
                            _logger.LogInformation("Parsed Line Item: {Item}, Qty: {Qty}, Rate: {Rate}, Amt: {Amt}", item.Item, item.Quantity, item.Rate, item.Amount);
                        }
                    }
                }
            }

            return lineItems;
        }

        #endregion

        #region Excel Parser

        public (DocumentMetadata Metadata, List<LineItem> LineItems) ParseExcel(string filePath)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var metadata = new DocumentMetadata();
            var lineItems = new List<LineItem>();

            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = ExcelReaderFactory.CreateReader(stream);
                var result = reader.AsDataSet();

                if (result.Tables.Count == 0)
                {
                    return (metadata, lineItems);
                }

                _logger.LogInformation("Excel successfully loaded with {Count} sheets.", result.Tables.Count);

                // Strategy: Scan all sheets.
                // We'll perform metadata scanning (proximity) and table scanning (line items) across all sheets.
                // If there are multiple sheets (e.g. Sheet1 = Line Items, Sheet2 = Others/Metadata), 
                // Sheet 1 will yield the Line Items and Sheet 2 will yield the Metadata headers!
                foreach (DataTable table in result.Tables)
                {
                    _logger.LogInformation("Scanning Sheet: {Name}", table.TableName);
                    
                    // 1. Scan for Metadata in this sheet
                    ScanSheetForMetadata(table, metadata);

                    // 2. Scan for Line Items in this sheet
                    var sheetItems = ScanSheetForLineItems(table, metadata.PoNumber);
                    if (sheetItems.Count > 0)
                    {
                        lineItems.AddRange(sheetItems);
                    }
                }

                // If PO number was extracted from metadata, sync it into line items
                if (!string.IsNullOrWhiteSpace(metadata.PoNumber))
                {
                    foreach (var item in lineItems)
                    {
                        if (string.IsNullOrWhiteSpace(item.PoNumber))
                        {
                            item.PoNumber = metadata.PoNumber;
                        }
                    }
                }

                return (metadata, lineItems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Excel file at: {Path}", filePath);
                throw;
            }
        }

        private void ScanSheetForMetadata(DataTable table, DocumentMetadata metadata)
        {
            int rows = table.Rows.Count;
            int cols = table.Columns.Count;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    object cellVal = table.Rows[r][c];
                    if (cellVal == null || cellVal == DBNull.Value) continue;

                    string cellText = cellVal.ToString()!.Trim();
                    if (string.IsNullOrWhiteSpace(cellText)) continue;

                    string cellTextLower = cellText.ToLowerInvariant();

                    // PO Number
                    if (metadata.PoNumber == null && 
                        (cellTextLower == "po number" || cellTextLower == "po #" || cellTextLower == "purchase order" || cellTextLower == "order no" || cellTextLower.Contains("purchase order no")))
                    {
                        metadata.PoNumber = GetAdjacentCellValue(table, r, c);
                    }

                    // PO Date
                    if (metadata.PoDate == null && 
                        (cellTextLower == "po date" || cellTextLower == "order date" || cellTextLower == "issue date" || cellTextLower == "date"))
                    {
                        string? dateStr = GetAdjacentCellValue(table, r, c);
                        if (DateTime.TryParse(dateStr, out var d)) metadata.PoDate = d;
                    }

                    // Delivery Date
                    if (metadata.DeliveryDate == null && 
                        (cellTextLower == "delivery date" || cellTextLower == "due date" || cellTextLower == "ship date" || cellTextLower == "deliver by"))
                    {
                        string? dateStr = GetAdjacentCellValue(table, r, c);
                        if (DateTime.TryParse(dateStr, out var d)) metadata.DeliveryDate = d;
                    }

                    // Vendor Details
                    if (metadata.VendorDetails == null && 
                        (cellTextLower == "vendor" || cellTextLower == "supplier" || cellTextLower == "from" || cellTextLower == "sold by"))
                    {
                        metadata.VendorDetails = GetAdjacentCellValue(table, r, c, blockSearch: true);
                    }

                    // Deliver To
                    if (metadata.DeliverTo == null && 
                        (cellTextLower == "deliver to" || cellTextLower == "ship to" || cellTextLower == "delivery address" || cellTextLower == "recipient" || cellTextLower == "bill to"))
                    {
                        metadata.DeliverTo = GetAdjacentCellValue(table, r, c, blockSearch: true);
                    }
                }
            }
        }

        private string? GetAdjacentCellValue(DataTable table, int r, int c, bool blockSearch = false)
        {
            int rows = table.Rows.Count;
            int cols = table.Columns.Count;

            // 1. Try cell immediately to the right
            if (c + 1 < cols)
            {
                object rightVal = table.Rows[r][c + 1];
                if (rightVal != null && rightVal != DBNull.Value && !string.IsNullOrWhiteSpace(rightVal.ToString()))
                {
                    return rightVal.ToString()!.Trim();
                }
            }

            // 2. Try cell immediately below
            if (r + 1 < rows)
            {
                object downVal = table.Rows[r + 1][c];
                if (downVal != null && downVal != DBNull.Value && !string.IsNullOrWhiteSpace(downVal.ToString()))
                {
                    // Block search handles multiple lines of address blocks
                    if (blockSearch)
                    {
                        var block = new List<string> { downVal.ToString()!.Trim() };
                        for (int offset = 2; offset <= 4 && (r + offset) < rows; offset++)
                        {
                            object offsetVal = table.Rows[r + offset][c];
                            if (offsetVal == null || offsetVal == DBNull.Value || string.IsNullOrWhiteSpace(offsetVal.ToString()) || offsetVal.ToString()!.Contains(":"))
                            {
                                break;
                            }
                            block.Add(offsetVal.ToString()!.Trim());
                        }
                        return string.Join(", ", block);
                    }
                    return downVal.ToString()!.Trim();
                }
            }

            return null;
        }

        private List<LineItem> ScanSheetForLineItems(DataTable table, string? poNumber)
        {
            var items = new List<LineItem>();
            int rows = table.Rows.Count;
            int cols = table.Columns.Count;

            int headerRowIdx = -1;
            var colMappings = new Dictionary<string, int>();

            var itemHeaders = new[] { "item", "description", "details", "particulars", "name" };
            var qtyHeaders = new[] { "qty", "quantity", "volume", "count" };
            var rateHeaders = new[] { "rate", "price", "unit price", "cost" };
            var taxPercentHeaders = new[] { "tax%", "tax %", "tax percent", "vat%" };
            var taxAmountHeaders = new[] { "tax", "tax amount", "vat" };
            var amountHeaders = new[] { "amount", "total", "line total", "subtotal" };

            // Scan for row that has item headers
            for (int r = 0; r < rows; r++)
            {
                int matchedCols = 0;
                var tempMappings = new Dictionary<string, int>();

                for (int c = 0; c < cols; c++)
                {
                    object cellVal = table.Rows[r][c];
                    if (cellVal == null || cellVal == DBNull.Value) continue;

                    string headerLower = cellVal.ToString()!.Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(headerLower)) continue;

                    if (itemHeaders.Any(h => headerLower == h || headerLower.Contains("item description")))
                    {
                        tempMappings["item"] = c;
                        matchedCols++;
                    }
                    else if (qtyHeaders.Any(h => headerLower == h || headerLower.Contains("qty")))
                    {
                        tempMappings["qty"] = c;
                        matchedCols++;
                    }
                    else if (rateHeaders.Any(h => headerLower == h || headerLower.Contains("price") || headerLower.Contains("rate")))
                    {
                        tempMappings["rate"] = c;
                        matchedCols++;
                    }
                    else if (taxPercentHeaders.Any(h => headerLower == h || headerLower == "tax %"))
                    {
                        tempMappings["taxPercent"] = c;
                        matchedCols++;
                    }
                    else if (taxAmountHeaders.Any(h => headerLower == h || headerLower == "tax amount"))
                    {
                        tempMappings["taxAmount"] = c;
                        matchedCols++;
                    }
                    else if (amountHeaders.Any(h => headerLower == h || headerLower == "line total"))
                    {
                        tempMappings["amount"] = c;
                        matchedCols++;
                    }
                }

                // If we match at least 2 key columns (Qty and Amount, or Item and Qty/Rate), that's our header row!
                if (matchedCols >= 2 && (tempMappings.ContainsKey("qty") || tempMappings.ContainsKey("amount")))
                {
                    headerRowIdx = r;
                    colMappings = tempMappings;
                    break;
                }
            }

            if (headerRowIdx == -1)
            {
                return items; // No line items table found in this sheet
            }

            _logger.LogInformation("Detected line items table header in sheet {SheetName} at row {Idx}", table.TableName, headerRowIdx);

            // Read table items
            for (int r = headerRowIdx + 1; r < rows; r++)
            {
                // Check if row is empty or contains "Total"
                object firstCell = table.Rows[r][0];
                if (firstCell != null && firstCell != DBNull.Value)
                {
                    string txt = firstCell.ToString()!.ToLowerInvariant();
                    if (txt.Contains("total") || txt.Contains("subtotal") || txt.Contains("grand total"))
                    {
                        break; // Stop at totals
                    }
                }

                // Gather column values
                string itemVal = colMappings.ContainsKey("item") ? GetCellValue(table, r, colMappings["item"]) ?? "Line Item" : "Line Item";
                string qtyStr = colMappings.ContainsKey("qty") ? GetCellValue(table, r, colMappings["qty"]) : null;
                string rateStr = colMappings.ContainsKey("rate") ? GetCellValue(table, r, colMappings["rate"]) : null;
                string taxPercentStr = colMappings.ContainsKey("taxPercent") ? GetCellValue(table, r, colMappings["taxPercent"]) : null;
                string taxAmountStr = colMappings.ContainsKey("taxAmount") ? GetCellValue(table, r, colMappings["taxAmount"]) : null;
                string amountStr = colMappings.ContainsKey("amount") ? GetCellValue(table, r, colMappings["amount"]) : null;

                if (string.IsNullOrWhiteSpace(itemVal) && string.IsNullOrWhiteSpace(qtyStr) && string.IsNullOrWhiteSpace(amountStr))
                {
                    // Empty row, possibly end of table
                    continue;
                }

                // Parse numbers safely
                decimal.TryParse(qtyStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal qty);
                decimal.TryParse(rateStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rate);
                decimal.TryParse(taxPercentStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal taxPercent);
                decimal.TryParse(taxAmountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal taxAmt);
                decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount);

                if (qty == 0 && amount == 0) continue; // Skip invalid records

                // If rate is missing but amount & qty are there
                if (rate == 0 && qty > 0 && amount > 0)
                {
                    rate = amount / qty;
                }
                // If amount is missing
                if (amount == 0 && qty > 0 && rate > 0)
                {
                    amount = qty * rate;
                }

                var line = new LineItem
                {
                    PoNumber = poNumber,
                    Item = itemVal,
                    Quantity = qty > 0 ? qty : null,
                    Rate = rate > 0 ? rate : null,
                    TaxPercent = taxPercent > 0 ? taxPercent : null,
                    TaxAmount = taxAmt > 0 ? taxAmt : null,
                    Amount = amount > 0 ? amount : null
                };

                // Calculate missing tax fields
                if (line.TaxPercent != null && line.TaxAmount == null && line.Amount != null)
                {
                    line.TaxAmount = line.Amount.Value * (line.TaxPercent.Value / 100);
                }
                else if (line.TaxAmount != null && line.TaxPercent == null && line.Amount != null && line.Amount > 0)
                {
                    line.TaxPercent = Math.Round((line.TaxAmount.Value / line.Amount.Value) * 100, 2);
                }

                items.Add(line);
            }

            return items;
        }

        private string? GetCellValue(DataTable table, int r, int c)
        {
            object val = table.Rows[r][c];
            if (val == null || val == DBNull.Value) return null;
            return val.ToString()?.Trim();
        }

        #endregion
    }
}
