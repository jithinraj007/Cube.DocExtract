# Architecture & Flow Diagrams

## 🏗️ System Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      File Extracter Application                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────────────┐     ┌──────────────────┐     ┌────────────────┐  │
│  │   Web UI Layer  │     │   API Controller │     │ Export Service │  │
│  │                 │────▶│                  │────▶│                │  │
│  │ • File Upload   │     │ DocumentCtrl     │     │ • Excel        │  │
│  │ • Dashboard     │     │ • Upload handler │     │ • CSV          │  │
│  │ • Real-time     │     │ • Status polling │     │ • JSON         │  │
│  │   Status        │     │                  │     │                │  │
│  └─────────────────┘     └──────────────────┘     └────────────────┘  │
│         ↑                         ↑                         ↑           │
│         │                         │                         │           │
│         └─────────────────────────┼─────────────────────────┘           │
│                                   ↓                                     │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │              Text Extraction & Parsing Layer                   │   │
│  ├─────────────────────────────────────────────────────────────────┤   │
│  │                                                                 │   │
│  │  ┌────────────────────┐    ┌─────────────────┐                │   │
│  │  │ TextExtraction     │    │ DocumentParser  │                │   │
│  │  │ Service            │───▶│ Service         │                │   │
│  │  │                    │    │                 │                │   │
│  │  │ • File detection   │    │ • PO extraction │                │   │
│  │  │ • PDF extraction   │    │ • Date parsing  │                │   │
│  │  │ • Image extract    │    │ • Vendor extract│                │   │
│  │  │ • OCR routing      │    │ • Line items    │                │   │
│  │  │                    │    │                 │                │   │
│  │  └────────────────────┘    └─────────────────┘                │   │
│  │          ↓                          ↓                         │   │
│  │  ┌─────────────────┐        ┌──────────────┐                │   │
│  │  │  OCR Service    │        │ Excel Parser │                │   │
│  │  │ (Tesseract 5)   │        │              │                │   │
│  │  │                 │        │ ExcelData    │                │   │
│  │  │ • Image OCR     │        │ Reader       │                │   │
│  │  │ • Scanned PDF   │        │              │                │   │
│  │  │ • eng.traindata │        │              │                │   │
│  │  │   (offline)     │        │              │                │   │
│  │  └─────────────────┘        └──────────────┘                │   │
│  │                                                                 │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                   ↓                                     │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │              Database Layer (MSSQL)                            │   │
│  ├─────────────────────────────────────────────────────────────────┤   │
│  │                                                                 │   │
│  │  ┌─────────────────────┐  ┌──────────────┐  ┌──────────────┐  │   │
│  │  │ UploadedDocuments   │  │ DocumentMeta │  │  LineItems   │  │   │
│  │  │ (Master Table)      │  │   data       │  │              │  │   │
│  │  │                     │  │              │  │              │  │   │
│  │  │ • Id (PK)          │  │ • Id (PK)    │  │ • Id (PK)   │  │   │
│  │  │ • FileName         │  │ • DocumentId │  │ • DocumentId│  │   │
│  │  │ • FileType         │  │ • PoNumber   │  │ • PoNumber  │  │   │
│  │  │ • Status           │  │ • VendorInfo │  │ • Item      │  │   │
│  │  │ • RawText          │  │ • Dates      │  │ • Qty       │  │   │
│  │  │ • ErrorMessage     │  │ • DeliverTo  │  │ • Rate      │  │   │
│  │  │ • ProcessingTimeMs │  │              │  │ • Tax%      │  │   │
│  │  │                     │  │              │  │ • Amount    │  │   │
│  │  └─────────────────────┘  └──────────────┘  └──────────────┘  │   │
│  │           1:1                   1:1              1:Many        │   │
│  │          Relationship          Relationship     Relationship   │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 📊 Data Flow: Document Upload & Processing

