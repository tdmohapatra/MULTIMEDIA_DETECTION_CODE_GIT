using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using OpenCvSharp;

namespace STAR_MUTIMEDIA.Models
{
    public class DetectionStats
    {
        public int FacesDetected { get; set; }
        public int EyesDetected { get; set; }
        public int HandsDetected { get; set; }
        public int TotalFramesProcessed { get; set; }
        public double CurrentMovementLevel { get; set; }
        public bool MovementDetected { get; set; }
        public bool TextDetected { get; set; }
        public bool ExpressionsDetected { get; set; }
        public bool GesturesDetected { get; set; }

        public double CurrentFPS { get; set; }
        public double TargetFPS { get; set; }
        public double ActualProcessingFPS { get; set; }
        public DateTime LastUpdate { get; set; }

        public int FramesSinceLastCalculation { get; set; }
        public DateTime LastFPSCalculation { get; set; } = DateTime.UtcNow;

        // Camera movement analysis
        public CameraMovementType CameraMovement { get; set; }
        public double CameraStability { get; set; } = 100.0; // 0-100%
        public List<MovementVector> RecentMovements { get; set; } = new List<MovementVector>();

        // Performance metrics
        public double AverageProcessingTimeMs { get; set; }
        public double MemoryUsageMB { get; set; }
        public bool IsSystemOptimal { get; set; } = true;

        public DetectionStats Clone()
        {
            return new DetectionStats
            {
                FacesDetected = this.FacesDetected,
                EyesDetected = this.EyesDetected,
                HandsDetected = this.HandsDetected,
                TotalFramesProcessed = this.TotalFramesProcessed,
                CurrentMovementLevel = this.CurrentMovementLevel,
                MovementDetected = this.MovementDetected,
                TextDetected = this.TextDetected,
                ExpressionsDetected = this.ExpressionsDetected,
                GesturesDetected = this.GesturesDetected,
                CurrentFPS = this.CurrentFPS,
                TargetFPS = this.TargetFPS,
                ActualProcessingFPS = this.ActualProcessingFPS,
                LastUpdate = this.LastUpdate,
                FramesSinceLastCalculation = this.FramesSinceLastCalculation,
                LastFPSCalculation = this.LastFPSCalculation,
                CameraMovement = this.CameraMovement,
                CameraStability = this.CameraStability,
                RecentMovements = new List<MovementVector>(this.RecentMovements),
                AverageProcessingTimeMs = this.AverageProcessingTimeMs,
                MemoryUsageMB = this.MemoryUsageMB,
                IsSystemOptimal = this.IsSystemOptimal
            };
        }
    }

    public class MovementVector
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Magnitude { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public MovementDirection Direction { get; set; }
    }

    public enum MovementDirection
    {
        None,
        Left,
        Right,
        Up,
        Down,
        Diagonal
    }

    public enum CameraMovementType
    {
        Stable,
        SlowPan,
        FastPan,
        SlowTilt,
        FastTilt,
        Zooming,
        Shaking,
        Rotating,
        Unknown
    }

    public class DetectionSettings
    {
        // Frame rate control
        public double TargetFPS { get; set; } = 30.0;
        public bool EnableFrameRateControl { get; set; } = true;

        // Monitoring options
        public bool EnableFaceDetection { get; set; } = true;
        public bool EnableEyeDetection { get; set; } = true;
        public bool EnableHandDetection { get; set; } = true;
        public bool EnableMovementDetection { get; set; } = true;
        public bool EnableTextDetection { get; set; } = true;
        public bool EnableExpressionDetection { get; set; } = true;
        public bool EnableGestureDetection { get; set; } = true;
        public bool EnableCameraMovementAnalysis { get; set; } = true;

        // Thresholds
        public double MovementThreshold { get; set; } = 10.0;
        public double CameraStabilityThreshold { get; set; } = 80.0;
        public double FaceConfidenceThreshold { get; set; } = 0.7;
        public double HandConfidenceThreshold { get; set; } = 0.6;

        // Performance settings
        public int FrameSkipCount { get; set; } = 0;
        public bool EnableAdaptiveProcessing { get; set; } = true;
        public int MaxProcessingTimeMs { get; set; } = 100;
        public bool EnableHardwareAcceleration { get; set; } = true;

        // Alert settings
        public bool EnableAlerts { get; set; } = true;
        public bool EnableLogging { get; set; } = true;
    }

