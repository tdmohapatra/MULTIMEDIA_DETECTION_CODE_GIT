namespace STAR_MUTIMEDIA.Models
{
    public class OCRModel
    {
    }
    public class TextWithConfidence
    {
        public string Text { get; set; }
        public float Confidence { get; set; }
    }

    public class OcrComparisonResult
    {
        public string OriginalText { get; set; }
        public string ProcessedText { get; set; }
        public float OriginalConfidence { get; set; }
        public float ProcessedConfidence { get; set; }
    }

    public class CompressionResult
    {
        public string Text { get; set; }
        public string CompressedImagePath { get; set; }
        public string TextFilePath { get; set; }
    }

    public class SaveResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; }
    }
}
