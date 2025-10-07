using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Tesseract;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using System.Runtime.InteropServices;
using STAR_MUTIMEDIA.Models;

namespace ImgToText.Services
{
    public class ImageTextService : IImageTextService, IDisposable
    {
        private readonly string _tessDataPath;
        private bool _disposed = false;

        public ImageTextService(string tessDataPath)
        {
            _tessDataPath = tessDataPath ?? throw new ArgumentNullException(nameof(tessDataPath));

            // Validate tessdata directory exists
            if (!Directory.Exists(_tessDataPath))
            {
                throw new DirectoryNotFoundException($"Tessdata directory not found: {_tessDataPath}");
            }

            // Set environment variable for Tesseract
            Environment.SetEnvironmentVariable("TESSDATA_PREFIX", _tessDataPath);

            // Verify that required language files exist
            var engPath = Path.Combine(_tessDataPath, "eng.traineddata");
            if (!File.Exists(engPath))
            {
                throw new FileNotFoundException($"Required language file not found: {engPath}");
            }
        }

        public TextWithConfidence ExtractTextWithConfidence(string imagePath)
        {
            ValidateImagePath(imagePath);

            try
            {
                using (var engine = CreateTesseractEngine("eng", EngineMode.Default))
                using (var img = Pix.LoadFromFile(imagePath))
                using (var page = engine.Process(img))
                {
                    var text = CleanText(page.GetText());
                    var confidence = page.GetMeanConfidence();
                    return new TextWithConfidence
                    {
                        Text = text ?? "No text detected",
                        Confidence = confidence
                    };
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error during confidence-based OCR processing: {ex.Message}", ex);
            }
        }

        public string ExtractTextFromImageWithPreprocessing(string imagePath)
        {
            ValidateImagePath(imagePath);
            string processedPath = null;

            try
            {
                processedPath = PreprocessAndSaveImage(imagePath);

                using (var engine = CreateTesseractEngine("eng", EngineMode.Default))
                {
                    ConfigureEngineForText(engine);

                    using (var img = Pix.LoadFromFile(processedPath))
                    using (var page = engine.Process(img, PageSegMode.Auto))
                    {
                        var result = CleanText(page.GetText());
                        return result ?? "No text detected";
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error during preprocessed OCR: {ex.Message}", ex);
            }
            finally
            {
                SafeDeleteFile(processedPath);
            }
        }

        public string ExtractTextFromImageTDMWithPreprocessing(string imagePath)
        {
            ValidateImagePath(imagePath);
            string processedPath = null;

            try
            {
                processedPath = PreprocessAndSaveImage(imagePath);

                using (var engine = CreateTesseractEngine("eng", EngineMode.Default))
                {
                    ConfigureEngineForText(engine);

                    using (var img = Pix.LoadFromFile(processedPath))
                    using (var page = engine.Process(img, PageSegMode.SingleBlock))
                    {
                        var result = CleanText(page.GetText());
                        return result ?? "No text detected";
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error during TDM preprocessed OCR: {ex.Message}", ex);
            }
            finally
            {
                SafeDeleteFile(processedPath);
            }
        }

        public string ExtractTextFromBlurredImage(string imagePath)
        {
            ValidateImagePath(imagePath);
            string processedPath = null;

            try
            {
                processedPath = PreprocessAndSaveImage(imagePath);

                using (var engine = CreateTesseractEngine("eng", EngineMode.TesseractAndLstm))
                {
                    ConfigureEngineForBlurredText(engine);

                    using (var img = Pix.LoadFromFile(processedPath))
                    {
                        string result = null;

                        // Try multiple page segmentation modes
                        var segmentationModes = new[] {
                            PageSegMode.Auto,
                            PageSegMode.SingleBlock,
                            PageSegMode.SingleLine,
                            PageSegMode.SingleWord
                        };

                        foreach (var mode in segmentationModes)
                        {
                            try
                            {
                                using (var page = engine.Process(img, mode))
                                {
                                    var currentResult = CleanText(page.GetText());
                                    if (!string.IsNullOrWhiteSpace(currentResult) && currentResult.Length >= 3)
                                    {
                                        result = currentResult;
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                // Continue with next mode
                            }
                        }

                        return result ?? "No text detected";
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error extracting text from blurred image: {ex.Message}", ex);
            }
            finally
            {
                SafeDeleteFile(processedPath);
            }
        }

        public string ExtractTextWithLanguage(string imagePath, string language)
        {
            ValidateImagePath(imagePath);
            ValidateLanguage(language);

            try
            {
                using (var engine = CreateTesseractEngine(language, EngineMode.Default))
                using (var img = Pix.LoadFromFile(imagePath))
                using (var page = engine.Process(img))
                {
                    return CleanText(page.GetText()) ?? "No text detected";
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error during OCR processing with language '{language}': {ex.Message}", ex);
            }
        }

        public bool IsImageReadable(string imagePath)
        {
            try
            {
                ValidateImagePath(imagePath);
                var result = ExtractTextWithConfidence(imagePath);
                return !string.IsNullOrWhiteSpace(result.Text) &&
                       result.Text != "No text detected" &&
                       result.Confidence >= 50.0f;
            }
            catch
            {
                return false;
            }
        }

        public List<string> GetAvailableLanguages()
        {
            var languages = new List<string>();
            try
            {
                var tessdataDir = new DirectoryInfo(_tessDataPath);
                if (tessdataDir.Exists)
                {
                    var languageFiles = tessdataDir.GetFiles("*.traineddata");
                    foreach (var file in languageFiles)
                    {
                        var language = Path.GetFileNameWithoutExtension(file.Name);
                        if (language != "osd" && language != "equ")
                        {
                            languages.Add(language);
                        }
                    }
                }

                if (!languages.Any())
                {
                    languages.Add("eng");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting languages: {ex.Message}");
                languages.Add("eng");
            }
            return languages;
        }

        public OcrComparisonResult CompareOcrResults(string imagePath)
        {
            ValidateImagePath(imagePath);
            string processedPath = null;

            try
            {
                var originalResult = ExtractTextWithConfidence(imagePath);
                processedPath = PreprocessAndSaveImage(imagePath);
                var processedResult = ExtractTextWithConfidence(processedPath);

                return new OcrComparisonResult
                {
                    OriginalText = originalResult.Text,
                    ProcessedText = processedResult.Text,
                    OriginalConfidence = originalResult.Confidence,
                    ProcessedConfidence = processedResult.Confidence
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Error comparing OCR results: {ex.Message}", ex);
            }
            finally
            {
                SafeDeleteFile(processedPath);
            }
        }

        public CompressionResult ProcessImageWithCompression(string imagePath)
        {
            ValidateImagePath(imagePath);

            string compressedImagePath = null;
            string textFilePath = null;

            try
            {
                // Preprocess image
                var processedBytes = PreprocessImageForTextExtraction(File.ReadAllBytes(imagePath));

                // Save compressed image
                compressedImagePath = Path.Combine(
                    Path.GetDirectoryName(imagePath),
                    Path.GetFileNameWithoutExtension(imagePath) + "_compressed.jpg");

                // Further compress the image
                var compressedBytes = CompressImage(processedBytes, 75L); // Increased quality for better OCR
                File.WriteAllBytes(compressedImagePath, compressedBytes);

                // Extract text using enhanced method for blurred images
                var text = ExtractTextFromBlurredImage(compressedImagePath);

                // If text extraction fails with blurred method, try regular method
                if (string.IsNullOrWhiteSpace(text) || text == "No text detected")
                {
                    text = ExtractTextFromImageTDMWithPreprocessing(compressedImagePath);
                }

                // Save text
                textFilePath = Path.Combine(
                    Path.GetDirectoryName(imagePath),
                    Path.GetFileNameWithoutExtension(imagePath) + "_extracted.txt");

                File.WriteAllText(textFilePath, text ?? "No text could be extracted");

                return new CompressionResult
                {
                    Text = text ?? "No text could be extracted",
                    CompressedImagePath = compressedImagePath,
                    TextFilePath = textFilePath
                };
            }
            catch (Exception ex)
            {
                // Clean up on error
                SafeDeleteFile(compressedImagePath);
                SafeDeleteFile(textFilePath);
                throw new Exception($"Error processing image with compression: {ex.Message}", ex);
            }
        }

        public string PreprocessAndSaveImage(string imagePath)
        {
            ValidateImagePath(imagePath);

            try
            {
                var imageBytes = File.ReadAllBytes(imagePath);
                var processedBytes = PreprocessImageForTextExtraction(imageBytes);

                var processedPath = Path.Combine(
                    Path.GetDirectoryName(imagePath),
                    Path.GetFileNameWithoutExtension(imagePath) + "_processed.png");

                File.WriteAllBytes(processedPath, processedBytes);
                return processedPath;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error preprocessing image: {ex.Message}", ex);
            }
        }

        public SaveResult SaveTextAsDocx(string text)
        {
            var cleanedText = CleanText(text);
            if (cleanedText == null)
            {
                return new SaveResult { Success = false, FilePath = "No valid text to save." };
            }

            try
            {
                var fileName = $"extracted_text_{DateTime.Now:yyyyMMdd_HHmmss}.docx";
                var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
                Directory.CreateDirectory(uploadsDir);
                var filePath = Path.Combine(uploadsDir, fileName);

                File.WriteAllText(filePath, cleanedText);

                return new SaveResult { Success = true, FilePath = filePath };
            }
            catch (Exception ex)
            {
                return new SaveResult { Success = false, FilePath = $"Error creating file: {ex.Message}" };
            }
        }

        public SaveResult SaveTextAsTxt(string text)
        {
            var cleanedText = CleanText(text);
            if (cleanedText == null)
            {
                return new SaveResult { Success = false, FilePath = null };
            }

            try
            {
                var fileName = $"extracted_text_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
                Directory.CreateDirectory(uploadsDir);
                var filePath = Path.Combine(uploadsDir, fileName);

                File.WriteAllText(filePath, cleanedText);

                return new SaveResult { Success = true, FilePath = filePath };
            }
            catch (Exception ex)
            {
                return new SaveResult { Success = false, FilePath = $"Error creating file: {ex.Message}" };
            }
        }

        #region Private Helper Methods

        private TesseractEngine CreateTesseractEngine(string language, EngineMode engineMode)
        {
            try
            {
                return new TesseractEngine(_tessDataPath, language, engineMode);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create Tesseract engine for language '{language}': {ex.Message}", ex);
            }
        }

        private void ConfigureEngineForText(TesseractEngine engine)
        {
            try
            {
                engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?()-+*/=@#$%&");
                engine.SetVariable("preserve_interword_spaces", "1");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Could not configure engine: {ex.Message}");
            }
        }

        private void ConfigureEngineForBlurredText(TesseractEngine engine)
        {
            try
            {
                engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?()-+*/=@#$%&");
                engine.SetVariable("preserve_interword_spaces", "1");
                engine.SetVariable("textord_min_linesize", "2.0");
                engine.SetVariable("textord_heavy_nr", "1");
                engine.SetVariable("edges_max_children_per_outline", "40");
                engine.SetVariable("textord_min_blobs_in_row", "2");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Could not configure blurred text engine: {ex.Message}");
            }
        }

        private string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var cleanedText = text
                .Replace("\n\n\n", "\n\n")
                .Replace("\n\n", "\n")
                .Replace("   ", " ")
                .Replace("  ", " ")
                .Trim();

            if (string.IsNullOrWhiteSpace(cleanedText) ||
                !cleanedText.Any(char.IsLetter) ||
                cleanedText.Length < 2)
            {
                return null;
            }

            return cleanedText;
        }

        private byte[] PreprocessImageForTextExtraction(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("Image bytes cannot be null or empty", nameof(imageBytes));

            using (var ms = new MemoryStream(imageBytes))
            using (var originalImage = Image.FromStream(ms))
            using (var bitmap = new Bitmap(originalImage.Width, originalImage.Height, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(originalImage, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                }

                // Use the simplified preprocessing pipeline
                using (var processedBitmap = ApplySimplifiedPreprocessingPipeline(bitmap))
                using (var outputMs = new MemoryStream())
                {
                    processedBitmap.Save(outputMs, ImageFormat.Png);
                    return outputMs.ToArray();
                }
            }
        }

        private byte[] CompressImage(byte[] imageBytes, long quality = 75L)
        {
            try
            {
                using (var ms = new MemoryStream(imageBytes))
                using (var image = Image.FromStream(ms))
                {
                    var encoder = GetEncoder(ImageFormat.Jpeg);
                    if (encoder == null)
                        return imageBytes;

                    var encoderParameters = new EncoderParameters(1);
                    encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);

                    using (var outputMs = new MemoryStream())
                    {
                        image.Save(outputMs, encoder, encoderParameters);
                        return outputMs.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Compression failed, using original: {ex.Message}");
                return imageBytes;
            }
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                    return codec;
            }
            return null;
        }

        #region Image Preprocessing Methods

        private Bitmap ApplySimplifiedPreprocessingPipeline(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            List<Bitmap> disposableBitmaps = new List<Bitmap>();
            Bitmap currentBitmap = bitmap;

            try
            {
                // Simplified pipeline - focus on most effective operations
                currentBitmap = SafeResizeImageForOCR(currentBitmap);
                disposableBitmaps.Add(currentBitmap);

                currentBitmap = SafeConvertToGrayscale(currentBitmap);
                disposableBitmaps.Add(currentBitmap);

                currentBitmap = SafeEnhanceContrast(currentBitmap, 2.0f);
                disposableBitmaps.Add(currentBitmap);

                currentBitmap = SafeApplyNoiseReduction(currentBitmap);
                disposableBitmaps.Add(currentBitmap);

                currentBitmap = SafeApplyAdaptiveThreshold(currentBitmap);
                disposableBitmaps.Add(currentBitmap);

                // Remove final bitmap from disposable list
                disposableBitmaps.Remove(currentBitmap);
                return currentBitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Image preprocessing failed: {ex.Message}");

                // Clean up all bitmaps
                foreach (var bmp in disposableBitmaps)
                {
                    SafeDispose(bmp);
                }
                SafeDispose(currentBitmap);

                // Return original as fallback
                return new Bitmap(bitmap);
            }
        }

        private Bitmap SafeResizeImageForOCR(Bitmap bitmap)
        {
            const int optimalWidth = 2400;
            const int optimalHeight = 1800;

            if (bitmap.Width >= optimalWidth && bitmap.Height >= optimalHeight)
                return new Bitmap(bitmap); // Return copy

            int newWidth = Math.Min(bitmap.Width * 2, optimalWidth);
            int newHeight = Math.Min(bitmap.Height * 2, optimalHeight);

            var resized = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(bitmap, 0, 0, newWidth, newHeight);
            }

            return resized;
        }

        private Bitmap SafeConvertToGrayscale(Bitmap bitmap)
        {
            var grayscale = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);

            // Simple and safe grayscale conversion
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    int grayValue = (int)(pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114);
                    grayscale.SetPixel(x, y, Color.FromArgb(grayValue, grayValue, grayValue));
                }
            }

            return grayscale;
        }

        private Bitmap SafeEnhanceContrast(Bitmap bitmap, float contrastFactor)
        {
            var contrastAdjusted = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    int r = (int)Math.Max(0, Math.Min(255, (pixel.R - 128) * contrastFactor + 128));
                    int g = (int)Math.Max(0, Math.Min(255, (pixel.G - 128) * contrastFactor + 128));
                    int b = (int)Math.Max(0, Math.Min(255, (pixel.B - 128) * contrastFactor + 128));

                    contrastAdjusted.SetPixel(x, y, Color.FromArgb(r, g, b));
                }
            }

            return contrastAdjusted;
        }

        private Bitmap SafeApplyNoiseReduction(Bitmap bitmap)
        {
            var denoised = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);

            // Simple median filter for noise reduction
            for (int y = 1; y < bitmap.Height - 1; y++)
            {
                for (int x = 1; x < bitmap.Width - 1; x++)
                {
                    // Collect 3x3 neighborhood values
                    var redValues = new List<byte>();
                    var greenValues = new List<byte>();
                    var blueValues = new List<byte>();

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            Color pixel = bitmap.GetPixel(x + dx, y + dy);
                            redValues.Add(pixel.R);
                            greenValues.Add(pixel.G);
                            blueValues.Add(pixel.B);
                        }
                    }

                    // Get median values
                    redValues.Sort();
                    greenValues.Sort();
                    blueValues.Sort();

                    byte medianRed = redValues[4];
                    byte medianGreen = greenValues[4];
                    byte medianBlue = blueValues[4];

                    denoised.SetPixel(x, y, Color.FromArgb(medianRed, medianGreen, medianBlue));
                }
            }

            // Copy border pixels
            CopyBordersSafe(bitmap, denoised, 1);
            return denoised;
        }

        private Bitmap SafeApplyAdaptiveThreshold(Bitmap bitmap)
        {
            var thresholded = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
            int blockSize = 19; // Increased for better adaptation
            double C = 5; // Reduced constant for better thresholding

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int sum = 0;
                    int count = 0;

                    for (int dy = -blockSize / 2; dy <= blockSize / 2; dy++)
                    {
                        for (int dx = -blockSize / 2; dx <= blockSize / 2; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;

                            if (nx >= 0 && nx < bitmap.Width && ny >= 0 && ny < bitmap.Height)
                            {
                                Color pixel = bitmap.GetPixel(nx, ny);
                                sum += pixel.R; // Use red channel for grayscale
                                count++;
                            }
                        }
                    }

                    double mean = (double)sum / count;
                    Color currentPixel = bitmap.GetPixel(x, y);
                    byte newValue = currentPixel.R > (mean - C) ? (byte)255 : (byte)0;

                    thresholded.SetPixel(x, y, Color.FromArgb(newValue, newValue, newValue));
                }
            }

            return thresholded;
        }

        private void CopyBordersSafe(Bitmap source, Bitmap destination, int borderWidth)
        {
            // Copy top border
            for (int y = 0; y < borderWidth; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    destination.SetPixel(x, y, source.GetPixel(x, y));
                }
            }

            // Copy bottom border
            for (int y = source.Height - borderWidth; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    destination.SetPixel(x, y, source.GetPixel(x, y));
                }
            }

