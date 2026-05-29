using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using ClosedXML.Excel;
using TaskOne.Models;

namespace TaskOne.Services
{
    public interface IExportService
    {
        byte[] ExportToExcel(List<UploadedDocument> documents);
        byte[] ExportToCsvZip(List<UploadedDocument> documents);
        string ExportToJson(List<UploadedDocument> documents);
    }

    public class ExportService : IExportService
    {
        public byte[] ExportToExcel(List<UploadedDocument> documents)
        {
            using var workbook = new XLWorkbook();

            // Sheet 1: Line Items
            var wsLines = workbook.Worksheets.Add("Line Items");
            wsLines.Cell(1, 1).Value = "PO Number";
            wsLines.Cell(1, 2).Value = "Item";
            wsLines.Cell(1, 3).Value = "Quantity";
            wsLines.Cell(1, 4).Value = "Rate";
            wsLines.Cell(1, 5).Value = "Tax %";
            wsLines.Cell(1, 6).Value = "Tax";
            wsLines.Cell(1, 7).Value = "Amount";
            wsLines.Cell(1, 8).Value = "Source File";

            // Format Header Row for Sheet 1
            var headerRow1 = wsLines.Row(1);
            headerRow1.Style.Font.Bold = true;
            headerRow1.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            headerRow1.Style.Font.FontColor = XLColor.White;

            int rowIdx1 = 2;
            foreach (var doc in documents)
            {
                foreach (var item in doc.LineItems)
                {
                    wsLines.Cell(rowIdx1, 1).Value = item.PoNumber ?? doc.Metadata?.PoNumber ?? "";
                    wsLines.Cell(rowIdx1, 2).Value = item.Item ?? "";
                    wsLines.Cell(rowIdx1, 3).Value = item.Quantity ?? 0;
                    wsLines.Cell(rowIdx1, 4).Value = item.Rate ?? 0;
                    wsLines.Cell(rowIdx1, 5).Value = item.TaxPercent ?? 0;
                    wsLines.Cell(rowIdx1, 6).Value = item.TaxAmount ?? 0;
                    wsLines.Cell(rowIdx1, 7).Value = item.Amount ?? 0;
                    wsLines.Cell(rowIdx1, 8).Value = doc.FileName;
                    rowIdx1++;
                }
            }
            wsLines.Columns().AdjustToContents();

            // Sheet 2: Document Metadata
            var wsMeta = workbook.Worksheets.Add("Document Metadata");
            wsMeta.Cell(1, 1).Value = "PO Number";
            wsMeta.Cell(1, 2).Value = "Vendor Details";
            wsMeta.Cell(1, 3).Value = "PO Date";
            wsMeta.Cell(1, 4).Value = "Delivery Date";
            wsMeta.Cell(1, 5).Value = "Deliver To";
            wsMeta.Cell(1, 6).Value = "Source File";
            wsMeta.Cell(1, 7).Value = "Upload Date";
            wsMeta.Cell(1, 8).Value = "Processing Time (ms)";

            // Format Header Row for Sheet 2
            var headerRow2 = wsMeta.Row(1);
            headerRow2.Style.Font.Bold = true;
            headerRow2.Style.Fill.BackgroundColor = XLColor.FromHtml("#16213e");
            headerRow2.Style.Font.FontColor = XLColor.White;

            int rowIdx2 = 2;
            foreach (var doc in documents)
            {
                wsMeta.Cell(rowIdx2, 1).Value = doc.Metadata?.PoNumber ?? "";
                wsMeta.Cell(rowIdx2, 2).Value = doc.Metadata?.VendorDetails ?? "";
                
                if (doc.Metadata?.PoDate != null)
                    wsMeta.Cell(rowIdx2, 3).Value = doc.Metadata.PoDate.Value.ToString("yyyy-MM-dd");
                else
                    wsMeta.Cell(rowIdx2, 3).Value = "";

                if (doc.Metadata?.DeliveryDate != null)
                    wsMeta.Cell(rowIdx2, 4).Value = doc.Metadata.DeliveryDate.Value.ToString("yyyy-MM-dd");
                else
                    wsMeta.Cell(rowIdx2, 4).Value = "";

                wsMeta.Cell(rowIdx2, 5).Value = doc.Metadata?.DeliverTo ?? "";
                wsMeta.Cell(rowIdx2, 6).Value = doc.FileName;
                wsMeta.Cell(rowIdx2, 7).Value = doc.UploadDate.ToString("yyyy-MM-dd HH:mm:ss");
                wsMeta.Cell(rowIdx2, 8).Value = doc.ProcessingTimeMs ?? 0;
                rowIdx2++;
            }
            wsMeta.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            return ms.ToArray();
        }

