using System.Collections.Generic;
using STAR_MUTIMEDIA.Models;

namespace ImgToText.Services
{
    public interface IImageTextService
    {
        TextWithConfidence ExtractTextWithConfidence(string imagePath);
        string ExtractTextFromImageWithPreprocessing(string imagePath);
        string ExtractTextFromImageTDMWithPreprocessing(string imagePath);
        string ExtractTextFromBlurredImage(string imagePath);
        string ExtractTextWithLanguage(string imagePath, string language);
        bool IsImageReadable(string imagePath);
        List<string> GetAvailableLanguages();
        OcrComparisonResult CompareOcrResults(string imagePath);
        CompressionResult ProcessImageWithCompression(string imagePath);
        string PreprocessAndSaveImage(string imagePath);
        SaveResult SaveTextAsDocx(string text);
        SaveResult SaveTextAsTxt(string text);
    }

   
}