    public class FrameRateInfo
    {
        public double TargetFPS { get; set; }
        public double ActualFPS { get; set; }
        public double ProcessingTimeMs { get; set; }
        public bool IsOptimal { get; set; }
        public string Recommendation { get; set; }
        public double FrameDropRate { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    public class CameraMovementAnalysis
    {
        public CameraMovementType MovementType { get; set; }
        public double StabilityScore { get; set; }
        public double HorizontalMovement { get; set; }
        public double VerticalMovement { get; set; }
        public double ZoomLevel { get; set; }
        public string Status { get; set; }
        public string Recommendation { get; set; }
        public List<MovementVector> RecentVectors { get; set; } = new List<MovementVector>();
        public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;
    }

    public class DetectionResult
    {
        public string ImageData { get; set; }  // Add this back

        public DetectionStats Stats { get; set; } = new DetectionStats();
        public List<DetectionNotification> Notifications { get; set; } = new List<DetectionNotification>();
        public List<SystemLog> Logs { get; set; } = new List<SystemLog>();
        public DetectionData Detections { get; set; } = new DetectionData();
        public string CapturedText { get; set; }
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
        public DateTime ProcessingTime { get; set; } = DateTime.UtcNow;
    }

    public class DetectionData
    {
        public List<FaceDetection> Faces { get; set; } = new List<FaceDetection>();
        public List<EyeDetection> Eyes { get; set; } = new List<EyeDetection>();
        public List<HandDetection> Hands { get; set; } = new List<HandDetection>();
        public List<TextDetection> TextRegions { get; set; } = new List<TextDetection>();
        public List<ObjectDetection> Objects { get; set; } = new List<ObjectDetection>();
    }

    public class FaceDetection
    {
        public BoundingBox BBox { get; set; } = new BoundingBox();
        public double Confidence { get; set; }
        public FaceExpression Expression { get; set; }
        public List<LandmarkPoint> Landmarks { get; set; } = new List<LandmarkPoint>();
        public int TrackId { get; set; }
    }

    public class EyeDetection
    {
        public BoundingBox BBox { get; set; } = new BoundingBox();
        public double Confidence { get; set; }
        public string State { get; set; } // Open, Closed, Blinking
        public GazeDirection Gaze { get; set; }
        public int TrackId { get; set; }
    }

    public class HandDetection
    {
        public BoundingBox BBox { get; set; } = new BoundingBox();
        public double Confidence { get; set; }
        public string Handedness { get; set; } // Left, Right
        public HandGesture Gesture { get; set; }
        public List<LandmarkPoint> Landmarks { get; set; } = new List<LandmarkPoint>();
        public int TrackId { get; set; }
    }

    public class TextDetection
    {
        public BoundingBox BBox { get; set; } = new BoundingBox();
        public string Content { get; set; }
        public double Confidence { get; set; }
        public string Language { get; set; } = "en";
    }

    public class ObjectDetection
    {
        public string Type { get; set; }
        public BoundingBox BBox { get; set; } = new BoundingBox();
        public double Confidence { get; set; }
        public string AdditionalInfo { get; set; }
    }

    public class FrameData
    {
        public string ImageData { get; set; }
        public string SessionId { get; set; }
        public long Timestamp { get; set; }
        public int FrameNumber { get; set; }
        public string ProcessingMode { get; set; } = "standard"; // standard, enhanced, fast
    }

    public class EnhancedFrameData : FrameData

    {

        public DetectionSettings Settings { get; set; } = new DetectionSettings();
        public bool RequireEnhancedProcessing { get; set; } = true;
    }

    public class EnhancedDetectionResult : DetectionResult
    {
        public List<FaceExpression> FaceExpressions { get; set; } = new List<FaceExpression>();
        public List<HandGesture> HandGestures { get; set; } = new List<HandGesture>();
        public List<EyeMovement> EyeMovements { get; set; } = new List<EyeMovement>();
        public VitalMetrics VitalMetrics { get; set; } = new VitalMetrics();
        public EmotionAnalysis EmotionAnalysis { get; set; } = new EmotionAnalysis();
        public BehaviorAnalysis BehaviorAnalysis { get; set; } = new BehaviorAnalysis();
    }

    public class FaceExpression
    {
        public BoundingBox BBox { get; set; } = new BoundingBox();
        public Dictionary<string, double> Emotions { get; set; } = new Dictionary<string, double>();
        public string DominantEmotion { get; set; }
        public double Confidence { get; set; }
        public int FaceId { get; set; }
    }

    public class HandGesture
    {
        public BoundingBox BBox { get; set; } = new BoundingBox();
        public string Type { get; set; }
        public string Handedness { get; set; }
        public double Confidence { get; set; }
        public string Meaning { get; set; }
        public List<LandmarkPoint> KeyPoints { get; set; } = new List<LandmarkPoint>();
    }

    public class EyeMovement
    {
        public BoundingBox BBox { get; set; } = new BoundingBox();
        public string Direction { get; set; }
        public bool IsBlinking { get; set; }
        public double Confidence { get; set; }
        public double GazeConfidence { get; set; }
        public Point2f GazePoint { get; set; }
    }

    public class VitalMetrics
    {
        public int? HeartRate { get; set; }
        public string StressLevel { get; set; } = "Unknown";
        public double AttentionScore { get; set; }
        public string EngagementLevel { get; set; } = "Unknown";
        public double BlinkRate { get; set; }
        public double HeadPoseConfidence { get; set; }
    }

    public class EmotionAnalysis
    {
        public Dictionary<string, double> OverallEmotions { get; set; } = new Dictionary<string, double>();
        public string DominantEmotion { get; set; }
        public double EmotionalIntensity { get; set; }
        public List<EmotionTimeline> Timeline { get; set; } = new List<EmotionTimeline>();
    }

    public class EmotionTimeline
    {
        public DateTime Timestamp { get; set; }
        public string Emotion { get; set; }
        public double Intensity { get; set; }
    }

    public class BehaviorAnalysis
    {
        public string Posture { get; set; }
        public double EngagementLevel { get; set; }
        public List<string> DetectedBehaviors { get; set; } = new List<string>();
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    }

    public class BoundingBox
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public double Area => Width * Height;
        public Point2d Center => new Point2d(X + Width / 2, Y + Height / 2);
    }