```
┌──────────────┐
│   User       │
│ Selects File │
└──────┬───────┘
	   │
	   ↓
┌────────────────────────────────────────┐
│ Browser (File Input / Drag-Drop)       │
│ - Validate file extension              │
│ - Create FormData                      │
│ - Show progress bar                    │
└──────┬─────────────────────────────────┘
	   │
	   ↓ POST /api/document/upload
┌────────────────────────────────────────┐
│ DocumentController.UploadDocument()     │
│ - Receive file                         │
│ - Save to disk (wwwroot/uploads/)      │
│ - Create DB record (Status: Pending)   │
│ - Return document ID                   │
└──────┬─────────────────────────────────┘
	   │
	   ↓
┌────────────────────────────────────────┐
│ Background: ProcessDocumentAsync()     │
│ - Update Status: Processing            │
└──────┬─────────────────────────────────┘
	   │
	   ↓
┌────────────────────────────────────────┐
│ TextExtractionService.DetectFileType() │
│ - Check magic bytes                    │
│ - Identify: PDF/Image/Excel            │
└──────┬─────────────────────────────────┘
	   │
	   ├─────────────┬──────────────┬──────────────┐
	   │             │              │              │
	   ↓             ↓              ↓              ↓
   (PDF)       (Image)         (Excel)        (Unknown)
	│             │               │              │
	├─────────────┴───────────────┴──────────────┤
	│                                             │
	↓                                             │
┌──────────────────────────────┐                 │
│ PDF: ExtractTextFromPdf()    │                 │
│ - Try native text extraction │                 │
│ - If text > 100 chars:       │                 │
│   ✓ Return text              │                 │
│ - Else:                      │                 │
│   → Fall through to OCR      │                 │
└──────────────┬───────────────┘                 │
			   │                                  │
			   ↓                                  │
		(Need OCR?)                              │
		 ↓          ↓                            │
		Yes         No                           │
		 │           │                           │
		 ↓           └──────────────┐             │
	OCR needed              Continue              │
		 │                      ↓                │
		 ↓                      │                │
	┌──────────────┐            │                │
	│ OcrService   │            │                │
	│ Extract...   │            │                │
	│ FromImage()  │            │                │
	└──────┬───────┘            │                │
		   │                     │                │
		   └──────────┬──────────┘                │
					  │                          │
					  ↓                          │
			┌──────────────────────┐             │
			│ ExcelDataReader      │◄────────────┤
			│ ParseExcel()         │
			│ - Direct column map  │
			└──────┬───────────────┘
				   │
				   ↓
		 ┌─────────────────────────┐
		 │ DocumentParserService   │
		 │ ParseRawText() or       │
		 │ ParseExcel()            │
		 │                         │
		 │ Extract:                │
		 │ • PO Number             │
		 │ • Dates                 │
		 │ • Vendor                │
		 │ • Deliver To            │
		 │ • Line Items            │
		 └─────────┬───────────────┘
				   │
				   ↓
		 ┌─────────────────────────┐
		 │ Database Operations     │
		 │                         │
		 │ - Save DocumentMetadata │
		 │ - Save LineItems        │
		 │ - Update Status         │
		 │ - Store RawText         │
		 │ - Log ProcessingTimeMs  │
		 └─────────┬───────────────┘
				   │
				   ↓
		 ┌─────────────────────────┐
		 │ Update Status:          │
		 │ Completed / Failed      │
		 │                         │
		 │ Frontend polling detects│
		 │ status change and       │
		 │ refreshes dashboard     │
		 └─────────────────────────┘
```

---

## 🔀 Field Extraction Logic

