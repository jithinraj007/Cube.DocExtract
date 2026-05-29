using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tesseract;

namespace TaskOne.Services
{
    public interface IOcrService
    {
        Task EnsureInitializedAsync();
        string ExtractTextFromImage(byte[] imageBytes);
        string ExtractTextFromImageFile(string filePath);
        bool IsInitialized { get; }
    }

    public class OcrService : IOcrService
    {
        private readonly ILogger<OcrService> _logger;
        private readonly string _tessdataPath;
        private bool _isInitialized = false;

        public bool IsInitialized => _isInitialized;

        public OcrService(ILogger<OcrService> logger)
        {
            _logger = logger;
            // Store tessdata in the project root directory
            _tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        }

        public async Task EnsureInitializedAsync()
        {
            if (_isInitialized) return;

            try
            {
                if (!Directory.Exists(_tessdataPath))
                {
                    _logger.LogInformation("Creating tessdata directory at: {Path}", _tessdataPath);
                    Directory.CreateDirectory(_tessdataPath);
                }

                string trainedDataPath = Path.Combine(_tessdataPath, "eng.traineddata");

                if (!File.Exists(trainedDataPath))
                {
                    _logger.LogInformation("eng.traineddata not found. Downloading for offline use...");
                    
                    // URL to official fast English trained data
                    string downloadUrl = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata";
                    
                    using var httpClient = new HttpClient();
                    // Set timeout for large file download
                    httpClient.Timeout = TimeSpan.FromMinutes(5);
                    
                    var response = await httpClient.GetAsync(downloadUrl);
                    response.EnsureSuccessStatusCode();
                    
                    await using var fs = new FileStream(trainedDataPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fs);
                    
                    _logger.LogInformation("Successfully downloaded and cached eng.traineddata. OCR is now ready for offline operations.");
                }

                // Verify that we can instantiate the engine (checks if native libraries and tessdata are correct)
                using (var engine = new TesseractEngine(_tessdataPath, "eng", EngineMode.Default))
                {
                    _logger.LogInformation("Tesseract OCR engine successfully initialized and verified.");
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Tesseract OCR engine. Scanned PDFs and images may fail to process.");
                throw;
            }
        }

        public string ExtractTextFromImage(byte[] imageBytes)
        {
            if (!_isInitialized)
            {
                EnsureInitializedAsync().GetAwaiter().GetResult();
            }

            try
            {
                using var engine = new TesseractEngine(_tessdataPath, "eng", EngineMode.Default);
                using var img = Pix.LoadFromMemory(imageBytes);
                using var page = engine.Process(img);
                return page.GetText();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing OCR on image bytes.");
                return string.Empty;
            }
        }

        public string ExtractTextFromImageFile(string filePath)
        {
            if (!_isInitialized)
            {
                EnsureInitializedAsync().GetAwaiter().GetResult();
            }

            try
            {
                using var engine = new TesseractEngine(_tessdataPath, "eng", EngineMode.Default);
                using var img = Pix.LoadFromFile(filePath);
                using var page = engine.Process(img);
                return page.GetText();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing OCR on file: {Path}", filePath);
                return string.Empty;
            }
        }
    }
}