        public byte[] ExportToCsvZip(List<UploadedDocument> documents)
        {
            var metadataBuilder = new StringBuilder();
            metadataBuilder.AppendLine("PO Number,Vendor Details,PO Date,Delivery Date,Deliver To,Source File,Upload Date,Processing Time (ms)");

            foreach (var doc in documents)
            {
                var meta = doc.Metadata;
                metadataBuilder.AppendLine(string.Join(",", new[]
                {
                    EscapeCsv(meta?.PoNumber),
                    EscapeCsv(meta?.VendorDetails),
                    meta?.PoDate?.ToString("yyyy-MM-dd") ?? "",
                    meta?.DeliveryDate?.ToString("yyyy-MM-dd") ?? "",
                    EscapeCsv(meta?.DeliverTo),
                    EscapeCsv(doc.FileName),
                    doc.UploadDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    (doc.ProcessingTimeMs ?? 0).ToString()
                }));
            }

            var linesBuilder = new StringBuilder();
            linesBuilder.AppendLine("PO Number,Item,Quantity,Rate,Tax %,Tax,Amount,Source File");

            foreach (var doc in documents)
            {
                foreach (var item in doc.LineItems)
                {
                    linesBuilder.AppendLine(string.Join(",", new[]
                    {
                        EscapeCsv(item.PoNumber ?? doc.Metadata?.PoNumber),
                        EscapeCsv(item.Item),
                        (item.Quantity ?? 0).ToString("F4"),
                        (item.Rate ?? 0).ToString("F4"),
                        (item.TaxPercent ?? 0).ToString("F2"),
                        (item.TaxAmount ?? 0).ToString("F4"),
                        (item.Amount ?? 0).ToString("F4"),
                        EscapeCsv(doc.FileName)
                    }));
                }
            }

            using var zipMs = new MemoryStream();
            using (var archive = new ZipArchive(zipMs, ZipArchiveMode.Create, true))
            {
                // Write metadata.csv
                var metadataEntry = archive.CreateEntry("metadata.csv");
                using (var writer = new StreamWriter(metadataEntry.Open(), Encoding.UTF8))
                {
                    writer.Write(metadataBuilder.ToString());
                }

                // Write line_items.csv
                var linesEntry = archive.CreateEntry("line_items.csv");
                using (var writer = new StreamWriter(linesEntry.Open(), Encoding.UTF8))
                {
                    writer.Write(linesBuilder.ToString());
                }
            }

            return zipMs.ToArray();
        }

        public string ExportToJson(List<UploadedDocument> documents)
        {
            var exportList = documents.Select(doc => new
            {
                doc.Id,
                doc.FileName,
                doc.FileType,
                doc.UploadDate,
                doc.Status,
                doc.ProcessingTimeMs,
                Metadata = doc.Metadata != null ? new
                {
                    doc.Metadata.PoNumber,
                    doc.Metadata.VendorDetails,
                    PoDate = doc.Metadata.PoDate?.ToString("yyyy-MM-dd"),
                    DeliveryDate = doc.Metadata.DeliveryDate?.ToString("yyyy-MM-dd"),
                    doc.Metadata.DeliverTo
                } : null,
                LineItems = doc.LineItems.Select(item => new
                {
                    item.PoNumber,
                    item.Item,
                    item.Quantity,
                    item.Rate,
                    TaxPercent = item.TaxPercent,
                    Tax = item.TaxAmount,
                    item.Amount
                }).ToList()
            }).ToList();

            return System.Text.Json.JsonSerializer.Serialize(exportList, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private string EscapeCsv(string? value)
        {
            if (value == null) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }
    }
}