```
EXTRACTION PIPELINE
───────────────────

Raw Text Input (PDF/Image/Excel)
		│
		├─────────────────────────────┐
		│                             │
		↓                             ↓
   DOCUMENT METADATA             LINE ITEMS
   ──────────────────────        ──────────
		│                             │
		├─ PO Number                  │
		│  Extraction                 │
		│  └─ Regex Match             │
		│     Pattern 1: "PO-XXXX"    │
		│     Pattern 2: "PO XXXX"    │
		│     Pattern 3: "P.O. XXXX"  │
		│                             │
		├─ Date Extraction            │
		│  ├─ PO Date                 ├─ Header Detection
		│  │  Keywords:               │  Find: "qty", "rate", "amount"
		│  │  "po date", "date:"      │
		│  │                          ├─ Number Extraction
		│  ├─ Delivery Date           │  Regex: \d+(\.\d+)?
		│  │  Keywords:               │
		│  │  "delivery", "due date"  ├─ Heuristic Matching
		│  │                          │  Test: Qty × Rate ≈ Amount
		│  └─ Logic:                  │
		│     Search same line →      ├─ Boundary Detection
		│     next 2 lines →          │  Stop at: "subtotal", "total"
		│     first 10 lines          │
		│                             └─ Parse & Store
		├─ Vendor Details                     Each Line Item
		│  └─ Keywords: "vendor",
		│     "supplier", "from:"
		│     → Extract address block
		│
		└─ Deliver To
		   └─ Keywords: "deliver to",
			  "ship to", "recipient"
			  → Extract address block

MATHEMATICAL HEURISTICS FOR LINE ITEMS
──────────────────────────────────────

For each line containing 2+ numbers:

Test Pattern 1 (Standard):
  values[0] × values[1] ≈ values[2]?
  If YES → Qty=v0, Rate=v1, Amount=v2

Test Pattern 2 (With Tax):
  values[0] × values[1] × (1 + values[2]/100) ≈ values[4]?
  If YES → Qty=v0, Rate=v1, Tax%=v2, Amount=v4

Test Pattern 3 (Alternative):
  values[0] × values[1] = values[3]? (Qty × Rate = Amount)
  values[2] × values[3] / 100 = values[4]? (Amount × Tax% / 100 = Tax)
  If YES → Use this arrangement

Fallback:
  Assign based on magnitude:
  - Smallest decimal = Tax% (typically < 20)
  - Next smallest = Rate
  - Larger values = Qty, Amount
```

---

## 📈 Status Tracking State Machine

```
╔════════════════════════════════════════════════════════════════╗
║              Document Processing Status Flow                  ║
╚════════════════════════════════════════════════════════════════╝

					┌─────────────────┐
					│   User Upload   │
					│     Document    │
					└────────┬────────┘
							 │
							 ↓
					┌─────────────────┐
					│    PENDING      │
					│                 │
					│ • Queued        │
					│ • Awaiting      │
					│   Processing    │
					└────────┬────────┘
							 │
					(Background Worker Starts)
							 │
							 ↓
					┌─────────────────┐
					│  PROCESSING     │
					│                 │
					│ • File type     │
					│   detection     │
					│ • Text          │
					│   extraction    │
					│ • Data parsing  │
					│ • DB storing    │
					└────────┬────────┘
							 │
					┌────────┴────────┐
					│                 │
					↓                 ↓
		(Success?)      (Error?)
		   │               │
		   ↓               ↓
	┌─────────────┐   ┌──────────────┐
	│  COMPLETED  │   │    FAILED    │
	│             │   │              │
	│ • All data  │   │ • Error      │
	│   extracted │   │   message    │
	│ • DB stored │   │ • Status set │
	│ • Ready for │   │ • Logged     │
	│   export    │   │              │
	└─────────────┘   └──────────────┘
		   │                │
		   └────────┬───────┘
					│
					↓
		(User Views in Dashboard)
		(Can Export or Retry)

ERROR HANDLING DURING STATE TRANSITIONS
────────────────────────────────────────

Pending → Processing
  ✓ Mark start time
  ✓ Create scope for DbContext
  ✓ Initialize OCR if needed

Processing → Completed
  ✓ Calculate processing time
  ✓ Store RawText
  ✓ Save metadata & line items
  ✓ Update status
  ✓ Log success

Processing → Failed
  ✗ Catch exception
  ✗ Store error message
  ✗ Update status to Failed
  ✗ Log full stack trace
  ✗ Notify user via API
```