    public class LandmarkPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Confidence { get; set; }
        public string Type { get; set; }
    }

    public enum GazeDirection
    {
        Center,
        Left,
        Right,
        Up,
        Down,
        Unknown
    }

    public class DetectionNotification
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Type { get; set; } // FaceDetected, MovementDetected, TextCaptured, etc.
        public string Message { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Severity { get; set; } = "Info"; // Info, Warning, Alert, Critical
        public string Category { get; set; } // Detection, System, Performance
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SystemLog
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Message { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Level { get; set; } = "Info"; // Info, Warning, Error, Debug
        public string Component { get; set; } // Detection, Camera, Processing, System
        public string SessionId { get; set; }
        public Dictionary<string, object> Context { get; set; } = new Dictionary<string, object>();
    }

    public class MonitoringConfiguration
    {
        public string SessionId { get; set; }
        public List<MonitoringOption> EnabledOptions { get; set; } = new List<MonitoringOption>();
        public FrameRateControl FrameRateControl { get; set; } = new FrameRateControl();
        public CameraMovementConfig CameraMovementConfig { get; set; } = new CameraMovementConfig();
        public AlertConfiguration AlertConfig { get; set; } = new AlertConfiguration();
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }

    public class MonitoringOption
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public bool IsEnabled { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }

    public class FrameRateControl
    {
        public double TargetFPS { get; set; } = 30.0;
        public double MinFPS { get; set; } = 5.0;
        public double MaxFPS { get; set; } = 60.0;
        public bool AdaptiveMode { get; set; } = true;
        public int FrameSkip { get; set; } = 0;
        public double QualityFactor { get; set; } = 0.8; // 0-1, affects processing quality
    }

    public class CameraMovementConfig
    {
        public bool EnableAnalysis { get; set; } = true;
        public double StabilityThreshold { get; set; } = 80.0;
        public bool ShowStatusBar { get; set; } = true;
        public bool EnableAlerts { get; set; } = true;
        public int AnalysisInterval { get; set; } = 10; // frames
    }

    public class AlertConfiguration
    {
        public bool EnableFaceDetectionAlerts { get; set; } = true;
        public bool EnableMovementAlerts { get; set; } = true;
        public bool EnableTextDetectionAlerts { get; set; } = true;
        public bool EnableSystemAlerts { get; set; } = true;
        public double AlertCooldownSeconds { get; set; } = 30.0;
        public List<string> AlertRecipients { get; set; } = new List<string>();
    }

    public class SessionAnalytics
    {
        public string SessionId { get; set; }
        public DetectionStats Stats { get; set; } = new DetectionStats();
        public TimeSpan SessionDuration { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        public List<DetectionNotification> RecentNotifications { get; set; } = new List<DetectionNotification>();
        public PerformanceMetrics Performance { get; set; } = new PerformanceMetrics();
        public List<string> DetectedObjects { get; set; } = new List<string>();
    }

    public class PerformanceMetrics
    {
        public double AverageFPS { get; set; }
        public double PeakFPS { get; set; }
        public double AverageProcessingTime { get; set; }
        public int TotalFramesProcessed { get; set; }
        public int TotalFacesDetected { get; set; }
        public int TotalHandsDetected { get; set; }
        public int TotalTextCaptures { get; set; }
        public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    }

    // Request/Response DTOs for API
    public class ProcessFrameRequest
    {
        public string ImageData { get; set; }
        public string SessionId { get; set; }
        public string ProcessingMode { get; set; } = "standard";
        public Dictionary<string, object> Options { get; set; } = new Dictionary<string, object>();
    }

    public class UpdateSettingsRequest
    {
        public DetectionSettings Settings { get; set; } = new DetectionSettings();
        public bool ApplyToAllSessions { get; set; } = false;
    }

    public class HealthCheckResponse
    {
        public string Status { get; set; } = "Healthy";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int ActiveSessions { get; set; }
        public SystemInfo SystemInfo { get; set; } = new SystemInfo();
        public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();
    }

    public class SystemInfo
    {
        public string Version { get; set; } = "1.0.0";
        public string Framework { get; set; } = ".NET";
        public DateTime StartupTime { get; set; } = DateTime.UtcNow;
        public double MemoryUsageMB { get; set; }
        public bool IsDevelopment { get; set; } = true;
    }

    // Internal classes for service use only
    internal class SessionData
    {
        public string SessionId { get; set; }
        public DetectionStats Stats { get; set; } = new DetectionStats();
        public DetectionSettings Settings { get; set; } = new DetectionSettings();
        public MonitoringConfiguration MonitoringConfig { get; set; } = new MonitoringConfiguration();

        [JsonIgnore]
        public Mat PreviousFrame { get; set; }

        [JsonIgnore]
        public Mat PreviousGrayFrame { get; set; }
            //public string LastProcessedImageData { get; set; }
        public bool LastFaceState { get; set; }
        public bool LastMovementState { get; set; }
        public string LastDetectedText { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastFrameTime { get; set; } = DateTime.UtcNow;
        public Queue<double> ProcessingTimes { get; set; } = new Queue<double>();
        public int FramesToSkip { get; set; } = 0;

        // Camera movement tracking
        public List<MovementVector> MovementHistory { get; set; } = new List<MovementVector>();

        [JsonIgnore]
        public Point2f[] PreviousFeatures { get; set; }

        // Performance tracking
        public DateTime LastCleanup { get; set; } = DateTime.UtcNow;
        public int FramesSinceLastCleanup { get; set; } = 0;
        public bool IsActive { get; set; } = true;

        public void UpdateFrameTime()
        {
            LastFrameTime = DateTime.UtcNow;
            FramesSinceLastCleanup++;
        }

        public void AddProcessingTime(double milliseconds)
        {
            ProcessingTimes.Enqueue(milliseconds);
            if (ProcessingTimes.Count > 100) // Keep last 100 processing times
            {
                ProcessingTimes.Dequeue();
            }
        }

        public double GetAverageProcessingTime()
        {
            if (ProcessingTimes.Count == 0) return 0;

            double sum = 0;
            foreach (var time in ProcessingTimes)
            {
                sum += time;
            }
            return sum / ProcessingTimes.Count;
        }
    }
}




//using System;
//using System.Collections.Generic;
//using OpenCvSharp;

//namespace STAR_MUTIMEDIA.Models
//{
//    public class DetectionStats
//    {
//        public int FacesDetected { get; set; }
//        public int EyesDetected { get; set; }
//        public int HandsDetected { get; set; }
//        public int TotalFramesProcessed { get; set; }
//        public double CurrentMovementLevel { get; set; }
//        public bool MovementDetected { get; set; }
//        public bool TextDetected { get; set; }
//        public bool ExpressionsDetected { get; set; }
//        public bool GesturesDetected { get; set; }

//        public double CurrentFPS { get; set; }
//        public double TargetFPS { get; set; }
//        public double ActualProcessingFPS { get; set; }
//        public DateTime LastUpdate { get; set; }

//        public int FramesSinceLastCalculation { get; set; }
//        public DateTime LastFPSCalculation { get; set; } = DateTime.MinValue;

//        // Camera movement analysis
//        public CameraMovementType CameraMovement { get; set; }
//        public double CameraStability { get; set; } // 0-100%
//        public List<MovementVector> RecentMovements { get; set; } = new List<MovementVector>();

//        public DetectionStats Clone()
//        {
//            return new DetectionStats
//            {
//                TotalFramesProcessed = this.TotalFramesProcessed,
//                FacesDetected = this.FacesDetected,
//                EyesDetected = this.EyesDetected,
//                HandsDetected = this.HandsDetected,
//                MovementDetected = this.MovementDetected,
//                TextDetected = this.TextDetected,
//                CurrentMovementLevel = this.CurrentMovementLevel,
//                CurrentFPS = this.CurrentFPS,
//                TargetFPS = this.TargetFPS,
//                ActualProcessingFPS = this.ActualProcessingFPS,
//                LastUpdate = this.LastUpdate,
//                FramesSinceLastCalculation = this.FramesSinceLastCalculation,
//                LastFPSCalculation = this.LastFPSCalculation,
//                CameraMovement = this.CameraMovement,
//                CameraStability = this.CameraStability,
//                RecentMovements = new List<MovementVector>(this.RecentMovements)
//            };
//        }
//    }

//    public class MovementVector
//    {
//        public double X { get; set; }
//        public double Y { get; set; }
//        public double Magnitude { get; set; }
//        public DateTime Timestamp { get; set; }
//    }

//    public enum CameraMovementType
//    {
//        Stable,
//        SlowPan,
//        FastPan,
//        SlowTilt,
//        FastTilt,
//        Zooming,
//        Shaking,
//        Rotating
//    }

//    public class DetectionSettings
//    {
//        // Frame rate control
//        public double TargetFPS { get; set; } = 30.0;
//        public bool EnableFrameRateControl { get; set; } = true;

//        // Monitoring options
//        public bool EnableFaceDetection { get; set; } = true;
//        public bool EnableEyeDetection { get; set; } = true;
//        public bool EnableHandDetection { get; set; } = true;
//        public bool EnableMovementDetection { get; set; } = true;
//        public bool EnableTextDetection { get; set; } = true;
//        public bool EnableCameraMovementAnalysis { get; set; } = true;

//        // Thresholds
//        public double MovementThreshold { get; set; } = 10.0;
//        public double CameraStabilityThreshold { get; set; } = 80.0;

//        // Performance settings
//        public int FrameSkipCount { get; set; } = 0;
//        public bool EnableAdaptiveProcessing { get; set; } = true;
//    }

//    public class FrameRateInfo
//    {
//        public double TargetFPS { get; set; }
//        public double ActualFPS { get; set; }
//        public double ProcessingTimeMs { get; set; }
//        public bool IsOptimal { get; set; }
//        public string Recommendation { get; set; }
//    }

//    public class CameraMovementAnalysis
//    {
//        public CameraMovementType MovementType { get; set; }
//        public double StabilityScore { get; set; }
//        public double HorizontalMovement { get; set; }
//        public double VerticalMovement { get; set; }
//        public double ZoomLevel { get; set; }
//        public string Status { get; set; }
//        public string Recommendation { get; set; }
//    }

//    public class DetectionResult
//    {
//        public string ImageData { get; set; }
//        public DetectionStats Stats { get; set; }
//        public List<string> Notifications { get; set; } = new List<string>();
//        public List<string> Logs { get; set; } = new List<string>();
//        public List<DetectedObject> DetectedObjects { get; set; } = new List<DetectedObject>();
//        public string CapturedText { get; set; }
//    }

//    public class DetectedObject
//    {
//        public string Type { get; set; }
//        public int X { get; set; }
//        public int Y { get; set; }
//        public int Width { get; set; }
//        public int Height { get; set; }
//        public double Confidence { get; set; }
//        public string AdditionalInfo { get; set; }
//    }
//    public class FrameData
//    {
//        public string ImageData { get; set; }
//        public string SessionId { get; set; }
//        public long Timestamp { get; set; }
//        public int FrameNumber { get; set; }
//    }

//    public class EnhancedFrameData : FrameData
//    {
//        public long Timestamp { get; set; }
//        public int FrameNumber { get; set; }
//        public DetectionSettings Settings { get; set; }
//    }

//    public class EnhancedDetectionResult : DetectionResult
//    {
//        public List<FaceExpression> FaceExpressions { get; set; } = new List<FaceExpression>();
//        public List<HandGesture> HandGestures { get; set; } = new List<HandGesture>();
//        public List<EyeMovement> EyeMovements { get; set; } = new List<EyeMovement>();
//        public VitalMetrics VitalMetrics { get; set; } = new VitalMetrics();
//        public List<DetectionNotification> Notifications { get; set; } = new List<DetectionNotification>();
//    }

//    public class FaceExpression
//    {
//        public BoundingBox BBox { get; set; }
//        public Dictionary<string, double> Emotions { get; set; }
//        public string DominantEmotion { get; set; }
//        public double Confidence { get; set; }
//    }

//    public class HandGesture
//    {
//        public BoundingBox BBox { get; set; }
//        public string Type { get; set; }
//        public string Handedness { get; set; }
//        public double Confidence { get; set; }
//        public string Meaning { get; set; }
//    }

//    public class EyeMovement
//    {
//        public BoundingBox BBox { get; set; }
//        public string Direction { get; set; }
//        public bool IsBlinking { get; set; }
//        public double Confidence { get; set; }
//    }

//    public class VitalMetrics
//    {
//        public int? HeartRate { get; set; }
//        public string StressLevel { get; set; }
//        public double AttentionScore { get; set; }
//        public string EngagementLevel { get; set; }
//    }

//    public class BoundingBox
//    {
//        public double X { get; set; }
//        public double Y { get; set; }
//        public double Width { get; set; }
//        public double Height { get; set; }
//    }

//    public class DetectionNotification
//    {
//        public string Type { get; set; }
//        public string Message { get; set; }
//        public DateTime Timestamp { get; set; }
//        public string Severity { get; set; } // Info, Warning, Alert
//    }

//    public class SystemLog
//    {
//        public string Message { get; set; }
//        public DateTime Timestamp { get; set; }
//        public string Level { get; set; } // Info, Warning, Error
//    }

//    public class MonitoringConfiguration
//    {
//        public string SessionId { get; set; }
//        public List<MonitoringOption> EnabledOptions { get; set; } = new List<MonitoringOption>();
//        public FrameRateControl FrameRateControl { get; set; } = new FrameRateControl();
//        public CameraMovementConfig CameraMovementConfig { get; set; } = new CameraMovementConfig();
//    }

//    public class MonitoringOption
//    {
//        public string Name { get; set; }
//        public string DisplayName { get; set; }
//        public bool IsEnabled { get; set; }
//        public string Category { get; set; }
//        public string Description { get; set; }
//    }

//    public class FrameRateControl
//    {
//        public double TargetFPS { get; set; } = 30.0;
//        public double MinFPS { get; set; } = 5.0;
//        public double MaxFPS { get; set; } = 60.0;
//        public bool AdaptiveMode { get; set; } = true;
//        public int FrameSkip { get; set; } = 0;
//    }

//    public class CameraMovementConfig
//    {
//        public bool EnableAnalysis { get; set; } = true;
//        public double StabilityThreshold { get; set; } = 80.0;
//        public bool ShowStatusBar { get; set; } = true;
//        public bool EnableAlerts { get; set; } = true;
//    }

//    public class SessionAnalytics
//    {
//        public string SessionId { get; set; }
//        public DetectionStats Stats { get; set; }
//        public TimeSpan SessionDuration { get; set; }
//        public DateTime StartedAt { get; set; }
//        public DateTime LastActivity { get; set; }
//        public List<string> RecentNotifications { get; set; } = new List<string>();
//    }

//    // Internal classes for service use only
//    internal class SessionData
//    {
//        public string SessionId { get; set; }
//        public DetectionStats Stats { get; set; } = new DetectionStats();
//        public DetectionSettings Settings { get; set; } = new DetectionSettings();
//        public MonitoringConfiguration MonitoringConfig { get; set; } = new MonitoringConfiguration();
//        public Mat PreviousFrame { get; set; }
//        public Mat PreviousGrayFrame { get; set; }
//        public bool LastFaceState { get; set; }
//        public bool LastMovementState { get; set; }
//        public string LastDetectedText { get; set; }
//        public DateTime CreatedAt { get; set; } = DateTime.Now;
//        public DateTime LastFrameTime { get; set; }
//        public Queue<double> ProcessingTimes { get; set; } = new Queue<double>();
//        public int FramesToSkip { get; set; } = 0;

//        // Camera movement tracking
//        public List<MovementVector> MovementHistory { get; set; } = new List<MovementVector>();
//        public Point2f[] PreviousFeatures { get; set; }
//    }
//}