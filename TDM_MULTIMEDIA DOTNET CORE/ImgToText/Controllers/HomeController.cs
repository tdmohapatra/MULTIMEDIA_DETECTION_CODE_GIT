
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using Tesseract;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Threading.Tasks;
using ImgToText.Services;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
public class HomeController : Controller
{
    private readonly IImageTextService _imageTextService;
    private readonly string uploadsPath;
    // GET: Index action to render the main view

    public HomeController(IImageTextService imageTextService)
    {
        _imageTextService = imageTextService;
        uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        Directory.CreateDirectory(uploadsPath);
    }
    public IActionResult ImageToTextBasic()
    {
        return View();
    }
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file, string cameraFile)
    {
        string extractedText = string.Empty;
        float confidence = 0.0f;
        string processedImagePath = string.Empty;

        try
        {
            // Handle camera file (base64 image)
            if (!string.IsNullOrEmpty(cameraFile))
            {
                var base64Data = cameraFile.Split(',').Length > 1 ? cameraFile.Split(',')[1] : cameraFile;
                var imageBytes = Convert.FromBase64String(base64Data);

                // Save the original image
                var fileName = $"camera_capture_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                processedImagePath = Path.Combine(uploadsPath, fileName);
                await System.IO.File.WriteAllBytesAsync(processedImagePath, imageBytes);

                // Extract text with confidence
                var result = _imageTextService.ExtractTextWithConfidence(processedImagePath);
                extractedText = result.Text;
                confidence = result.Confidence;

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    // Try alternative extraction method
                    extractedText = _imageTextService.ExtractTextFromImageWithPreprocessing(processedImagePath);
                    confidence = 60.0f; // Default confidence for fallback
                }

                // Clean up if no text found
                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    System.IO.File.Delete(processedImagePath);
                    processedImagePath = null;
                }
            }
            // Handle uploaded file
            else if (file != null && file.Length > 0)
            {
                var fileName = $"{Path.GetFileNameWithoutExtension(file.FileName)}_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(file.FileName)}";
                var filePath = Path.Combine(uploadsPath, fileName);

                // Save the uploaded file
                await using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Extract text with confidence
                var result = _imageTextService.ExtractTextWithConfidence(filePath);
                extractedText = result.Text;
                confidence = result.Confidence;

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    // Try alternative extraction method
                    extractedText = _imageTextService.ExtractTextFromImageWithPreprocessing(filePath);
                    confidence = 60.0f; // Default confidence for fallback
                }

                // Clean up if no text found
                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    System.IO.File.Delete(filePath);
                }
                else
                {
                    processedImagePath = fileName; // Return just filename for security
                }
            }
            else
            {
                return BadRequest(new { error = "No image data provided" });
            }

            return Json(new
            {
                success = !string.IsNullOrWhiteSpace(extractedText),
                extractedText = extractedText ?? "",
                confidence = confidence,
                processedImagePath = processedImagePath,
                message = !string.IsNullOrWhiteSpace(extractedText) ?
                         "Text extracted successfully" :
                         "No text found in image"
            });
        }
        catch (Exception ex)
        {
            // Clean up on error
            if (!string.IsNullOrEmpty(processedImagePath) && System.IO.File.Exists(processedImagePath))
            {
                System.IO.File.Delete(processedImagePath);
            }

            return StatusCode(500, new
            {
                success = false,
                error = "Error processing image: " + ex.Message,
                extractedText = "",
                confidence = 0
            });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UploadEnhanced(IFormFile file, string cameraFile)
    {
        try
        {
            string extractedText = string.Empty;
            string compressedImagePath = string.Empty;
            float confidence = 0.0f;

            if (!string.IsNullOrEmpty(cameraFile))
            {
                // Handle camera capture
                var base64Data = cameraFile.Split(',').Length > 1 ? cameraFile.Split(',')[1] : cameraFile;
                var imageBytes = Convert.FromBase64String(base64Data);

                var tempImagePath = Path.Combine(uploadsPath, $"camera_temp_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                await System.IO.File.WriteAllBytesAsync(tempImagePath, imageBytes);

                // Try enhanced processing with compression
                try
                {
                    var result = _imageTextService.ProcessImageWithCompression(tempImagePath);
                    extractedText = result.Text;
                    compressedImagePath = result.CompressedImagePath;

                    // Get confidence for enhanced processing
                    var confidenceResult = _imageTextService.ExtractTextWithConfidence(compressedImagePath);
                    confidence = confidenceResult.Confidence;
                }
                catch (NotImplementedException)
                {
                    // Fallback to regular processing
                    var processedPath = _imageTextService.PreprocessAndSaveImage(tempImagePath);
                    extractedText = _imageTextService.ExtractTextFromImageTDMWithPreprocessing(processedPath);
                    compressedImagePath = processedPath;

                    var confidenceResult = _imageTextService.ExtractTextWithConfidence(processedPath);
                    confidence = confidenceResult.Confidence;
                }

                // Clean up temp file
                System.IO.File.Delete(tempImagePath);
            }
            else if (file != null && file.Length > 0)
            {
                // Handle uploaded file
                var filePath = Path.Combine(uploadsPath, file.FileName);
                await using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                try
                {
                    var result = _imageTextService.ProcessImageWithCompression(filePath);
                    extractedText = result.Text;
                    compressedImagePath = result.CompressedImagePath;

                    var confidenceResult = _imageTextService.ExtractTextWithConfidence(compressedImagePath);
                    confidence = confidenceResult.Confidence;
                }
                catch (NotImplementedException)
                {
                    // Fallback to regular processing
                    var processedPath = _imageTextService.PreprocessAndSaveImage(filePath);
                    extractedText = _imageTextService.ExtractTextFromImageTDMWithPreprocessing(processedPath);
                    compressedImagePath = processedPath;

                    var confidenceResult = _imageTextService.ExtractTextWithConfidence(processedPath);
                    confidence = confidenceResult.Confidence;
                }

                // Clean up original file
                System.IO.File.Delete(filePath);
            }
            else
            {
                return BadRequest(new { success = false, error = "No image data provided" });
            }

            return Json(new
            {
                success = !string.IsNullOrWhiteSpace(extractedText),
                extractedText = extractedText ?? "",
                confidence = confidence,
                compressedImagePath = compressedImagePath != null ? Path.GetFileName(compressedImagePath) : null,
                message = !string.IsNullOrWhiteSpace(extractedText) ?
                         "Image processed with enhanced OCR and compression" :
                         "No text found in image"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message,
                extractedText = "",
                confidence = 0
            });
        }
    }

    [HttpPost]
    public IActionResult SaveTextAsTxt([FromBody] SaveTextRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request?.Text))
            {
                return BadRequest(new { success = false, error = "Text is required" });
            }

            var fileName = $"extracted_text_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var filePath = Path.Combine(uploadsPath, fileName);

            System.IO.File.WriteAllText(filePath, request.Text);

            return Json(new
            {
                success = true,
                filePath = fileName,
                message = "Text saved as TXT successfully"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    [HttpPost]
    public IActionResult SaveTextAsDocx([FromBody] SaveTextRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request?.Text))
            {
                return BadRequest(new { success = false, error = "Text is required" });
            }

            var result = _imageTextService.SaveTextAsDocx(request.Text);
            return Json(new
            {
                success = result.Success,
                filePath = result.FilePath,
                message = result.Success ? "Text saved as DOCX successfully" : result.FilePath
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    [HttpPost]
    public IActionResult SaveTextAsPdf([FromBody] SaveTextRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request?.Text))
            {
                return BadRequest(new { success = false, error = "Text is required" });
            }

            // For PDF saving, implement this in your service
            // For now, we'll save as TXT as a fallback
            var fileName = $"extracted_text_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var filePath = Path.Combine(uploadsPath, fileName);

            // Simple PDF creation - you might want to use a proper PDF library
            var pdfContent = $"%PDF-1.4\n1 0 obj\n<<\n/Type /Catalog\n/Pages 2 0 R\n>>\nendobj\n2 0 obj\n<<\n/Type /Pages\n/Kids [3 0 R]\n/Count 1\n>>\nendobj\n3 0 obj\n<<\n/Type /Page\n/Parent 2 0 R\n/Resources <<\n/Font <<\n/F1 4 0 R\n>>\n>>\n/MediaBox [0 0 612 792]\n/Contents 5 0 R\n>>\nendobj\n4 0 obj\n<<\n/Type /Font\n/Subtype /Type1\n/BaseFont /Helvetica\n>>\nendobj\n5 0 obj\n<<\n/Length 100\n>>\nstream\nBT\n/F1 12 Tf\n72 720 Td\n({EscapePdfString(request.Text)}) Tj\nET\nendstream\nendobj\nxref\n0 6\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \n0000000115 00000 n \n0000000274 00000 n \n0000000382 00000 n \ntrailer\n<<\n/Size 6\n/Root 1 0 R\n>>\nstartxref\n492\n%%EOF";

            System.IO.File.WriteAllText(filePath, pdfContent);

            return Json(new
            {
                success = true,
                filePath = fileName,
                message = "Text saved as PDF successfully"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    [HttpPost]
    public IActionResult ProcessAndCompare([FromBody] ProcessRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.ImagePath))
            {
                return BadRequest(new { success = false, error = "Image path is required" });
            }

            var fullPath = Path.Combine(uploadsPath, request.ImagePath);
            if (!System.IO.File.Exists(fullPath))
            {
                return BadRequest(new { success = false, error = "Image file not found" });
            }

            var comparisonResult = _imageTextService.CompareOcrResults(fullPath);

            return Json(new
            {
                success = true,
                originalText = comparisonResult.OriginalText,
                processedText = comparisonResult.ProcessedText,
                originalConfidence = comparisonResult.OriginalConfidence,
                processedConfidence = comparisonResult.ProcessedConfidence,
                improvement = comparisonResult.ProcessedConfidence - comparisonResult.OriginalConfidence
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    [HttpPost]
    public IActionResult ProcessBlurredImage([FromBody] ProcessRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.ImagePath))
            {
                return BadRequest(new { success = false, error = "Image path is required" });
            }

            var fullPath = Path.Combine(uploadsPath, request.ImagePath);
            if (!System.IO.File.Exists(fullPath))
            {
                return BadRequest(new { success = false, error = "Image file not found" });
            }

            string text;
            try
            {
                text = _imageTextService.ExtractTextFromBlurredImage(fullPath);
            }
            catch (NotImplementedException)
            {
                text = _imageTextService.ExtractTextFromImageWithPreprocessing(fullPath);
            }

            var confidenceResult = _imageTextService.ExtractTextWithConfidence(fullPath);

            return Json(new
            {
                success = true,
                text = text ?? "",
                confidence = confidenceResult.Confidence,
                isReadable = !string.IsNullOrWhiteSpace(text) && confidenceResult.Confidence > 50.0f
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    [HttpGet]
    public IActionResult GetAvailableLanguages()
    {
        try
        {
            var languages = _imageTextService.GetAvailableLanguages();
            return Json(new
            {
                success = true,
                languages = languages ?? new List<string> { "eng", "fra", "deu", "spa" }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message,
                languages = new List<string> { "eng" }
            });
        }
    }

    [HttpPost]
    public IActionResult CheckImageReadability([FromBody] ProcessRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.ImagePath))
            {
                return BadRequest(new { success = false, error = "Image path is required" });
            }

            var fullPath = Path.Combine(uploadsPath, request.ImagePath);
            if (!System.IO.File.Exists(fullPath))
            {
                return BadRequest(new { success = false, error = "Image file not found" });
            }

            var isReadable = _imageTextService.IsImageReadable(fullPath);
            var confidenceResult = _imageTextService.ExtractTextWithConfidence(fullPath);

            return Json(new
            {
                success = true,
                isReadable,
                confidence = confidenceResult.Confidence,
                textSample = confidenceResult.Text?.Substring(0, Math.Min(50, confidenceResult.Text?.Length ?? 0)) + "..."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    [HttpGet]
    public IActionResult DownloadFile(string fileName, string type = "text")
    {
        try
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return BadRequest("File name is required");
            }

            var filePath = Path.Combine(uploadsPath, fileName);
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("File not found");
            }

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            var contentType = GetContentType(fileName);

            return File(fileBytes, contentType, fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error downloading file: {ex.Message}");
        }
    }

    [HttpPost]
    public IActionResult ExtractTextWithLanguage([FromBody] ExtractTextRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request?.ImagePath))
            {
                return BadRequest(new { success = false, error = "Image path is required" });
            }

            var fullPath = Path.Combine(uploadsPath, request.ImagePath);
            if (!System.IO.File.Exists(fullPath))
            {
                return BadRequest(new { success = false, error = "Image file not found" });
            }

            var text = _imageTextService.ExtractTextWithLanguage(fullPath, request.Language);
            return Json(new
            {
                success = true,
                text = text ?? ""
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    [HttpPost]
    public IActionResult BulkProcess([FromBody] BulkProcessRequest request)
    {
        try
        {
            if (request?.FileNames == null || !request.FileNames.Any())
            {
                return BadRequest(new { success = false, error = "File names are required" });
            }

            var results = new List<BulkProcessResult>();

            foreach (var fileName in request.FileNames)
            {
                var fullPath = Path.Combine(uploadsPath, fileName);
                if (System.IO.File.Exists(fullPath))
                {
                    var confidenceResult = _imageTextService.ExtractTextWithConfidence(fullPath);
                    results.Add(new BulkProcessResult
                    {
                        FileName = fileName,
                        Text = confidenceResult.Text,
                        Confidence = confidenceResult.Confidence,
                        Success = !string.IsNullOrWhiteSpace(confidenceResult.Text)
                    });
                }
            }

            return Json(new
            {
                success = true,
                results = results
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    // Helper methods
    private void SaveExtractedText(string fileName, string text)
    {
        var cleanedText = Regex.Replace(text, @"\s+", " ").Trim();

        if (!string.IsNullOrWhiteSpace(cleanedText) && Regex.IsMatch(cleanedText, "[a-zA-Z0-9]"))
        {
            var textFilePath = Path.Combine(uploadsPath, fileName);
            System.IO.File.WriteAllText(textFilePath, cleanedText);
        }
    }

    private string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };
    }

    private string EscapePdfString(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("\\", "\\\\")
                  .Replace("(", "\\(")
                  .Replace(")", "\\)")
                  .Replace("\r", "")
                  .Replace("\n", "\\Tj\nT* (");
    }
}

// Request models
public class SaveTextRequest
{
    public string Text { get; set; }
}

public class ExtractTextRequest
{
    public string ImagePath { get; set; }
    public string Language { get; set; } = "eng";
}

public class ProcessRequest
{
    public string ImagePath { get; set; }
}

public class BulkProcessRequest
{
    public List<string> FileNames { get; set; }
}

public class BulkProcessResult
{
    public string FileName { get; set; }
    public string Text { get; set; }
    public float Confidence { get; set; }
    public bool Success { get; set; }
}