---

## 💾 Database Relationship Diagram

```
┌──────────────────────────────────┐
│    UploadedDocuments (Master)    │
│                                  │
│ PK: Id                           │
│ • FileName                       │
│ • FileType                       │
│ • FilePath                       │
│ • UploadDate                     │
│ • Status                         │
│ • ErrorMessage                   │
│ • ProcessingTimeMs               │
│ • RawText (Full OCR/PDF text)   │
│                                  │
└──────────────┬───────────────────┘
			   │
		┌──────┴──────┐
		│             │
		│ 1:1         │ 1:Many
		│             │
		↓             ↓
┌───────────────────┐  ┌──────────────────────┐
│DocumentMetadata   │  │    LineItems         │
│                   │  │                      │
│ PK: Id            │  │ PK: Id               │
│ FK: DocumentId    │  │ FK: DocumentId       │
│ • PoNumber        │  │ • PoNumber           │
│ • VendorDetails   │  │ • Item               │
│ • PoDate          │  │ • Quantity           │
│ • DeliveryDate    │  │ • Rate               │
│ • DeliverTo       │  │ • TaxPercent         │
│                   │  │ • TaxAmount          │
└───────────────────┘  │ • Amount             │
					   │                      │
					   └──────────────────────┘

RELATIONSHIPS
─────────────

UploadedDocuments → DocumentMetadata
  Type: 1:1
  Cascade: ON DELETE CASCADE
  (When document deleted, metadata deleted)

UploadedDocuments → LineItems
  Type: 1:Many
  Cascade: ON DELETE CASCADE
  (When document deleted, all line items deleted)

INDICES
──────

• UploadedDocuments.Id (Clustered Primary Key)
• DocumentMetadata.DocumentId (Foreign Key)
• LineItems.DocumentId (Foreign Key)

CONSTRAINTS
──────────

• UploadedDocuments.Status ∈ {Pending, Processing, Completed, Failed}
• DocumentMetadata.DocumentId (NOT NULL, Foreign Key)
• LineItems.DocumentId (NOT NULL, Foreign Key)
• LineItems Numeric Fields: Decimal(18, 4)
```

---

## 🔄 Multiple File Upload Sequence

```
USER SELECTION: Select 3 Files at Once
│
├─ file1.pdf
├─ file2.jpg
└─ file3.xlsx

					↓

BROWSER JAVASCRIPT
│
├─ Validate each file
│  ├─ Check extension
│  └─ Check MIME type
│
├─ Create UI queue items
│  ├─ Queue Item 1: file1.pdf (0%)
│  ├─ Queue Item 2: file2.jpg (0%)
│  └─ Queue Item 3: file3.xlsx (0%)
│
└─ Upload sequentially with 100ms delay

					↓

UPLOAD SEQUENCE
│
├─ Upload #1: file1.pdf
│  ├─ POST to /api/document/upload
│  ├─ Show progress: 25% → 50% → 100%
│  ├─ Server returns Document ID: 1
│  ├─ Remove from queue (after 2s)
│  └─ Wait 100ms
│
├─ Upload #2: file2.jpg
│  ├─ POST to /api/document/upload
│  ├─ Show progress: 25% → 50% → 100%
│  ├─ Server returns Document ID: 2
│  ├─ Remove from queue (after 2s)
│  └─ Wait 100ms
│
└─ Upload #3: file3.xlsx
   ├─ POST to /api/document/upload
   ├─ Show progress: 25% → 50% → 100%
   ├─ Server returns Document ID: 3
   ├─ Remove from queue (after 2s)
   └─ Wait 100ms

					↓

PARALLEL BACKGROUND PROCESSING
│
├─ Document 1 (file1.pdf)
│  ├─ Status: Pending
│  ├─ Start Processing → Detect PDF type
│  ├─ Extract text → Parse fields
│  └─ Status: Completed (6126ms)
│
├─ Document 2 (file2.jpg)
│  ├─ Status: Pending
│  ├─ Start Processing → Detect Image type
│  ├─ OCR scan → Parse fields
│  └─ Status: Completed (8234ms)
│
└─ Document 3 (file3.xlsx)
   ├─ Status: Pending
   ├─ Start Processing → Detect Excel type
   ├─ Parse directly → Extract fields
   └─ Status: Completed (1245ms)

					↓

FRONTEND POLLING (Every 2 seconds)
│
├─ Poll /api/document/list
├─ Receive updated documents
├─ Check for Completed status
├─ Update dashboard table
└─ Show green checkmark when completed

					↓

USER SEES
│
├─ Document 1: COMPLETED ✅
├─ Document 2: COMPLETED ✅
└─ Document 3: COMPLETED ✅
   Ready to Export!
```

