# Implementation Reference Guide

## File-by-File Implementation Details

### 📋 MODELS & DATABASE

**File**: `Models/DocumentModels.cs`
```csharp
// All extraction fields defined as properties
UploadedDocument
  - Id, FileName, FileType, FilePath, Status, ErrorMessage, RawText
  - Relationships: Metadata (1:1), LineItems (1:Many)

DocumentMetadata
  - PoNumber
  - VendorDetails
  - PoDate, DeliveryDate
  - DeliverTo

LineItem
  - PoNumber, Item, Quantity, Rate
  - TaxPercent, TaxAmount, Amount

DocumentStatus (Enum-like)
  - Pending, Processing, Completed, Failed
```

---

### 🔍 TEXT EXTRACTION SERVICE

**File**: `Services/TextExtractionService.cs` (203 lines)

**Key Methods**:

1. **DetectFileType()** (Lines 25-82)
   - Magic byte detection: PDF, PNG, JPEG, ZIP/XLSX, XLS
   - Extension fallback
   - Returns: "PDF", "Image (PNG)", "Excel (XLSX)", etc.

2. **ExtractText()** (Lines 84-117)
   - Routes to appropriate extractor based on file type
   - Returns extracted text string

3. **ExtractTextFromPdf()** (Lines 119-180)
   - Uses UglyToad.PdfPig
   - Multi-page support
   - OCR fallback if PDF has no searchable text
   - Returns: Raw text + detected type ("PDF" or "Scanned PDF")

4. **ExtractTextFromImage()** (Lines 182-203)
   - Routes to OcrService
   - Returns OCR'd text

---

### 🧠 DOCUMENT PARSER SERVICE

**File**: `Services/DocumentParserService.cs` (714 lines)

**Key Methods**:

1. **ParseRawText()** (Lines 28-50)
   - Entry point for PDF/Image extraction
   - Calls all field extractors
   - Returns: (DocumentMetadata, List<LineItem>)

2. **ExtractPoNumber()** (Lines 65-90)
   - Multiple regex patterns
   - Validates non-empty result
   - Logging for debugging

3. **ExtractPoDate()** (Lines 92-95)
   - Calls `ExtractDateWithProximity()` with PO date keywords
   - Returns: DateTime?

4. **ExtractDeliveryDate()** (Lines 97-100)
   - Calls `ExtractDateWithProximity()` with delivery keywords
   - Returns: DateTime?

5. **ExtractDateWithProximity()** (Lines 102-145)
   - 6 different date format support
   - Keyword proximity logic
   - Progressive search: same line → next 2 lines → first 10 lines

6. **ExtractVendorDetails()** (Lines 147-150)
   - Keyword-based extraction
   - Address block assembly

7. **ExtractDeliverTo()** (Lines 152-155)
   - Similar to vendor extraction
   - Different keywords: "deliver to", "ship to", etc.

8. **ExtractAddressBlock()** (Lines 157-198)
   - Generic address extraction logic
   - 3-4 line capture
   - Header/total line filtering

9. **ExtractLineItemsFromText()** (Lines 200-500+)
   - Header detection
   - Number extraction via regex
   - Mathematical heuristic matching (Qty × Rate ≈ Amount)
   - Boundary detection (stops at "subtotal", "total")
   - Decimal parsing with comma handling

10. **ParseExcel()** (Lines 550+)
	- Uses ExcelDataReader library
	- Direct column mapping
	- Returns: (DocumentMetadata, List<LineItem>)

---

### 📸 OCR SERVICE

**File**: `Services/OcrService.cs` (124 lines)

**Key Methods**:

1. **EnsureInitializedAsync()** (Lines 31-60)
   - First-time initialization check
   - Creates tessdata directory if needed
   - Downloads `eng.traineddata` if missing
   - Initializes Tesseract engine
   - Fully offline after first run

2. **ExtractTextFromImage()** (Lines 62-80)
   - Accepts byte array
   - Initializes Tesseract
   - Returns OCR'd text

3. **ExtractTextFromImageFile()** (Lines 82-95)
   - File-based variant
   - Wraps byte array method

4. **IsInitialized** (Property)
   - Flags whether OCR is ready

**Dependencies**:
- Tesseract NuGet package
- `eng.traineddata` (bundled with project)

---

### 💾 EXPORT SERVICE

**File**: `Services/ExportService.cs` (215 lines)

**Key Methods**:

1. **ExportToExcel()** (Lines 15-75)
   - Creates XLSX workbook with 2 sheets
   - Sheet 1: "Line Items" with 8 columns
   - Sheet 2: "Metadata" with 6 columns
   - Formatted headers (bold, dark background)
   - Returns: byte[] (XLSX file content)

2. **ExportToCsvZip()** (Lines 77-130)
   - Creates ZIP archive
   - 2 CSV files: line_items.csv, metadata.csv
   - Returns: byte[] (ZIP content)

3. **ExportToJson()** (Lines 132-180)
   - Serializes documents with all relationships
   - Handles circular reference prevention
   - Pretty-prints JSON
   - Returns: string (JSON content)

---

### 🎮 DOCUMENT CONTROLLER

**File**: `Controllers/DocumentController.cs` (392 lines)

**Key Methods**:

1. **GetDocuments()** (Lines 53-70)
   - API: GET `/api/document/list`
   - Returns all documents with metadata and line items
   - Error handling: catches and logs exceptions

