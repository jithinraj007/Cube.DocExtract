using Microsoft.EntityFrameworkCore;
using TaskOne.Models;
using TaskOne.Services;

var builder = WebApplication.CreateBuilder(args);

// Register MSSQL Database Context using local connection string
builder.Services.AddDbContext<DocumentDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register our extraction & parsing services
builder.Services.AddSingleton<IOcrService, OcrService>();
builder.Services.AddScoped<ITextExtractionService, TextExtractionService>();
builder.Services.AddScoped<IDocumentParserService, DocumentParserService>();
builder.Services.AddScoped<IExportService, ExportService>();

// Add MVC Services
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Auto-initialize the SQL Server tables on startup using targeted raw SQL script (isolated execution)
try
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<DocumentDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        
        logger.LogInformation("Verifying and initializing targeted database tables...");
        
        string sqlScript = @"
            IF OBJECT_ID('dbo.UploadedDocuments', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.UploadedDocuments (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    FileName NVARCHAR(255) NOT NULL,
                    FileType NVARCHAR(50) NOT NULL,
                    FilePath NVARCHAR(500) NOT NULL,
                    UploadDate DATETIME NOT NULL,
                    Status NVARCHAR(50) NOT NULL,
                    ErrorMessage NVARCHAR(MAX) NULL,
                    ProcessingTimeMs INT NULL,
                    RawText NVARCHAR(MAX) NULL
                );
            END

            IF OBJECT_ID('dbo.DocumentMetadata', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.DocumentMetadata (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    DocumentId INT NOT NULL FOREIGN KEY REFERENCES dbo.UploadedDocuments(Id) ON DELETE CASCADE,
                    PoNumber NVARCHAR(100) NULL,
                    VendorDetails NVARCHAR(MAX) NULL,
                    PoDate DATETIME NULL,
                    DeliveryDate DATETIME NULL,
                    DeliverTo NVARCHAR(MAX) NULL
                );
            END

            IF OBJECT_ID('dbo.LineItems', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.LineItems (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    DocumentId INT NOT NULL FOREIGN KEY REFERENCES dbo.UploadedDocuments(Id) ON DELETE CASCADE,
                    PoNumber NVARCHAR(100) NULL,
                    Item NVARCHAR(MAX) NULL,
                    Quantity DECIMAL(18,4) NULL,
                    Rate DECIMAL(18,4) NULL,
                    TaxPercent DECIMAL(18,4) NULL,
                    TaxAmount DECIMAL(18,4) NULL,
                    Amount DECIMAL(18,4) NULL
                );
            END";

        context.Database.ExecuteSqlRaw(sqlScript);
        logger.LogInformation("Targeted database schema verified and active.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Critical: Error creating/checking database tables: {ex.Message}");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