---

## 📊 Export Format Examples

### Excel Export Structure
```
Sheet 1: "Line Items"
┌─────────┬──────────────┬──────────┬───────┬─────────┬─────┬─────────────┐
│ PO #    │ Item         │ Qty      │ Rate  │ Tax %   │ Tax │ Amount      │
├─────────┼──────────────┼──────────┼───────┼─────────┼─────┼─────────────┤
│ PO-0020 │ Fresh Carr.. │ 20.00    │ 3.00  │ 5.00    │ 2.85│ 60.00       │
│ PO-0020 │ Fresh Tom..  │ 15.00    │ 5.00  │ 5.00    │ 3.57│ 75.00       │
│ PO-0020 │ Dried Onion  │ 5.00     │ 7.00  │ 5.00    │ 1.66│ 35.00       │
│ PO-0020 │ Dried Tom..  │ 5.00     │ 6.00  │ 5.00    │ 1.43│ 30.00       │
│ PO-0020 │ Dried Mang.. │ 15.00    │ 14.00 │ 5.00    │ 9.99│ 210.00      │
└─────────┴──────────────┴──────────┴───────┴─────────┴─────┴─────────────┘

Sheet 2: "Metadata"
┌─────────┬──────────────────┬──────────────────────────────┬────────────┐
│ PO #    │ Vendor           │ Delivery Address             │ PO Date    │
├─────────┼──────────────────┼──────────────────────────────┼────────────┤
│ PO-0020 │ Wild Berries ... │ Cube Innovators Technologies │ 2025-03-06 │
└─────────┴──────────────────┴──────────────────────────────┴────────────┘
```

### CSV Export (ZIP Contents)
```
archive.zip
├── line_items.csv
│   PO#,Item,Qty,Rate,Tax%,Tax,Amount,SourceFile
│   PO-0020,Fresh Carrots,20,3,5,2.85,60,PO-00020.pdf
│   ...
│
└── metadata.csv
	PO#,Vendor,DeliverTo,PoDate,DeliveryDate,SourceFile
	PO-0020,Wild Berries...,Cube Innovators,2025-03-06,,PO-00020.pdf
	...
```

### JSON Export
```json
[
  {
	"id": 5,
	"fileName": "PO-00020.pdf",
	"fileType": "PDF",
	"status": "Completed",
	"uploadDate": "2025-03-06T10:30:00",
	"metadata": {
	  "id": 1,
	  "poNumber": "PO-00020",
	  "vendorDetails": "Wild Berries L.L.C",
	  "poDate": "2025-03-06",
	  "deliveryDate": null,
	  "deliverTo": "Cube Innovators Technologies"
	},
	"lineItems": [
	  {
		"id": 1,
		"poNumber": "PO-00020",
		"item": "Fresh Carrots (1kg)",
		"quantity": 20.0,
		"rate": 3.0,
		"taxPercent": 5.0,
		"taxAmount": 2.85,
		"amount": 60.0
	  },
	  ...
	]
  }
]
```

---

**Diagram Complete** ✅  
All flows, relationships, and processes documented.
