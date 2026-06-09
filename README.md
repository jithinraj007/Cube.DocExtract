Project Flow: Cube.DocExtract

•	Framework: ASP.NET Core Razor Pages (.NET 9)
•	Database: SQL Server with Entity Framework Core
•	PDF Processing: UglyToad.PdfPig
•	OCR: Tesseract (5.2.0)
•	Excel: ExcelDataReader
•	Frontend: HTML5, Bootstrap, jQuery, AJAX
•	Logging: Microsoft.Extensions.Logging



This is a Document Processing & Data Extraction application built with ASP.NET Core Razor Pages (.NET 9) and SQL Server. Here's how it works:
1.	Architecture Overview

 


2. Core Components
A. Frontend (UI Layer)
•	Views/Home/Index.cshtml - Main dashboard with:
•	Analytics Cards: Display stats (Total Uploaded, Processed Success, In Pipeline, Failed)
•	Upload Section: File upload interface
•	Document List: Table showing all uploaded documents with their status
•	Real-time Status Updates: Via AJAX polling
B. Controllers
HomeController.cs
•	Serves the main Index page (dashboard)
•	Handles Privacy and Error views
•	Basic routing for the UI
DocumentController.cs (API)
•	GET /api/document/list - Retrieves all documents with metadata
•	GET /api/document/status/{id} - Checks processing status
•	POST /api/document/upload - Handles file uploads and triggers processing
•	Background Processing - Queues document for extraction after upload
C. Database Layer (Models/)
Entity Models:
•	UploadedDocument - Main document record with:
•	File info (name, type, path)
•	Upload date & processing status
•	Raw extracted text
•	Processing time metrics
•	DocumentMetadata - Extracted business data (PO numbers, vendor details)
•	LineItem - Structured line items from documents (quantities, amounts)
DocumentDbContext.cs - EF Core context managing SQL Server connection
D. Services Layer (Business Logic)
TextExtractionService.cs
•	Detects file type (PDF, scanned images, Excel) using magic bytes
•	Extracts text from:
•	PDFs → Uses PdfPig library
•	Scanned Images → Routes to OCR service
•	Excel files → Uses ExcelDataReader
OcrService.cs
•	Uses Tesseract OCR for image text recognition
•	Auto-downloads trained data (eng.traineddata) on first run
•	Handles scanned document text extraction
DocumentParserService.cs (Core intelligence - 989 lines)
•	Parses raw text using regex patterns to extract:
•	PO numbers
•	Vendor information
•	Line items (products, quantities, prices)
•	Parses Excel files directly into structured data
•	Uses CultureInfo for number/currency formatting
ExportService.cs
•	Exports extracted data in various formats (PDF, Excel, JSON)