2. **UploadDocument()** (Lines 82-127)
   - API: POST `/api/document/upload`
   - File validation
   - Saves to disk and database
   - Returns: Document object (HTTP 200)
   - Launches background processing

3. **ProcessDocumentAsync()** (Lines 129-230)
   - Background processing worker
   - Status: Pending → Processing → Completed/Failed
   - Detects file type
   - Routes to appropriate extractor
   - Saves metadata and line items
   - Logs processing time
   - Error handling with status update

4. **GetDocumentStatus()** (Lines 72-80)
   - API: GET `/api/document/status/{id}`
   - Real-time status polling
   - Returns: { status, errorMessage }

5. **SaveDocument()** (Lines 240-300+)
   - API: POST `/api/document/save`
   - Manual update of metadata and line items
   - User-editable fields in dashboard

---

### 🌐 DATABASE INITIALIZATION

**File**: `Program.cs` (Lines 23-84)

**Key Setup**:
```csharp
// Register DbContext
builder.Services.AddDbContext<DocumentDbContext>(options =>
	options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Auto-create tables on startup
// Uses raw SQL script for robustness
// Creates: UploadedDocuments, DocumentMetadata, LineItems
```

---

### 🎨 FRONTEND

**File**: `Views/Home/Index.cshtml`
- Upload interface with drag-and-drop
- File input with `multiple` attribute (for batch upload)
- Processing queue display
- Document table with search, filtering, bulk actions

**File**: `wwwroot/js/dashboard.js` (597 lines)
- File upload handler (now supports multiple files)
- Real-time status polling
- Data display and rendering
- Export trigger
- Bulk selection and delete

---

## 🔄 DATA FLOW DIAGRAM

```
┌─────────────────────────────────────┐
│  FILE UPLOAD (Single or Multiple)   │
│  - Browser File Input / Drag-Drop   │
└──────────────┬──────────────────────┘
			   ↓
┌─────────────────────────────────────┐
│  DocumentController.UploadDocument()│
│  - Save file to disk                │
│  - Create DB record (Pending)       │
│  - Return document ID               │
└──────────────┬──────────────────────┘
			   ↓
┌─────────────────────────────────────┐
│  ProcessDocumentAsync() [Background]│
│  - Update status: Processing        │
└──────────────┬──────────────────────┘
			   ↓
┌─────────────────────────────────────┐
│  TextExtractionService              │
│  - DetectFileType()                 │
│    ├─ PDF → ExtractTextFromPdf()   │
│    ├─ Image → OCR via OcrService()  │
│    └─ Excel → ExcelDataReader       │
└──────────────┬──────────────────────┘
			   ↓
┌─────────────────────────────────────┐
│  DocumentParserService              │
│  - ParseRawText() or ParseExcel()   │
│  - Extract all fields               │
│  - Build metadata & line items      │
└──────────────┬──────────────────────┘
			   ↓
┌─────────────────────────────────────┐
│  Database Storage                   │
│  - DocumentMetadata                 │
│  - LineItems                        │
│  - Update Document Status           │
└──────────────┬──────────────────────┘
			   ↓
┌─────────────────────────────────────┐
│  Update Status: Completed           │
│  - Log processing time              │
│  - Frontend polls and displays       │
└─────────────────────────────────────┘
```

---

## 🧪 TESTING COMMANDS

```bash
# Test PO extraction with sample image
# Upload a PO invoice (PDF or image)
# Verify extracted fields match source document

# Test Excel export
# Select documents and click "Export to Excel"
# Verify: Line Items sheet + Metadata sheet

# Test offline operation
# Disconnect internet
# Verify OCR and document processing still works

# Test multiple file upload
# Select 3+ files at once
# Verify all queue and process sequentially
```

---

## 📝 CONFIGURATION

**File**: `appsettings.json`
```json
{
  "ConnectionStrings": {
	"DefaultConnection": "Server=YOUR_SERVER;Database=Jithinz;Trusted_Connection=True;"
  }
}
```

**File**: `appsettings.Development.json`
```json
{
  "Logging": {
	"LogLevel": {
	  "Default": "Information"
	}
  }
}
```

---

## 🚀 DEPLOYMENT NOTES

1. **Tesseract Training Data**
   - Location: `tessdata/eng.traineddata`
   - Size: ~65 MB
   - Download location: `https://github.com/UB-Mannheim/tesseract/wiki`
   - Status: Auto-downloaded on first OCR use if missing

2. **Database**
   - Location: MSSQL Server instance
   - Schema: Auto-created on application start
   - Connection: Configurable via appsettings.json

3. **Upload Folder**
   - Location: `wwwroot/uploads`
   - Auto-created on first upload
   - File permissions: Read/Write

4. **Logging**
   - Output: Console (debug) + Event Log (production)
   - Configured: Microsoft.Extensions.Logging

---

## ✅ VERIFICATION CHECKLIST

- [x] All extraction fields implemented
- [x] File type detection working
- [x] Digital PDF extraction working
- [x] Scanned PDF/Image OCR working
- [x] Excel parsing working
- [x] Database storage working
- [x] Export (Excel/CSV/JSON) working
- [x] Status tracking working
- [x] Logging implemented
- [x] Error handling implemented
- [x] Offline operation verified
- [x] Multiple file upload working

**Status**: ✅ PRODUCTION READY
