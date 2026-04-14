using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Threading.Tasks;
using ImgToText.Services;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using OpenCvSharp;

namespace ImgToTextApp.Controllers
{
    public class ImgToTextController : Controller
    {
        private readonly IImageTextService _imageTextService;
        private readonly string uploadsPath;
        private const long MaxUploadSizeBytes = 10 * 1024 * 1024; // 10 MB
        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif"
        };

        public ImgToTextController(IImageTextService imageTextService)
        {
            _imageTextService = imageTextService;
            uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            Directory.CreateDirectory(uploadsPath);
        }

        public IActionResult ImageToTextScreen()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file, string cameraFile, string ocrPreset = "balanced")
        {
            string extractedText = string.Empty;
            float confidence = 0.0f;
            string processedImagePath = string.Empty;
            string analysisImagePath = string.Empty;
            var totalTimer = Stopwatch.StartNew();
            double extractionMs = 0;
            var normalizedPreset = NormalizeOcrPreset(ocrPreset);

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
                    analysisImagePath = processedImagePath;
                    await System.IO.File.WriteAllBytesAsync(processedImagePath, imageBytes);

                    // Extract text with confidence
                    var extractionTimer = Stopwatch.StartNew();
                    var result = ExecuteOcrByPreset(processedImagePath, normalizedPreset);
                    extractionTimer.Stop();
                    extractionMs = extractionTimer.Elapsed.TotalMilliseconds;
                    extractedText = result.Text;
                    confidence = result.Confidence;

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
                    if (!ValidateIncomingFile(file, out var validationError))
                    {
                        return BadRequest(new { error = validationError });
                    }

                    var safeBaseName = SanitizeFileName(Path.GetFileNameWithoutExtension(file.FileName));
                    var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                    var fileName = $"{safeBaseName}_{DateTime.Now:yyyyMMdd_HHmmss}{fileExtension}";
                    var filePath = Path.Combine(uploadsPath, fileName);
                    analysisImagePath = filePath;

                    // Save the uploaded file
                    await using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // Extract text with confidence
                    var extractionTimer = Stopwatch.StartNew();
                    var result = ExecuteOcrByPreset(filePath, normalizedPreset);
                    extractionTimer.Stop();
                    extractionMs = extractionTimer.Elapsed.TotalMilliseconds;
                    extractedText = result.Text;
                    confidence = result.Confidence;

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
                    return ApiError("No image data provided", 400);
                }

                totalTimer.Stop();
                return Json(new
                {
                    success = !string.IsNullOrWhiteSpace(extractedText),
                    extractedText = extractedText ?? "",
                    confidence = confidence,
                    processedImagePath = processedImagePath,
                    ocrPreset = normalizedPreset,
                    ocrStageTimings = new
                    {
                        extractionMs = Math.Round(extractionMs, 2),
                        totalMs = Math.Round(totalTimer.Elapsed.TotalMilliseconds, 2)
                    },
                    ocrDiagnostics = BuildOcrDiagnostics(
                        analysisImagePath,
                        extractedText,
                        confidence,
                        $"normal:{normalizedPreset}",
                        !string.IsNullOrWhiteSpace(extractedText),
                        new OcrStageTimings
                        {
                            ExtractionMs = Math.Round(extractionMs, 2),
                            TotalMs = Math.Round(totalTimer.Elapsed.TotalMilliseconds, 2)
                        }),
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
        public async Task<IActionResult> UploadEnhanced(IFormFile file, string cameraFile, string ocrPreset = "balanced")
        {
            try
            {
                string extractedText = string.Empty;
                string compressedImagePath = string.Empty;
                float confidence = 0.0f;
                string analysisImagePath = string.Empty;
                var totalTimer = Stopwatch.StartNew();
                double compressionMs = 0;
                double extractionMs = 0;
                var normalizedPreset = NormalizeOcrPreset(ocrPreset);

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
                        var compressionTimer = Stopwatch.StartNew();
                        var result = _imageTextService.ProcessImageWithCompression(tempImagePath);
                        compressionTimer.Stop();
                        compressionMs = compressionTimer.Elapsed.TotalMilliseconds;
                        extractedText = result.Text;
                        compressedImagePath = result.CompressedImagePath;
                        analysisImagePath = compressedImagePath;

                        // Get confidence for enhanced processing
                        var extractionTimer = Stopwatch.StartNew();
                        var confidenceResult = ExecuteOcrByPreset(compressedImagePath, normalizedPreset);
                        extractionTimer.Stop();
                        extractionMs = extractionTimer.Elapsed.TotalMilliseconds;
                        confidence = confidenceResult.Confidence;
                    }
                    catch (NotImplementedException)
                    {
                        // Fallback to regular processing
                        var processedPath = _imageTextService.PreprocessAndSaveImage(tempImagePath);
                        extractedText = _imageTextService.ExtractTextFromImageTDMWithPreprocessing(processedPath);
                        compressedImagePath = processedPath;
                        analysisImagePath = processedPath;

                        var extractionTimer = Stopwatch.StartNew();
                        var confidenceResult = ExecuteOcrByPreset(processedPath, normalizedPreset);
                        extractionTimer.Stop();
                        extractionMs = extractionTimer.Elapsed.TotalMilliseconds;
                        confidence = confidenceResult.Confidence;
                    }

                    // Clean up temp file
                    System.IO.File.Delete(tempImagePath);
                }
                else if (file != null && file.Length > 0)
                {
                    if (!ValidateIncomingFile(file, out var validationError))
                    {
                        return BadRequest(new { success = false, error = validationError });
                    }

                    // Handle uploaded file
                    var safeBaseName = SanitizeFileName(Path.GetFileNameWithoutExtension(file.FileName));
                    var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                    var safeFileName = $"{safeBaseName}_{DateTime.Now:yyyyMMdd_HHmmss}{fileExtension}";
                    var filePath = Path.Combine(uploadsPath, safeFileName);
                    await using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    try
                    {
                        var compressionTimer = Stopwatch.StartNew();
                        var result = _imageTextService.ProcessImageWithCompression(filePath);
                        compressionTimer.Stop();
                        compressionMs = compressionTimer.Elapsed.TotalMilliseconds;
                        extractedText = result.Text;
                        compressedImagePath = result.CompressedImagePath;
                        analysisImagePath = compressedImagePath;

                        var extractionTimer = Stopwatch.StartNew();
                        var confidenceResult = ExecuteOcrByPreset(compressedImagePath, normalizedPreset);
                        extractionTimer.Stop();
                        extractionMs = extractionTimer.Elapsed.TotalMilliseconds;
                        confidence = confidenceResult.Confidence;
                    }
                    catch (NotImplementedException)
                    {
                        // Fallback to regular processing
                        var processedPath = _imageTextService.PreprocessAndSaveImage(filePath);
                        extractedText = _imageTextService.ExtractTextFromImageTDMWithPreprocessing(processedPath);
                        compressedImagePath = processedPath;
                        analysisImagePath = processedPath;

                        var extractionTimer = Stopwatch.StartNew();
                        var confidenceResult = ExecuteOcrByPreset(processedPath, normalizedPreset);
                        extractionTimer.Stop();
                        extractionMs = extractionTimer.Elapsed.TotalMilliseconds;
                        confidence = confidenceResult.Confidence;
                    }

                    // Clean up original file
                    System.IO.File.Delete(filePath);
                }
                else
                {
                    return ApiError("No image data provided", 400);
                }

                totalTimer.Stop();
                return Json(new
                {
                    success = !string.IsNullOrWhiteSpace(extractedText),
                    extractedText = extractedText ?? "",
                    confidence = confidence,
                    compressedImagePath = compressedImagePath != null ? Path.GetFileName(compressedImagePath) : null,
                    ocrPreset = normalizedPreset,
                    ocrStageTimings = new
                    {
                        compressionMs = Math.Round(compressionMs, 2),
                        extractionMs = Math.Round(extractionMs, 2),
                        totalMs = Math.Round(totalTimer.Elapsed.TotalMilliseconds, 2)
                    },
                    ocrDiagnostics = BuildOcrDiagnostics(
                        analysisImagePath,
                        extractedText,
                        confidence,
                        $"enhanced:{normalizedPreset}",
                        !string.IsNullOrWhiteSpace(extractedText),
                        new OcrStageTimings
                        {
                            CompressionMs = Math.Round(compressionMs, 2),
                            ExtractionMs = Math.Round(extractionMs, 2),
                            TotalMs = Math.Round(totalTimer.Elapsed.TotalMilliseconds, 2)
                        }),
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
                    return ApiError("Image path is required", 400);
                }

                if (!TryBuildSafeUploadPath(request.ImagePath, out var fullPath))
                {
                    return ApiError("Invalid image path", 400);
                }
                if (!System.IO.File.Exists(fullPath))
                {
                    return ApiError("Image file not found", 404);
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
                    return ApiError("Image path is required", 400);
                }

                if (!TryBuildSafeUploadPath(request.ImagePath, out var fullPath))
                {
                    return ApiError("Invalid image path", 400);
                }
                if (!System.IO.File.Exists(fullPath))
                {
                    return ApiError("Image file not found", 404);
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

                if (!TryBuildSafeUploadPath(request.ImagePath, out var fullPath))
                {
                    return BadRequest(new { success = false, error = "Invalid image path" });
                }
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
                    return ApiError("File name is required", 400);
                }

                if (!TryBuildSafeUploadPath(fileName, out var filePath))
                {
                    return ApiError("Invalid file name", 400);
                }
                if (!System.IO.File.Exists(filePath))
                {
                    return ApiError("File not found", 404);
                }

                var fileBytes = System.IO.File.ReadAllBytes(filePath);
                var contentType = GetContentType(fileName);

                return File(fileBytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                return ApiError($"Error downloading file: {ex.Message}", 500);
            }
        }

        [HttpPost]
        public IActionResult ExtractTextWithLanguage([FromBody] ExtractTextRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request?.ImagePath))
                {
                    return ApiError("Image path is required", 400);
                }

                if (!TryBuildSafeUploadPath(request.ImagePath, out var fullPath))
                {
                    return ApiError("Invalid image path", 400);
                }
                if (!System.IO.File.Exists(fullPath))
                {
                    return ApiError("Image file not found", 404);
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
                    return ApiError("File names are required", 400);
                }

                var results = new List<BulkProcessResult>();

                foreach (var fileName in request.FileNames)
                {
                    if (!TryBuildSafeUploadPath(fileName, out var fullPath))
                    {
                        continue;
                    }
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

        private bool ValidateIncomingFile(IFormFile file, out string error)
        {
            error = string.Empty;
            if (file == null || file.Length == 0)
            {
                error = "File is empty";
                return false;
            }

            if (file.Length > MaxUploadSizeBytes)
            {
                error = $"File size exceeds {MaxUploadSizeBytes / (1024 * 1024)}MB limit";
                return false;
            }

            if (!AllowedImageExtensions.Contains(Path.GetExtension(file.FileName)))
            {
                error = "Unsupported file type";
                return false;
            }

            return true;
        }

        private bool TryBuildSafeUploadPath(string inputFileName, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(inputFileName))
            {
                return false;
            }

            var safeName = Path.GetFileName(inputFileName);
            if (!string.Equals(safeName, inputFileName, StringComparison.Ordinal))
            {
                return false;
            }

            var candidate = Path.GetFullPath(Path.Combine(uploadsPath, safeName));
            var uploadsRoot = Path.GetFullPath(uploadsPath);
            if (!candidate.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }

        private string SanitizeFileName(string fileNameWithoutExtension)
        {
            if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            {
                return "upload";
            }

            var sanitized = Regex.Replace(fileNameWithoutExtension, @"[^a-zA-Z0-9_\-]", "_");
            return sanitized.Length > 80 ? sanitized[..80] : sanitized;
        }

        private ObjectResult ApiError(string message, int statusCode)
        {
            return StatusCode(statusCode, new
            {
                success = false,
                error = message
            });
        }

        private OcrDiagnostics BuildOcrDiagnostics(
            string imagePath,
            string extractedText,
            float confidence,
            string mode,
            bool success,
            OcrStageTimings? stageTimings = null)
        {
            var normalizedText = extractedText ?? string.Empty;
            var textLength = normalizedText.Length;
            var tokenCount = Regex.Matches(normalizedText, @"\b[\w\d]+\b").Count;
            var alnumChars = normalizedText.Count(char.IsLetterOrDigit);
            var alnumRatio = textLength == 0 ? 0 : (double)alnumChars / textLength;

            var diagnostics = new OcrDiagnostics
            {
                Mode = mode,
                Success = success,
                Confidence = confidence,
                TextLength = textLength,
                TokenCount = tokenCount,
                AlphaNumericRatio = Math.Round(alnumRatio, 4),
                QualityHint = confidence >= 80 ? "high" : confidence >= 55 ? "medium" : "low",
                StageTimings = stageTimings ?? new OcrStageTimings(),
                SignatureInsights = DetectSignatureCandidates(imagePath)
            };

            return diagnostics;
        }

        private string NormalizeOcrPreset(string? preset)
        {
            var value = (preset ?? "balanced").Trim().ToLowerInvariant();
            return value switch
            {
                "strict" => "strict",
                "sensitive" => "sensitive",
                _ => "balanced"
            };
        }

        private (string Text, float Confidence) ExecuteOcrByPreset(string imagePath, string preset)
        {
            if (preset == "sensitive")
            {
                var preprocessedText = _imageTextService.ExtractTextFromImageWithPreprocessing(imagePath);
                if (!string.IsNullOrWhiteSpace(preprocessedText))
                {
                    return (preprocessedText, 72f);
                }
            }

            var confidenceResult = _imageTextService.ExtractTextWithConfidence(imagePath);
            var text = confidenceResult.Text ?? string.Empty;
            var confidence = confidenceResult.Confidence;
            if (preset == "strict")
            {
                return (confidence >= 75f ? text : string.Empty, confidence);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = _imageTextService.ExtractTextFromImageWithPreprocessing(imagePath);
                if (!string.IsNullOrWhiteSpace(text) && confidence < 60f)
                {
                    confidence = 60f;
                }
            }

            return (text, confidence);
        }

        private SignatureInsights DetectSignatureCandidates(string imagePath)
        {
            var insights = new SignatureInsights();
            if (string.IsNullOrWhiteSpace(imagePath) || !System.IO.File.Exists(imagePath))
                return insights;

            try
            {
                using var mat = Cv2.ImRead(imagePath, ImreadModes.Grayscale);
                if (mat.Empty())
                    return insights;

                var h = mat.Rows;
                var w = mat.Cols;
                if (h < 24 || w < 24)
                    return insights;

                var roiY = (int)(h * 0.58);
                roiY = Math.Max(0, Math.Min(roiY, h - 1));
                using var roi = new Mat(mat, new Rect(0, roiY, w, h - roiY));
                using var blur = new Mat();
                Cv2.GaussianBlur(roi, blur, new Size(3, 3), 0);
                using var bin = new Mat();
                Cv2.Threshold(blur, bin, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

                Cv2.FindContours(bin, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                var minArea = (roi.Rows * roi.Cols) * 0.004;
                foreach (var c in contours)
                {
                    var rect = Cv2.BoundingRect(c);
                    if (rect.Width * rect.Height < minArea)
                        continue;
                    var aspect = rect.Height == 0 ? 0 : (double)rect.Width / rect.Height;
                    if (aspect < 2.0)
                        continue;

                    insights.Candidates.Add(new SignatureCandidate
                    {
                        X = rect.X,
                        Y = rect.Y + roiY,
                        Width = rect.Width,
                        Height = rect.Height,
                        AreaRatio = Math.Round((rect.Width * rect.Height) / (double)(w * h), 5)
                    });
                }

                insights.Candidates = insights.Candidates
                    .OrderByDescending(c => c.AreaRatio)
                    .Take(3)
                    .ToList();

                insights.HasSignatureLikeRegion = insights.Candidates.Count > 0;
                insights.Score = insights.HasSignatureLikeRegion
                    ? Math.Min(0.95, 0.45 + insights.Candidates.Sum(c => c.AreaRatio) * 3.0)
                    : 0.0;

                if (insights.HasSignatureLikeRegion)
                {
                    var avgArea = insights.Candidates.Average(c => c.AreaRatio);
                    var inkDensity = Cv2.CountNonZero(bin) / (double)(bin.Rows * bin.Cols);
                    insights.InkDensity = Math.Round(inkDensity, 4);
                    insights.TamperScore = Math.Round(Math.Clamp((inkDensity * 1.8) + (avgArea * 4.5), 0.0, 1.0), 4);
                    insights.PotentialTamper = insights.TamperScore >= 0.68;
                    insights.TamperSignals = new List<string>();
                    if (inkDensity > 0.32) insights.TamperSignals.Add("high_ink_density");
                    if (avgArea > 0.08) insights.TamperSignals.Add("oversized_signature_region");
                    if (insights.Candidates.Count >= 3) insights.TamperSignals.Add("multiple_signature_regions");
                }
            }
            catch
            {
                // Diagnostics should never break OCR response.
            }

            return insights;
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

    public class OcrDiagnostics
    {
        public string Mode { get; set; } = "normal";
        public bool Success { get; set; }
        public float Confidence { get; set; }
        public int TextLength { get; set; }
        public int TokenCount { get; set; }
        public double AlphaNumericRatio { get; set; }
        public string QualityHint { get; set; } = "unknown";
        public OcrStageTimings StageTimings { get; set; } = new OcrStageTimings();
        public SignatureInsights SignatureInsights { get; set; } = new SignatureInsights();
    }

    public class OcrStageTimings
    {
        public double CompressionMs { get; set; }
        public double ExtractionMs { get; set; }
        public double TotalMs { get; set; }
    }

    public class SignatureInsights
    {
        public bool HasSignatureLikeRegion { get; set; }
        public double Score { get; set; }
        public bool PotentialTamper { get; set; }
        public double TamperScore { get; set; }
        public double InkDensity { get; set; }
        public List<string> TamperSignals { get; set; } = new List<string>();
        public List<SignatureCandidate> Candidates { get; set; } = new List<SignatureCandidate>();
    }

    public class SignatureCandidate
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double AreaRatio { get; set; }
    }
}