            // Copy left and right borders
            for (int y = borderWidth; y < source.Height - borderWidth; y++)
            {
                for (int x = 0; x < borderWidth; x++)
                {
                    destination.SetPixel(x, y, source.GetPixel(x, y));
                    destination.SetPixel(source.Width - 1 - x, y, source.GetPixel(source.Width - 1 - x, y));
                }
            }
        }

        #endregion

        #region Validation Methods

        private void ValidateImagePath(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                throw new ArgumentException("Image path cannot be null or empty", nameof(imagePath));

            if (!File.Exists(imagePath))
                throw new FileNotFoundException($"Image file not found: {imagePath}");

            // Basic file type validation
            var extension = Path.GetExtension(imagePath)?.ToLower();
            var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif", ".gif" };

            if (!validExtensions.Contains(extension))
                throw new ArgumentException($"Unsupported image format: {extension}");

            // Verify file is not empty
            var fileInfo = new FileInfo(imagePath);
            if (fileInfo.Length == 0)
                throw new ArgumentException("Image file is empty");
        }

        private void ValidateLanguage(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
                throw new ArgumentException("Language cannot be null or empty", nameof(language));

            var languagePath = Path.Combine(_tessDataPath, $"{language}.traineddata");
            if (!File.Exists(languagePath))
                throw new FileNotFoundException($"Language file not found: {languagePath}");
        }

        private void SafeDeleteFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Could not delete file {filePath}: {ex.Message}");
            }
        }

        private void SafeDispose(Bitmap bitmap)
        {
            if (bitmap != null)
            {
                try
                {
                    bitmap.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Could not dispose bitmap: {ex.Message}");
                }
            }
        }

        #endregion

        #endregion

        #region IDisposable Implementation

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources here if any
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ImageTextService()
        {
            Dispose(false);
        }

        #endregion
    }
}