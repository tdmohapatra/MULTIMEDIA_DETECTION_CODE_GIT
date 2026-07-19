using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using OpenCvSharp.Extensions;
using STAR_MUTIMEDIA.Models;
using STAR_MUTIMEDIA.Services.Plugins;
using Tesseract;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace STAR_MUTIMEDIA.Services
{
    public partial class RealTimeDetectionService : IRealTimeDetectionService, IDisposable
    {
        private readonly string _tessDataPath;
        private readonly ConcurrentDictionary<string, SessionData> _sessions;
        private readonly object _lockObject = new object();
        private bool _disposed = false;
        private readonly string _sessionProfilesPath;
        private readonly List<MonitoringOption> _availableMonitoringOptions;
        private Net? _yoloV8Net;
        private Net? _yunetFaceNet;
        private Net? _emotionFerPlusNet;
        private readonly object _yoloLoadLock = new object();
        private readonly object _yunetLoadLock = new object();
        private readonly object _emotionLoadLock = new object();
        private bool _yoloLoadFailed;
        private bool _yunetLoadFailed;
        private bool _emotionLoadFailed;
        private volatile bool _yoloUsesOpenCl;
        private volatile bool _yunetUsesOpenCl;
        private volatile bool _emotionUsesOpenCl;
        private readonly List<IDetectionPlugin> _detectionPlugins;
        private readonly string _inferenceProvider;

        public RealTimeDetectionService(string tessDataPath)
        {
            _tessDataPath = tessDataPath ?? throw new ArgumentNullException(nameof(tessDataPath));
            _sessions = new ConcurrentDictionary<string, SessionData>();
            _sessionProfilesPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "session-profiles");
            Directory.CreateDirectory(_sessionProfilesPath);

            // Set Tesseract environment variable
            if (!string.IsNullOrEmpty(_tessDataPath))
            {
                Environment.SetEnvironmentVariable("TESSDATA_PREFIX", _tessDataPath);
            }

            // Initialize cascades with fallback
            var cascadesPath = GetCascadesPath();
            _detectionPlugins = new List<IDetectionPlugin>
            {
                new LowLightDetectionPlugin(),
                new NoHandDetectionPlugin(),
                new MultipleFacesDetectionPlugin()
            };

            // Initialize available monitoring options
            _availableMonitoringOptions = InitializeMonitoringOptions();
            WarmupModels();
            _inferenceProvider = DetermineInferenceProvider();
        }

        // Reflects the backend actually applied to the loaded CvDnn nets after
        // TryAccelerateWithOpenCl's warmup-forward validation (see SceneAnalysis partial).
        // OpenCV's DNN module has no DirectML backend, so this no longer probes ORT/DML.
        private string DetermineInferenceProvider()
        {
            if (_yoloUsesOpenCl || _yunetUsesOpenCl || _emotionUsesOpenCl)
                return "opencl";
            return "cpu";
        }

        private void WarmupModels()
        {
            try
            {
                _ = GetOrLoadYoloV8();
                _ = GetOrLoadYuNetFace();
                _ = GetOrLoadEmotionFerPlus();
            }
            catch
            {
                // Warmup failures are non-fatal; runtime will continue with available models.
            }
        }

        private string GetCascadesPath()
        {
            var possiblePaths = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "cascades"),
                Path.Combine(Directory.GetCurrentDirectory(), "cascades"),
                Path.Combine(AppContext.BaseDirectory, "cascades"),
                Path.Combine(Environment.CurrentDirectory, "cascades")
            };

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path))
                {
                    Debug.WriteLine($"Using cascades path: {path}");
                    return path;
                }
            }

            // Create default cascades directory if none exists
            var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "cascades");
            Directory.CreateDirectory(defaultPath);
            Debug.WriteLine($"Created default cascades path: {defaultPath}");
            return defaultPath;
        }

        private CascadeClassifier LoadCascadeClassifier(string cascadePath)
        {
            try
            {
                if (File.Exists(cascadePath))
                {
                    var classifier = new CascadeClassifier(cascadePath);
                    if (!classifier.Empty())
                    {
                        Debug.WriteLine($"Loaded cascade: {Path.GetFileName(cascadePath)}");
                        return classifier;
                    }
                    classifier.Dispose();
                }

                Debug.WriteLine($"Cascade file not found or invalid: {cascadePath}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading cascade {cascadePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolve classifier/model file from preferred cascades path, then common fallback folders.
        /// This allows users to drop cascades into Uploads/models as well.
        /// </summary>
        private string ResolveCascadePath(string preferredCascadesPath, string fileName)
        {
            var candidates = new[]
            {
                Path.Combine(preferredCascadesPath ?? string.Empty, fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "cascades", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "models", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "cascades", fileName),
                Path.Combine(AppContext.BaseDirectory, "cascades", fileName),
                Path.Combine(AppContext.BaseDirectory, "Uploads", "cascades", fileName),
                Path.Combine(AppContext.BaseDirectory, "Uploads", "models", fileName)
            };

            return candidates.FirstOrDefault(File.Exists) ?? Path.Combine(preferredCascadesPath ?? string.Empty, fileName);
        }

        private List<MonitoringOption> InitializeMonitoringOptions()
        {
            return new List<MonitoringOption>
            {
                new MonitoringOption {
                    Name = "FaceDetection",
                    DisplayName = "Face Detection",
                    IsEnabled = true,
                    Category = "People",
                    Description = "Detect human faces in the frame"
                },
                new MonitoringOption {
                    Name = "EyeDetection",
                    DisplayName = "Eye Detection",
                    IsEnabled = true,
                    Category = "People",
                    Description = "Detect eyes within detected faces"
                },
                new MonitoringOption {
                    Name = "HandDetection",
                    DisplayName = "Hand Detection",
                    IsEnabled = true,
                    Category = "People",
                    Description = "Detect hands and gestures"
                },
                new MonitoringOption {
                    Name = "MovementDetection",
                    DisplayName = "Movement Detection",
                    IsEnabled = true,
                    Category = "Motion",
                    Description = "Detect general movement in the scene"
                },
                new MonitoringOption {
                    Name = "TextDetection",
                    DisplayName = "Text Detection",
                    IsEnabled = true,
                    Category = "Objects",
                    Description = "Extract text using OCR"
                },
                new MonitoringOption {
                    Name = "CameraMovementAnalysis",
                    DisplayName = "Camera Movement Analysis",
                    IsEnabled = true,
                    Category = "Camera",
                    Description = "Analyze camera stability and movement patterns"
                }
            };
        }

        public async Task<DetectionResult> ProcessFrameAsync(FrameData frameData)
        {
            if (frameData == null)
                throw new ArgumentNullException(nameof(frameData));

            var sessionId = frameData.SessionId;
            if (string.IsNullOrEmpty(sessionId))
            {
                sessionId = Guid.NewGuid().ToString();
                frameData.SessionId = sessionId;
            }

            InitializeSessionIfNotExists(sessionId);

            var session = _sessions[sessionId];
            var result = new DetectionResult();
            var notifications = new List<DetectionNotification>();
            var logs = new List<SystemLog>();
            var stageTimings = new ProcessingStageTimings();
            var totalStopwatch = Stopwatch.StartNew();

            try
            {
                DetectionData detectionData = new DetectionData();

                // Frame rate control - skip frames if needed
                if (ShouldSkipFrame(session))
                {
                    session.FramesToSkip--;
                    result.Stats = session.Stats.Clone();
                    result.BehaviorAnalysis = new BehaviorAnalysis();
                    result.HumanTracking = new HumanTrackingState();
                    result.SceneAnalysis = new SceneAnalysisResult { Pipeline = "disabled" };
                    result.StageTimings = new ProcessingStageTimings
                    {
                        SkippedByRateControl = true,
                        TotalMs = 0
                    };
                    result.DetectorHealth = GetDetectorHealth(sessionId);
                    result.Notifications.Add(new DetectionNotification
                    {
                        Type = "System",
                        Message = "Frame skipped for rate control",
                        Timestamp = DateTime.UtcNow,
                        Severity = "Info"
                    });
                    return await Task.FromResult(result);
                }

                var decodeStopwatch = Stopwatch.StartNew();

                // Convert base64 to image with validation
                if (string.IsNullOrEmpty(frameData.ImageData))
                {
                    throw new ArgumentException("Image data is empty or null");
                }

                var imageDataParts = frameData.ImageData.Split(',');
                var base64Data = imageDataParts.Length > 1 ? imageDataParts[1] : imageDataParts[0];

                if (string.IsNullOrEmpty(base64Data))
                {
                    throw new ArgumentException("Invalid base64 image data");
                }

                var imageBytes = Convert.FromBase64String(base64Data);
                if (imageBytes.Length == 0)
                {
                    throw new ArgumentException("Decoded image data is empty");
                }

                using (var ms = new MemoryStream(imageBytes))
                {
                    if (ms.Length == 0)
                    {
                        throw new ArgumentException("Memory stream is empty");
                    }

                    using (var image = SD.Image.FromStream(ms))
                    using (var bitmap = new SD.Bitmap(image))
                    {
                        // Convert to OpenCV Mat
                        using (var mat = BitmapConverter.ToMat(bitmap))
                        {
                            if (mat.Empty())
                            {
                                throw new InvalidOperationException("Converted OpenCV mat is empty");
                            }

                            decodeStopwatch.Stop();
                            stageTimings.DecodeMs = decodeStopwatch.Elapsed.TotalMilliseconds;

                            session.Stats.AverageBrightness = EstimateSceneBrightness(mat);
                            var detectionStopwatch = Stopwatch.StartNew();
                            var processedMat = ProcessFrame(
                                mat,
                                session,
                                notifications,
                                logs,
                                detectionData,
                                frameData.ProcessingMode ?? "standard",
                                frameData.SceneOptions,
                                frameData.ClientHands);
                            detectionStopwatch.Stop();
                            stageTimings.DetectionMs = detectionStopwatch.Elapsed.TotalMilliseconds;

                            var renderStopwatch = Stopwatch.StartNew();
                            var outputMat = processedMat.Clone();
                            // Convert back to base64 only if processing was successful
                            if (!outputMat.Empty())
                            {
                                using (var processedBitmap = BitmapConverter.ToBitmap(outputMat))
                                using (var outputMs = new MemoryStream())
                                {
                                    processedBitmap.Save(outputMs, SDI.ImageFormat.Jpeg);
                                    result.ImageData = "data:image/jpeg;base64," + Convert.ToBase64String(outputMs.ToArray());
                                }
                            }
                            else
                            {
                                // Use original image if processing failed
                                result.ImageData = frameData.ImageData;
                            }

                            result.Detections = detectionData;
                            renderStopwatch.Stop();
                            stageTimings.RenderMs = renderStopwatch.Elapsed.TotalMilliseconds;
                        }
                    }
                }

                var postAnalysisStopwatch = Stopwatch.StartNew();
                var faceExpressions = AnalyzeFaceExpressions(detectionData.Faces);
                var handGestures = AnalyzeHandGestures(detectionData.Hands);
                handGestures = ApplyGestureTemporalSmoothing(session, handGestures);
                var eyeMovements = AnalyzeEyeMovements(detectionData.Eyes);
                if (session.Settings.EnableConfidenceFusion)
                {
                    ApplyConfidenceFusion(detectionData, faceExpressions, handGestures, eyeMovements);
                }
                var vitalMetrics = EstimateVitalMetrics(detectionData.Faces);
                var monitoringState = BuildMonitoringState(session, faceExpressions, handGestures, eyeMovements);
                RunDetectionPlugins(session, detectionData, notifications, logs);

                session.Stats.ExpressionsDetected = faceExpressions.Count > 0;
                session.Stats.GesturesDetected = handGestures.Count > 0;
                postAnalysisStopwatch.Stop();
                stageTimings.PostAnalysisMs = postAnalysisStopwatch.Elapsed.TotalMilliseconds;
                totalStopwatch.Stop();
                stageTimings.TotalMs = totalStopwatch.Elapsed.TotalMilliseconds;
                UpdateProcessingTime(session, stageTimings.TotalMs);
                session.AddStageTiming(stageTimings);
                UpdateCalibration(session);

                result.Stats = session.Stats.Clone();
                result.Notifications = notifications;
                result.Logs = logs;
                result.CapturedText = session.LastDetectedText;
                result.FaceExpressions = faceExpressions;
                result.HandGestures = handGestures;
                result.EyeMovements = eyeMovements;
                result.VitalMetrics = vitalMetrics;
                result.MonitoringState = monitoringState;
                result.HumanTracking = BuildHumanTrackingState(session, detectionData, session.Stats);
                result.BehaviorAnalysis = BuildBehaviorAnalysis(detectionData, session.Stats, monitoringState);
                AppendIntelligenceEvents(session, monitoringState, detectionData, result.BehaviorAnalysis);
                result.StageTimings = stageTimings;
                result.DetectorHealth = GetDetectorHealth(sessionId);
                if (string.Equals(frameData.ProcessingMode, "scene", StringComparison.OrdinalIgnoreCase))
                {
                    result.SceneAnalysis = session.LastSceneAnalysis ?? new SceneAnalysisResult();
                    result.SceneAnalysis.FrameProcessingMs = stageTimings.TotalMs;
                    result.SceneAnalysis.Summary = BuildSceneEntitySummary(result.SceneAnalysis.Entities);
                    AppendSceneFrameTimeLog(session.SessionId, frameData.FrameNumber, stageTimings.TotalMs, result.SceneAnalysis.Summary, result.SceneAnalysis.Pipeline);
                }
                else
                {
                    result.SceneAnalysis = new SceneAnalysisResult { Pipeline = "disabled" };
                }
                result.Calibration = new CalibrationResult
                {
                    SessionId = session.SessionId,
                    IsCalibrating = session.IsCalibrating,
                    FramesRemaining = session.CalibrationFramesRemaining,
                    TotalFrames = session.CalibrationFramesTotal,
                    BaselineMovement = session.BaselineMovement,
                    StartedAt = session.CalibrationStartedAt,
                    Message = session.IsCalibrating
                        ? $"Calibrating ({session.CalibrationFramesRemaining} frames left)"
                        : $"Calibrated baseline: {session.BaselineMovement:0.0}%"
                };
                result.Success = true;
                session.Stats.LastUpdate = DateTime.UtcNow;

                logs.Add(new SystemLog
                {
                    Message = $"Frame processed successfully in {stageTimings.TotalMs:0.00}ms",
                    Timestamp = DateTime.UtcNow,
                    Level = "Info",
                    Component = "Processing"
                });
            }
            catch (Exception ex)
            {
                totalStopwatch.Stop();
                stageTimings.TotalMs = totalStopwatch.Elapsed.TotalMilliseconds;
                logs.Add(new SystemLog
                {
                    Message = $"Error processing frame: {ex.Message}",
                    Timestamp = DateTime.UtcNow,
                    Level = "Error",
                    Component = "Processing"
                });

                result.Logs = logs;
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.StageTimings = stageTimings;
                result.DetectorHealth = GetDetectorHealth(sessionId);

                // Return a basic result with error information
                result.Stats = session?.Stats?.Clone() ?? new DetectionStats();
            }

            return await Task.FromResult(result);
        }

        public async Task<EnhancedDetectionResult> ProcessEnhancedFrameAsync(EnhancedFrameData frameData)
        {
            if (frameData == null)
                throw new ArgumentNullException(nameof(frameData));

            var basicResult = await ProcessFrameAsync(frameData);
            var enhancedResult = new EnhancedDetectionResult
            {
                ImageData = basicResult.ImageData,
                Stats = basicResult.Stats,
                Notifications = basicResult.Notifications,
                Logs = basicResult.Logs,
                Detections = basicResult.Detections,
                CapturedText = basicResult.CapturedText,
                Success = basicResult.Success,
                ErrorMessage = basicResult.ErrorMessage,
                FaceExpressions = basicResult.FaceExpressions ?? new List<FaceExpression>(),
                HandGestures = basicResult.HandGestures ?? new List<HandGesture>(),
                EyeMovements = basicResult.EyeMovements ?? new List<EyeMovement>(),
                VitalMetrics = basicResult.VitalMetrics ?? new VitalMetrics(),
                MonitoringState = basicResult.MonitoringState,
                BehaviorAnalysis = basicResult.BehaviorAnalysis ?? new BehaviorAnalysis(),
                HumanTracking = basicResult.HumanTracking ?? new HumanTrackingState(),
                SceneAnalysis = basicResult.SceneAnalysis ?? new SceneAnalysisResult(),
                EmotionAnalysis = new EmotionAnalysis()
            };

            if (basicResult.Success && basicResult.Detections?.Faces?.Count > 0)
            {
                enhancedResult.FaceExpressions = AnalyzeFaceExpressions(basicResult.Detections.Faces);
                enhancedResult.VitalMetrics = EstimateVitalMetrics(basicResult.Detections.Faces);
            }

            return enhancedResult;
        }

        private List<FaceExpression> AnalyzeFaceExpressions(List<FaceDetection> faces)
        {
            var expressions = new List<FaceExpression>();

            foreach (var face in faces)
            {
                var expressionSource = face.Expression;
                var dominantEmotion = expressionSource?.DominantEmotion ?? "Neutral";
                var confidence = expressionSource?.Confidence ?? face.Confidence;
                var emotions = expressionSource?.Emotions ?? new Dictionary<string, double>
                {
                    { "happy", 0.15 },
                    { "sad", 0.10 },
                    { "angry", 0.05 },
                    { "surprised", 0.10 },
                    { "neutral", 0.60 }
                };

                var expression = new FaceExpression
                {
                    BBox = face.BBox,
                    Confidence = confidence,
                    FaceId = face.TrackId,
                    DominantEmotion = dominantEmotion,
                    Emotions = emotions
                };
                expressions.Add(expression);
            }

            return expressions;
        }

        private VitalMetrics EstimateVitalMetrics(List<FaceDetection> faces)
        {
            return new VitalMetrics
            {
                HeartRate = 72 + new Random().Next(-10, 10),
                StressLevel = "Low",
                AttentionScore = 85.0,
                EngagementLevel = "High",
                BlinkRate = 15.0,
                HeadPoseConfidence = 0.8
            };
        }

        private List<HandGesture> AnalyzeHandGestures(List<HandDetection> hands)
        {
            var gestures = new List<HandGesture>();

            foreach (var hand in hands)
            {
                gestures.Add(hand.Gesture ?? InferGestureWithModel(hand));
            }

            return gestures;
        }

        private List<HandGesture> ApplyGestureTemporalSmoothing(SessionData session, List<HandGesture> gestures)
        {
            if (gestures == null || gestures.Count == 0)
            {
                return gestures ?? new List<HandGesture>();
            }

            var top = gestures
                .OrderByDescending(g => g.Confidence)
                .FirstOrDefault();
            if (top == null || string.IsNullOrWhiteSpace(top.Type))
            {
                return gestures;
            }

            session.RecentGestureLabels.Enqueue(top.Type);
            while (session.RecentGestureLabels.Count > 8)
            {
                session.RecentGestureLabels.Dequeue();
            }

            var smoothedLabel = session.RecentGestureLabels
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key == top.Type ? 1 : 0)
                .Select(g => g.Key)
                .FirstOrDefault() ?? top.Type;

            session.GestureConfidenceEma = session.GestureConfidenceEma <= 0.0
                ? top.Confidence
                : (0.65 * session.GestureConfidenceEma) + (0.35 * top.Confidence);

            top.Type = smoothedLabel;
            top.Confidence = Math.Clamp(session.GestureConfidenceEma, 0.1, 0.99);
            return gestures;
        }

        private void RunDetectionPlugins(
            SessionData session,
            DetectionData detectionData,
            List<DetectionNotification> notifications,
            List<SystemLog> logs)
        {
            foreach (var plugin in _detectionPlugins)
            {
                try
                {
                    plugin.Evaluate(session, detectionData, notifications, logs);
                }
                catch (Exception ex)
                {
                    logs.Add(new SystemLog
                    {
                        Message = $"Plugin {plugin.Name} failed: {ex.Message}",
                        Timestamp = DateTime.UtcNow,
                        Level = "Warning",
                        Component = "PluginPipeline"
                    });
                }
            }
        }

        private List<EyeMovement> AnalyzeEyeMovements(List<EyeDetection> eyes)
        {
            var eyeMovements = new List<EyeMovement>();

            foreach (var eye in eyes)
            {
                var direction = eye.Gaze switch
                {
                    GazeDirection.Left => "Left",
                    GazeDirection.Right => "Right",
                    GazeDirection.Up => "Up",
                    GazeDirection.Down => "Down",
                    _ => "Center"
                };

                eyeMovements.Add(new EyeMovement
                {
                    BBox = eye.BBox,
                    Direction = direction,
                    IsBlinking = string.Equals(eye.State, "Closed", StringComparison.OrdinalIgnoreCase),
                    Confidence = Math.Clamp(eye.Confidence, 0.1, 0.99),
                    GazeConfidence = Math.Clamp(eye.Confidence, 0.1, 0.99),
                    GazePoint = new Point2f(
                        (float)(eye.BBox.X + (eye.BBox.Width / 2.0)),
                        (float)(eye.BBox.Y + (eye.BBox.Height / 2.0)))
                });
            }

            return eyeMovements;
        }

        private MonitoringState BuildMonitoringState(
            SessionData session,
            List<FaceExpression> faceExpressions,
            List<HandGesture> handGestures,
            List<EyeMovement> eyeMovements)
        {
            var state = new MonitoringState();

            var topEmotion = faceExpressions
                .OrderByDescending(e => e.Confidence)
                .FirstOrDefault();
            if (topEmotion != null && !string.IsNullOrWhiteSpace(topEmotion.DominantEmotion))
            {
                var stableEmotionLabel = session.StableEmotionLabel;
                var emotionCandidateLabel = session.EmotionCandidateLabel;
                var stableEmotionStreak = session.StableEmotionStreak;
                UpdateStableLabel(
                    ref stableEmotionLabel,
                    ref emotionCandidateLabel,
                    ref stableEmotionStreak,
                    topEmotion.DominantEmotion,
                    minStreakToLock: 3);
                session.StableEmotionLabel = stableEmotionLabel;
                session.EmotionCandidateLabel = emotionCandidateLabel;
                session.StableEmotionStreak = stableEmotionStreak;
                state.StableEmotionConfidence = topEmotion.Confidence;
            }

            state.StableDominantEmotion = session.StableEmotionLabel;

            var topGesture = handGestures
                .OrderByDescending(g => g.Confidence)
                .FirstOrDefault();
            if (topGesture != null && !string.IsNullOrWhiteSpace(topGesture.Type))
            {
                var stableGestureLabel = session.StableGestureLabel;
                var gestureCandidateLabel = session.GestureCandidateLabel;
                var stableGestureStreak = session.StableGestureStreak;
                UpdateStableLabel(
                    ref stableGestureLabel,
                    ref gestureCandidateLabel,
                    ref stableGestureStreak,
                    topGesture.Type,
                    minStreakToLock: 2);
                session.StableGestureLabel = stableGestureLabel;
                session.GestureCandidateLabel = gestureCandidateLabel;
                session.StableGestureStreak = stableGestureStreak;
                state.StableGestureConfidence = topGesture.Confidence;
            }
            state.StableGesture = session.StableGestureLabel;

            var closedEyes = eyeMovements
                .Where(e => e.IsBlinking)
                .OrderBy(e => e.GazePoint.X)
                .ToList();
            var orderedEyes = eyeMovements
                .OrderBy(e => e.GazePoint.X)
                .ToList();

            if (closedEyes.Count == 1 && orderedEyes.Count >= 2)
            {
                state.OneEyeClosed = true;
                state.OneEyeClosedConfidence = closedEyes[0].Confidence;
                state.OneEyeClosedSide = closedEyes[0].GazePoint.X <= orderedEyes[0].GazePoint.X ? "Left" : "Right";
            }
            else
            {
                state.OneEyeClosed = false;
                state.OneEyeClosedSide = "None";
                state.OneEyeClosedConfidence = 0.0;
            }

            state.UpdatedAt = DateTime.UtcNow;
            return state;
        }

        private static HumanTrackingState BuildHumanTrackingState(SessionData session, DetectionData data, DetectionStats stats)
        {
            var ht = new HumanTrackingState
            {
                ActiveFaceTracks = data?.Faces?.Count ?? 0,
                ActiveHandTracks = data?.Hands?.Count ?? 0,
                UpdatedAt = DateTime.UtcNow
            };

            var est = ht.ActiveFaceTracks;
            if (est == 0 && ht.ActiveHandTracks > 0)
                est = 1;
            var cap = Math.Max(1, session?.Settings?.MaxTrackedPeople ?? 2);
            ht.EstimatedPersonCount = Math.Min(est, cap);

            var subjects = new List<SubjectTrackSnapshot>();

            if (data?.Faces != null && data.Faces.Count > 0)
            {
                var primary = data.Faces.OrderByDescending(f => f.BBox?.Area ?? 0).First();
                var cx = primary.BBox.Center.X;
                var cy = primary.BBox.Center.Y;
                var speed = 0.0;
                if (session.HasLastPrimaryFaceCenter)
                {
                    var dx = cx - session.LastPrimaryFaceCenterX;
                    var dy = cy - session.LastPrimaryFaceCenterY;
                    speed = Math.Sqrt(dx * dx + dy * dy);
                }

                session.LastPrimaryFaceCenterX = cx;
                session.LastPrimaryFaceCenterY = cy;
                session.HasLastPrimaryFaceCenter = true;
                ht.PrimarySubjectSpeed = speed;

                subjects.Add(new SubjectTrackSnapshot
                {
                    TrackId = primary.TrackId,
                    Kind = "Face",
                    CenterX = cx,
                    CenterY = cy,
                    Speed = speed,
                    Label = "PrimaryFace"
                });

                foreach (var f in data.Faces)
                {
                    if (ReferenceEquals(f, primary))
                        continue;
                    var c = f.BBox.Center;
                    subjects.Add(new SubjectTrackSnapshot
                    {
                        TrackId = f.TrackId,
                        Kind = "Face",
                        CenterX = c.X,
                        CenterY = c.Y,
                        Speed = 0,
                        Label = "Face"
                    });
                }
            }
            else
            {
                session.HasLastPrimaryFaceCenter = false;
                ht.PrimarySubjectSpeed = 0;
            }

            if (data?.Hands != null)
            {
                foreach (var h in data.Hands)
                {
                    var c = h.BBox.Center;
                    subjects.Add(new SubjectTrackSnapshot
                    {
                        TrackId = h.TrackId,
                        Kind = "Hand",
                        CenterX = c.X,
                        CenterY = c.Y,
                        Speed = 0,
                        Label = h.Gesture?.Type ?? "Hand"
                    });
                }
            }

            ht.Subjects = subjects;
            return ht;
        }

        private static BehaviorAnalysis BuildBehaviorAnalysis(
            DetectionData data,
            DetectionStats stats,
            MonitoringState mon)
        {
            var b = new BehaviorAnalysis { LastUpdate = DateTime.UtcNow };
            var movement = stats.CurrentMovementLevel;

            b.ActivityScore = Math.Clamp(
                movement * 1.8
                + (stats.GesturesDetected ? 18 : 0)
                + (stats.ExpressionsDetected ? 12 : 0),
                0, 100);

            if (movement < 8)
                b.SceneDynamics = "Calm";
            else if (movement < 22)
                b.SceneDynamics = "Active";
            else
                b.SceneDynamics = "Volatile";

            var faceCount = data?.Faces?.Count ?? 0;
            var handCount = data?.Hands?.Count ?? 0;
            var gestureBoost = string.Equals(mon.StableGesture, "None", StringComparison.OrdinalIgnoreCase) ? 0 : 25;
            var interactScore = faceCount * 15 + handCount * 20 + gestureBoost;
            if (interactScore < 25)
                b.InteractionLevel = "Low";
            else if (interactScore < 55)
                b.InteractionLevel = "Medium";
            else
                b.InteractionLevel = "High";

            b.EngagementLevel = Math.Clamp(
                mon.StableEmotionConfidence * 35
                + mon.StableGestureConfidence * 35
                + b.ActivityScore * 0.3,
                0, 100);

            b.Posture = "Unknown";
            if (faceCount > 0 && data.Faces != null)
            {
                var f = data.Faces.OrderByDescending(x => x.BBox?.Area ?? 0).First();
                var bb = f.BBox;
                if (bb != null)
                {
                    var ar = bb.Width / Math.Max(1.0, bb.Height);
                    if (bb.Area > 120 * 120 && ar > 0.65 && ar < 1.35)
                        b.Posture = "FacingCamera";
                    else if (bb.Area > 40 * 40)
                        b.Posture = "Present";
                    else
                        b.Posture = "Distant";
                }
            }

            b.DetectedBehaviors = new List<string>();
            if (stats.MovementDetected)
                b.DetectedBehaviors.Add("SceneMotion");
            if (mon.OneEyeClosed)
                b.DetectedBehaviors.Add($"OneEyeClosed:{mon.OneEyeClosedSide}");
            if (!string.IsNullOrEmpty(mon.StableGesture) && !string.Equals(mon.StableGesture, "None", StringComparison.OrdinalIgnoreCase))
                b.DetectedBehaviors.Add($"Gesture:{mon.StableGesture}");
            if (!string.IsNullOrEmpty(mon.StableDominantEmotion)
                && !string.Equals(mon.StableDominantEmotion, "Neutral", StringComparison.OrdinalIgnoreCase))
                b.DetectedBehaviors.Add($"Affect:{mon.StableDominantEmotion}");
            if (handCount > 0)
                b.DetectedBehaviors.Add("HandActivity");
            if (faceCount > 0)
                b.DetectedBehaviors.Add("FacePresent");

            return b;
        }

        private static void UpdateStableLabel(
            ref string stableLabel,
            ref string candidateLabel,
            ref int streak,
            string newLabel,
            int minStreakToLock)
        {
            if (string.IsNullOrWhiteSpace(newLabel))
            {
                return;
            }

            if (string.Equals(stableLabel, newLabel, StringComparison.OrdinalIgnoreCase))
            {
                candidateLabel = newLabel;
                streak = 0;
                return;
            }

            if (!string.Equals(candidateLabel, newLabel, StringComparison.OrdinalIgnoreCase))
            {
                candidateLabel = newLabel;
                streak = 1;
            }
            else
            {
                streak++;
            }

            if (streak >= minStreakToLock)
            {
                stableLabel = newLabel;
                streak = 0;
            }
        }

        private static void AppendIntelligenceEvents(
            SessionData session,
            MonitoringState monitoringState,
            DetectionData detectionData,
            BehaviorAnalysis behavior)
        {
            if (session == null || monitoringState == null)
            {
                return;
            }

            if (monitoringState.OneEyeClosed)
            {
                session.PushEvent(new IntelligenceEvent
                {
                    Type = "eye_close_alert",
                    Severity = "Warning",
                    Message = $"One eye closed: {monitoringState.OneEyeClosedSide}",
                    TimestampUtc = DateTime.UtcNow,
                    Context = new Dictionary<string, string>
                    {
                        ["sessionId"] = session.SessionId,
                        ["side"] = monitoringState.OneEyeClosedSide ?? "unknown"
                    }
                });
            }

            if (!string.IsNullOrWhiteSpace(monitoringState.StableGesture)
                && monitoringState.StableGesture.Contains("Raised", StringComparison.OrdinalIgnoreCase))
            {
                session.PushEvent(new IntelligenceEvent
                {
                    Type = "hand_raise_trigger",
                    Severity = "Info",
                    Message = "Hand raise trigger fired",
                    TimestampUtc = DateTime.UtcNow,
                    Context = new Dictionary<string, string>
                    {
                        ["sessionId"] = session.SessionId,
                        ["gesture"] = monitoringState.StableGesture
                    }
                });
            }

            if (behavior != null && behavior.EngagementLevel < 25)
            {
                session.PushEvent(new IntelligenceEvent
                {
                    Type = "attention_drop",
                    Severity = "Warning",
                    Message = "Attention level dropped below threshold",
                    TimestampUtc = DateTime.UtcNow,
                    Context = new Dictionary<string, string>
                    {
                        ["sessionId"] = session.SessionId,
                        ["engagement"] = behavior.EngagementLevel.ToString("0.0")
                    }
                });
            }

            var hands = detectionData?.Hands?.Count ?? 0;
            var objects = detectionData?.Objects?.Count ?? 0;
            if (hands > 0 && objects > 0)
            {
                session.PushEvent(new IntelligenceEvent
                {
                    Type = "context_fusion",
                    Severity = "Info",
                    Message = "Person/gesture/object context fused",
                    TimestampUtc = DateTime.UtcNow,
                    Context = new Dictionary<string, string>
                    {
                        ["sessionId"] = session.SessionId,
                        ["hands"] = hands.ToString(),
                        ["objects"] = objects.ToString()
                    }
                });
            }
        }

        private bool ShouldSkipFrame(SessionData session)
        {
            if (!session.Settings.EnableFrameRateControl)
                return false;

            if (session.AdaptiveSkipCountdown > 0)
            {
                session.AdaptiveSkipCountdown--;
                session.ScheduledSkipCount++;
                return true;
            }

            if (session.FramesToSkip > 0)
            {
                session.FramesToSkip--;
                session.ScheduledSkipCount++;
                return true;
            }

            var targetFPS = session.Settings.TargetFPS;
            var currentTime = DateTime.UtcNow;

            if (session.LastFrameTime != DateTime.MinValue)
            {
                var timeSinceLastFrame = (currentTime - session.LastFrameTime).TotalSeconds;
                var targetFrameTime = 1.0 / targetFPS;

                if (timeSinceLastFrame < targetFrameTime)
                {
                    session.ScheduledSkipCount++;
                    return true;
                }
            }

            session.LastFrameTime = currentTime;
            return false;
        }

        private bool ShouldRunHeavyModule(SessionData session, int interval)
        {
            var effective = Math.Max(1, interval);
            return (session.FrameSequence % effective) == 0;
        }

        private void UpdateProcessingTime(SessionData session, double processingTimeMs)
        {
            session.AddProcessingTime(processingTimeMs);

            // Calculate actual FPS based on processing time
            var avgProcessingTime = session.GetAverageProcessingTime();
            session.Stats.ActualProcessingFPS = avgProcessingTime > 0 ? 1000.0 / avgProcessingTime : 0;
            session.Stats.AverageProcessingTimeMs = avgProcessingTime;
        }

        private Mat ProcessFrame(Mat frame, SessionData session, List<DetectionNotification> notifications,
            List<SystemLog> logs, DetectionData detectionData, string processingMode, SceneProcessingOptions? sceneOptions = null,
            List<ClientHandData> clientHands = null)
        {
            if (frame.Empty())
            {
                logs.Add(new SystemLog
                {
                    Message = "Input frame is empty",
                    Timestamp = DateTime.UtcNow,
                    Level = "Error",
                    Component = "Processing"
                });
                return frame.Clone();
            }

            var processedFrame = frame.Clone();
            var stats = session.Stats;
            var config = session.MonitoringConfig;
            session.FrameSequence++;
            var runHeavy = !session.Settings.EnableRoiScheduling || ShouldRunHeavyModule(session, session.Settings.SchedulerHeavyFrameInterval);
            var runScene = !session.Settings.EnableRoiScheduling || ShouldRunHeavyModule(session, session.Settings.SchedulerSceneFrameInterval);

            try
            {
                using (var grayFrame = new Mat())
                {
                    Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);
                    if (session.Stats.AverageBrightness < session.Settings.LowLightThreshold)
                    {
                        using (var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8)))
                        {
                            clahe.Apply(grayFrame, grayFrame);
                        }
                    }
                    else
                    {
                        Cv2.EqualizeHist(grayFrame, grayFrame);
                    }

                    // Camera movement analysis
                    if (IsMonitoringEnabled(config, "CameraMovementAnalysis"))
                    {
                        AnalyzeCameraMovement(frame, grayFrame, session, stats, notifications);
                    }

                    // Face detection
                    if (IsMonitoringEnabled(config, "FaceDetection"))
                    {
                        var faceStart = Stopwatch.StartNew();
                        SafeFaceDetection(processedFrame, grayFrame, session, stats, notifications, logs, detectionData);
                        faceStart.Stop();
                        session.AddModuleTiming(session.FaceModuleMs, faceStart.Elapsed.TotalMilliseconds);
                    }

                    // Hand detection
                    if (IsMonitoringEnabled(config, "HandDetection") && runHeavy)
                    {
                        var handStart = Stopwatch.StartNew();
                        SafeHandDetection(processedFrame, session, stats, notifications, logs, detectionData, clientHands);
                        handStart.Stop();
                        session.AddModuleTiming(session.HandModuleMs, handStart.Elapsed.TotalMilliseconds);
                    }

                    // Movement detection
                    if (IsMonitoringEnabled(config, "MovementDetection") && session.PreviousFrame != null)
                    {
                        SafeMovementDetection(frame, processedFrame, session, stats, notifications, logs, detectionData);
                    }

                    // Text detection every 15 frames (for performance)
                    if (IsMonitoringEnabled(config, "TextDetection") && runHeavy && stats.TotalFramesProcessed % 15 == 0)
                    {
                        var ocrStart = Stopwatch.StartNew();
                        SafeTextDetection(processedFrame, stats, notifications, logs, session, detectionData);
                        ocrStart.Stop();
                        session.AddModuleTiming(session.OcrModuleMs, ocrStart.Elapsed.TotalMilliseconds);
                    }

                    if (string.Equals(processingMode, "scene", StringComparison.OrdinalIgnoreCase) && runScene)
                    {
                        var sceneStart = Stopwatch.StartNew();
                        RunSceneEntityDetection(processedFrame, grayFrame, session, detectionData, logs, sceneOptions);
                        sceneStart.Stop();
                        session.AddModuleTiming(session.SceneModuleMs, sceneStart.Elapsed.TotalMilliseconds);
                    }
                    else
                    {
                        session.LastSceneAnalysis = null;
                    }

                    // Update previous frame
                    SafeDispose(session.PreviousFrame);
                    SafeDispose(session.PreviousGrayFrame);

                    session.PreviousFrame = frame.Clone();
                    session.PreviousGrayFrame = grayFrame.Clone();

                    // Update statistics
                    stats.TotalFramesProcessed++;
                    stats.TargetFPS = session.Settings.TargetFPS;
                    UpdateFPS(stats);

                    // Update memory usage (approximate)
                    stats.MemoryUsageMB = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                    stats.IsSystemOptimal = stats.ActualProcessingFPS >= stats.TargetFPS * 0.7;

                    if (stats.AverageProcessingTimeMs > 45 && session.Settings.EnableAdaptiveProcessing)
                    {
                        session.AdaptiveSkipCountdown = Math.Max(session.AdaptiveSkipCountdown, 1);
                    }

                    // Overlay stats and camera movement status
                    AddStatsOverlay(processedFrame, stats);
                    AddCameraMovementStatus(processedFrame, stats);
                }
            }
            catch (Exception ex)
            {
                logs.Add(new SystemLog
                {
                    Message = $"Frame processing error: {ex.Message}",
                    Timestamp = DateTime.UtcNow,
                    Level = "Error",
                    Component = "Processing"
                });

                SafeDispose(processedFrame);
                return frame.Clone();
            }

            return processedFrame;
        }

        private void SafeDispose(IDisposable disposable)
        {
            try
            {
                disposable?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SafeDispose error: {ex.Message}");
            }
        }

        private bool IsMonitoringEnabled(MonitoringConfiguration config, string optionName)
        {
            var option = config.EnabledOptions?.FirstOrDefault(o => o.Name == optionName);
            return option?.IsEnabled ?? _availableMonitoringOptions.FirstOrDefault(o => o.Name == optionName)?.IsEnabled ?? false;
        }

        private void AnalyzeCameraMovement(Mat currentFrame, Mat grayFrame, SessionData session,
            DetectionStats stats, List<DetectionNotification> notifications)
        {
            if (session.PreviousGrayFrame == null || session.PreviousGrayFrame.Empty() || grayFrame.Empty())
                return;

            try
            {
                double movementLevel = 0.0;

                using (var diff = new Mat())
                {
                    Cv2.Absdiff(session.PreviousGrayFrame, grayFrame, diff);
                    Cv2.Threshold(diff, diff, 25, 255, ThresholdTypes.Binary);

                    var nonZeroPixels = Cv2.CountNonZero(diff);
                    var totalPixels = diff.Width * diff.Height;
                    movementLevel = totalPixels > 0 ? (double)nonZeroPixels / totalPixels * 100.0 : 0.0;
                }

                // Calculate stability (inverse of movement)
                double stability = Math.Max(0, 100 - movementLevel * 1.5);

                // Determine movement type based on movement level
                var movementType = movementLevel switch
                {
                    < 1.0 => CameraMovementType.Stable,
                    < 5.0 => CameraMovementType.SlowPan,
                    < 10.0 => CameraMovementType.SlowTilt,
                    < 20.0 => CameraMovementType.FastPan,
                    < 30.0 => CameraMovementType.FastTilt,
                    _ => CameraMovementType.Shaking
                };

                // Update stats
                stats.CameraMovement = movementType;
                stats.CameraStability = stability;
                stats.CurrentMovementLevel = movementLevel;

                // Create simple movement vector for display
                stats.RecentMovements = new List<MovementVector>
                {
                    new MovementVector
                    {
                        X = movementLevel / 10.0,
                        Y = movementLevel / 10.0,
                        Magnitude = movementLevel,
                        Timestamp = DateTime.UtcNow,
                        Direction = MovementDirection.None
                    }
                };

                // Add notification if stability is low
                if (stability < session.Settings.CameraStabilityThreshold && movementLevel > 5.0)
                {
                    notifications.Add(new DetectionNotification
                    {
                        Type = "Camera",
                        Message = $"Camera stability low: {stability:0}% (Movement: {movementLevel:0.0}%)",
                        Timestamp = DateTime.UtcNow,
                        Severity = "Warning",
                        Category = "Camera"
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Camera movement analysis error: {ex.Message}");
                // Safe fallback values
                stats.CameraMovement = CameraMovementType.Stable;
                stats.CameraStability = 100.0;
                stats.CurrentMovementLevel = 0.0;
                stats.RecentMovements = new List<MovementVector>();
            }
        }

        private void SafeFaceDetection(Mat processedFrame, Mat grayFrame, SessionData session,
            DetectionStats stats, List<DetectionNotification> notifications, List<SystemLog> logs,
            DetectionData detectionData)
        {
            try
            {
                var yunetNet = GetOrLoadYuNetFace();
                if (yunetNet == null)
                {
                    logs.Add(new SystemLog
                    {
                        Message = "YuNet face detector not available",
                        Timestamp = DateTime.UtcNow,
                        Level = "Warning",
                        Component = "FaceDetection"
                    });
                    return;
                }

                var scoreThreshold = (float)Math.Clamp(session.Settings.FaceConfidenceThreshold, 0.1, 0.95);
                var faces = YuNetDecoder.Detect(yunetNet, processedFrame, scoreThreshold, 0.3f);

                detectionData.Faces.Clear();
                detectionData.Eyes.Clear();

                foreach (var face in faces)
                {
                    var faceRect = face.BBox.Intersect(new Rect(0, 0, processedFrame.Width, processedFrame.Height));
                    if (faceRect.Width < 4 || faceRect.Height < 4)
                        continue;

                    Cv2.Rectangle(processedFrame, faceRect, Scalar.Red, 2);
                    Cv2.PutText(processedFrame, "Face",
                        new Point(faceRect.X, faceRect.Y - 10),
                        HersheyFonts.HersheySimplex, 0.5, Scalar.Red, 1);

                    var faceExpression = AnalyzeFaceEmotion(processedFrame, faceRect);
                    detectionData.Faces.Add(new FaceDetection
                    {
                        BBox = new BoundingBox
                        {
                            X = faceRect.X,
                            Y = faceRect.Y,
                            Width = faceRect.Width,
                            Height = faceRect.Height
                        },
                        Confidence = face.Score,
                        Expression = faceExpression,
                        TrackId = detectionData.Faces.Count
                    });

                    if (IsMonitoringEnabled(session.MonitoringConfig, "EyeDetection") && face.Landmarks?.Length >= 2)
                    {
                        AddEyesFromLandmarks(processedFrame, faceRect, face.Landmarks, detectionData);
                    }

                    if (face.Landmarks?.Length >= 5)
                    {
                        AddLipsFromLandmarks(processedFrame, faceRect, face.Landmarks, detectionData);
                    }
                }

                stats.FacesDetected = detectionData.Faces.Count;
                stats.EyesDetected = detectionData.Eyes.Count;

                if (detectionData.Faces.Count > 0 && !session.LastFaceState)
                {
                    notifications.Add(new DetectionNotification
                    {
                        Type = "FaceDetection",
                        Message = $"{detectionData.Faces.Count} face(s) detected",
                        Timestamp = DateTime.UtcNow,
                        Severity = "Info",
                        Category = "Detection"
                    });
                }
                session.LastFaceState = detectionData.Faces.Count > 0;
            }
            catch (Exception ex)
            {
                logs.Add(new SystemLog
                {
                    Message = $"Face detection error: {ex.Message}",
                    Timestamp = DateTime.UtcNow,
                    Level = "Error",
                    Component = "FaceDetection"
                });
            }
        }

        /// <summary>
        /// Derives eye boxes/gaze from YuNet's two eye landmark points (right eye, left eye).
        /// YuNet gives a point, not an eye contour, so there is no aspect-ratio signal to detect
        /// blinks from — State is reported as "Open" rather than fabricating a closed/open guess.
        /// </summary>
        private void AddEyesFromLandmarks(Mat processedFrame, Rect faceRect, System.Drawing.PointF[] landmarks, DetectionData detectionData)
        {
            var eyeSize = Math.Max(6, (int)(faceRect.Width * 0.16));
            var frameBounds = new Rect(0, 0, processedFrame.Width, processedFrame.Height);

            void AddEye(System.Drawing.PointF point)
            {
                var eyeRect = new Rect(
                    (int)Math.Round(point.X - eyeSize / 2.0),
                    (int)Math.Round(point.Y - eyeSize / 2.0),
                    eyeSize,
                    eyeSize).Intersect(frameBounds);
                if (eyeRect.Width < 2 || eyeRect.Height < 2)
                    return;

                var faceCenterX = faceRect.X + faceRect.Width / 2.0;
                var faceCenterY = faceRect.Y + faceRect.Height / 2.0;
                var normalizedOffsetX = (point.X - faceCenterX) / Math.Max(1, faceRect.Width);
                var normalizedOffsetY = (point.Y - faceCenterY) / Math.Max(1, faceRect.Height);
                var gaze = normalizedOffsetX switch
                {
                    < -0.15 => GazeDirection.Left,
                    > 0.15 => GazeDirection.Right,
                    _ => GazeDirection.Center
                };
                if (gaze == GazeDirection.Center)
                {
                    gaze = normalizedOffsetY switch
                    {
                        < -0.10 => GazeDirection.Up,
                        > 0.10 => GazeDirection.Down,
                        _ => GazeDirection.Center
                    };
                }

                Cv2.Rectangle(processedFrame, eyeRect, Scalar.Blue, 1);
                Cv2.PutText(processedFrame, "Eye",
                    new Point(eyeRect.X, eyeRect.Y - 5),
                    HersheyFonts.HersheySimplex, 0.3, Scalar.Blue, 1);

                detectionData.Eyes.Add(new EyeDetection
                {
                    BBox = new BoundingBox { X = eyeRect.X, Y = eyeRect.Y, Width = eyeRect.Width, Height = eyeRect.Height },
                    Confidence = 0.75,
                    State = "Open",
                    Gaze = gaze,
                    TrackId = detectionData.Eyes.Count
                });
            }

            AddEye(landmarks[0]);
            AddEye(landmarks[1]);
        }

        /// <summary>
        /// Builds a "Lips" object box from YuNet's two mouth-corner landmarks (index 3 = right
        /// corner, 4 = left corner). Two points give zero height on their own, so pad vertically
        /// by a fraction of face height to get a usable mouth region — same rough sizing the old
        /// Haar mouth-region crop used, just centered on real landmark points instead of a guess.
        /// </summary>
        private void AddLipsFromLandmarks(Mat processedFrame, Rect faceRect, System.Drawing.PointF[] landmarks, DetectionData detectionData)
        {
            var rightCorner = landmarks[3];
            var leftCorner = landmarks[4];

            var minX = Math.Min(rightCorner.X, leftCorner.X);
            var maxX = Math.Max(rightCorner.X, leftCorner.X);
            var centerY = (rightCorner.Y + leftCorner.Y) / 2.0;
            var padX = Math.Max(4, (maxX - minX) * 0.25);
            var halfHeight = Math.Max(4, faceRect.Height * 0.09);

            var mouthRect = new Rect(
                (int)Math.Round(minX - padX),
                (int)Math.Round(centerY - halfHeight),
                (int)Math.Round((maxX - minX) + 2 * padX),
                (int)Math.Round(halfHeight * 2)
            ).Intersect(new Rect(0, 0, processedFrame.Width, processedFrame.Height));
            if (mouthRect.Width < 4 || mouthRect.Height < 4)
                return;

            Cv2.Rectangle(processedFrame, mouthRect, Scalar.Magenta, 1);
            Cv2.PutText(processedFrame, "Lips",
                new Point(mouthRect.X, mouthRect.Y - 4),
                HersheyFonts.HersheySimplex, 0.35, Scalar.Magenta, 1);

            detectionData.Objects.Add(new ObjectDetection
            {
                Type = "Lips",
                Confidence = 0.75,
                AdditionalInfo = "Mouth region",
                BBox = new BoundingBox
                {
                    X = mouthRect.X,
                    Y = mouthRect.Y,
                    Width = mouthRect.Width,
                    Height = mouthRect.Height
                }
            });
        }

        /// <summary>
        /// Hands are detected client-side by the browser's real MediaPipe Hands solution (see
        /// hands.js) and sent up as normalized landmarks — there is no server-side hand model.
        /// This used to run a Haar upperbody cascade as a hand proxy and fabricate a 21-point
        /// skeleton from the bounding box; that was never real hand tracking, just a shaped guess.
        /// </summary>
        private void SafeHandDetection(Mat processedFrame, SessionData session, DetectionStats stats,
            List<DetectionNotification> notifications, List<SystemLog> logs, DetectionData detectionData,
            List<ClientHandData> clientHands)
        {
            try
            {
                detectionData.Hands.Clear();
                if (clientHands == null || clientHands.Count == 0)
                {
                    stats.HandsDetected = 0;
                    return;
                }

                var frameWidth = processedFrame.Width;
                var frameHeight = processedFrame.Height;
                var maxHands = Math.Max(1, session.Settings.HandOnDemandOnly ? 1 : session.Settings.MaxTrackedHands);

                foreach (var hand in clientHands.Take(maxHands))
                {
                    if (hand?.Landmarks == null || hand.Landmarks.Count == 0)
                        continue;
                    if (hand.Score < session.Settings.HandConfidenceThreshold)
                        continue;

                    var pixelLandmarks = hand.Landmarks.Select(p => new LandmarkPoint
                    {
                        X = p.X * frameWidth,
                        Y = p.Y * frameHeight,
                        Z = p.Z,
                        Confidence = hand.Score,
                        Type = p.Type
                    }).ToList();

                    var minX = pixelLandmarks.Min(p => p.X);
                    var maxX = pixelLandmarks.Max(p => p.X);
                    var minY = pixelLandmarks.Min(p => p.Y);
                    var maxY = pixelLandmarks.Max(p => p.Y);
                    var bbox = new BoundingBox
                    {
                        X = minX,
                        Y = minY,
                        Width = Math.Max(1, maxX - minX),
                        Height = Math.Max(1, maxY - minY)
                    };

                    var rect = new Rect((int)bbox.X, (int)bbox.Y, (int)bbox.Width, (int)bbox.Height)
                        .Intersect(new Rect(0, 0, frameWidth, frameHeight));
                    if (rect.Width < 2 || rect.Height < 2)
                        continue;

                    Cv2.Rectangle(processedFrame, rect, Scalar.Green, 2);
                    Cv2.PutText(processedFrame, "Hand",
                        new Point(rect.X, rect.Y - 10),
                        HersheyFonts.HersheySimplex, 0.5, Scalar.Green, 1);

                    var handDetection = new HandDetection
                    {
                        BBox = bbox,
                        Confidence = hand.Score,
                        Handedness = hand.Handedness ?? "Unknown",
                        Landmarks = pixelLandmarks,
                        TrackId = detectionData.Hands.Count
                    };
                    handDetection.Gesture = InferGestureWithModel(handDetection, useEnhancedPipeline: true);
                    if (handDetection.Gesture != null && handDetection.Gesture.KeyPoints.Count == 0)
                    {
                        handDetection.Gesture.KeyPoints = new List<LandmarkPoint>(handDetection.Landmarks);
                    }
                    detectionData.Hands.Add(handDetection);
                }

                stats.HandsDetected = detectionData.Hands.Count;

                if (detectionData.Hands.Count > 0)
                {
                    notifications.Add(new DetectionNotification
                    {
                        Type = "HandDetection",
                        Message = $"{detectionData.Hands.Count} hand(s) tracked",
                        Timestamp = DateTime.UtcNow,
                        Severity = "Info",
                        Category = "Detection"
                    });
                }
            }
            catch (Exception ex)
            {
                logs.Add(new SystemLog
                {
                    Message = $"Hand detection error: {ex.Message}",
                    Timestamp = DateTime.UtcNow,
                    Level = "Error",
                    Component = "HandDetection"
                });
            }
        }

        private static double EstimateSceneBrightness(Mat frame)
        {
            if (frame == null || frame.Empty())
            {
                return 0.0;
            }

            using (var gray = new Mat())
            {
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                return Cv2.Mean(gray).Val0;
            }
        }

        private void SafeMovementDetection(Mat currentFrame, Mat processedFrame, SessionData session,
            DetectionStats stats, List<DetectionNotification> notifications, List<SystemLog> logs,
            DetectionData detectionData)
        {
            try
            {
                var movementLevel = CalculateMovementLevel(currentFrame, session.PreviousFrame);
                stats.CurrentMovementLevel = movementLevel;
                var effectiveThreshold = Math.Max(session.Settings.MovementThreshold, session.BaselineMovement * 1.35);
                stats.MovementDetected = movementLevel > effectiveThreshold;

                if (stats.MovementDetected && !session.LastMovementState)
                {
                    notifications.Add(new DetectionNotification
                    {
                        Type = "MovementDetection",
                        Message = $"Movement detected: {movementLevel:0.0}%",
                        Timestamp = DateTime.UtcNow,
                        Severity = "Info",
                        Category = "Detection"
                    });
                }
                else if (!stats.MovementDetected && session.LastMovementState)
                {
                    notifications.Add(new DetectionNotification
                    {
                        Type = "MovementDetection",
                        Message = "Movement stopped",
                        Timestamp = DateTime.UtcNow,
                        Severity = "Info",
                        Category = "Detection"
                    });
                }
                session.LastMovementState = stats.MovementDetected;

                if (stats.MovementDetected)
                {
                    detectionData.Objects.Add(new ObjectDetection
                    {
                        Type = "Movement",
                        Confidence = movementLevel / 100.0,
                        AdditionalInfo = $"Movement level: {movementLevel:0.0}%",
                        BBox = new BoundingBox() // Empty bounding box for movement
                    });
                }

                DrawMovementIndicator(processedFrame, movementLevel);
            }
            catch (Exception ex)
            {
                logs.Add(new SystemLog
                {
                    Message = $"Movement detection error: {ex.Message}",
                    Timestamp = DateTime.UtcNow,
                    Level = "Error",
                    Component = "MovementDetection"
                });
            }
        }

        private double CalculateMovementLevel(Mat currentFrame, Mat previousFrame)
        {
            try
            {
                if (previousFrame == null || previousFrame.Empty() || currentFrame.Empty())
                    return 0.0;

                using (var diff = new Mat())
                using (var grayCurrent = new Mat())
                using (var grayPrevious = new Mat())
                {
                    Cv2.CvtColor(currentFrame, grayCurrent, ColorConversionCodes.BGR2GRAY);
                    Cv2.CvtColor(previousFrame, grayPrevious, ColorConversionCodes.BGR2GRAY);
                    Cv2.Absdiff(grayCurrent, grayPrevious, diff);
                    Cv2.Threshold(diff, diff, 25, 255, ThresholdTypes.Binary);

                    var nonZeroPixels = Cv2.CountNonZero(diff);
                    var totalPixels = diff.Width * diff.Height;
                    return totalPixels > 0 ? (double)nonZeroPixels / totalPixels * 100.0 : 0.0;
                }
            }
            catch
            {
                return 0.0;
            }
        }

        private void SafeTextDetection(Mat frame, DetectionStats stats, List<DetectionNotification> notifications,
            List<SystemLog> logs, SessionData session, DetectionData detectionData)
        {
            try
            {
                if (string.IsNullOrEmpty(_tessDataPath) || !Directory.Exists(_tessDataPath))
                {
                    logs.Add(new SystemLog
                    {
                        Message = "Tesseract data path not available",
                        Timestamp = DateTime.UtcNow,
                        Level = "Warning",
                        Component = "TextDetection"
                    });
                    return;
                }

                using (var tesseractEngine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default))
                {
                    tesseractEngine.SetVariable("tessedit_char_whitelist",
                        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?()-+*/=@#$%&");

                    using (var tempBitmap = BitmapConverter.ToBitmap(frame))
                    using (var tempStream = new MemoryStream())
                    {
                        tempBitmap.Save(tempStream, SDI.ImageFormat.Png);
                        using (var pix = Pix.LoadFromMemory(tempStream.ToArray()))
                        using (var page = tesseractEngine.Process(pix))
                        {
                            var text = page.GetText()?.Trim();
                            if (!string.IsNullOrEmpty(text) && text.Length > 3)
                            {
                                stats.TextDetected = true;
                                var displayText = text.Length > 30 ? text.Substring(0, 30) + "..." : text;

                                notifications.Add(new DetectionNotification
                                {
                                    Type = "TextDetection",
                                    Message = $"Text detected: {displayText}",
                                    Timestamp = DateTime.UtcNow,
                                    Severity = "Info",
                                    Category = "Detection"
                                });

                                session.LastDetectedText = text;

                                detectionData.TextRegions.Add(new TextDetection
                                {
                                    Content = text,
                                    Confidence = page.GetMeanConfidence() / 100.0,
                                    Language = "en",
                                    BBox = new BoundingBox() // OCR doesn't provide precise bounding boxes
                                });

                                Cv2.PutText(frame, "Text Detected",
                                    new Point(10, frame.Height - 30),
                                    HersheyFonts.HersheySimplex, 0.6, Scalar.Yellow, 2);
                            }
                            else
                            {
                                stats.TextDetected = false;
                                session.LastDetectedText = null;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logs.Add(new SystemLog
                {
                    Message = $"Text detection error: {ex.Message}",
                    Timestamp = DateTime.UtcNow,
                    Level = "Error",
                    Component = "TextDetection"
                });
            }
        }

        private void DrawMovementIndicator(Mat frame, double movementLevel)
        {
            try
            {
                int meterWidth = 200;
                int meterHeight = 20;
                int meterX = frame.Width - meterWidth - 10;
                int meterY = 10;

                // Background
                Cv2.Rectangle(frame, new Rect(meterX, meterY, meterWidth, meterHeight), Scalar.DarkGray, -1);

                // Fill based on movement level
                int fillWidth = (int)(movementLevel / 100.0 * meterWidth);
                var color = GetMovementColor(movementLevel);
                Cv2.Rectangle(frame, new Rect(meterX, meterY, fillWidth, meterHeight), color, -1);

                // Border
                Cv2.Rectangle(frame, new Rect(meterX, meterY, meterWidth, meterHeight), Scalar.White, 1);

                // Label
                Cv2.PutText(frame, $"Movement: {movementLevel:0.0}%",
                    new Point(meterX, meterY + meterHeight + 15),
                    HersheyFonts.HersheySimplex, 0.4, Scalar.White, 1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error drawing movement indicator: {ex.Message}");
            }
        }

        private Scalar GetMovementColor(double movementLevel)
        {
            if (movementLevel < 10) return new Scalar(0, 255, 0);    // Green
            if (movementLevel < 30) return new Scalar(0, 255, 255);  // Yellow
            if (movementLevel < 50) return new Scalar(0, 165, 255);  // Orange
            return new Scalar(0, 0, 255);                            // Red
        }

        private void AddStatsOverlay(Mat frame, DetectionStats stats)
        {
            try
            {
                string[] statsText =
                {
                    $"Faces: {stats.FacesDetected}",
                    $"Eyes: {stats.EyesDetected}",
                    $"Hands: {stats.HandsDetected}",
                    $"Movement: {(stats.MovementDetected ? "Yes" : "No")}",
                    $"Text: {(stats.TextDetected ? "Yes" : "No")}",
                    $"FPS: {stats.CurrentFPS:0.0}",
                    $"Frame: {stats.TotalFramesProcessed}"
                };

                int yOffset = 60; // Start below camera status bar
                foreach (var text in statsText)
                {
                    Cv2.PutText(frame, text, new Point(10, yOffset),
                        HersheyFonts.HersheySimplex, 0.5, Scalar.White, 1);
                    yOffset += 20;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding stats overlay: {ex.Message}");
            }
        }

        private void AddCameraMovementStatus(Mat frame, DetectionStats stats)
        {
            try
            {
                var statusBarHeight = 30;
                var statusBar = new Rect(0, 0, frame.Width, statusBarHeight);

                // Background
                Cv2.Rectangle(frame, statusBar, new Scalar(50, 50, 50), -1);

                // Camera movement status
                var statusText = $"Camera: {stats.CameraMovement} | Stability: {stats.CameraStability:0}%";
                var movementColor = GetCameraMovementColor(stats.CameraMovement, stats.CameraStability);

                Cv2.PutText(frame, statusText,
                    new Point(10, statusBarHeight - 10),
                    HersheyFonts.HersheySimplex, 0.5, movementColor, 1);

                // Frame rate status
                var fpsText = $"FPS: Target={stats.TargetFPS:0} | Actual={stats.ActualProcessingFPS:0.0}";
                var fpsColor = stats.ActualProcessingFPS >= stats.TargetFPS * 0.8 ? Scalar.Green : Scalar.Yellow;

                Cv2.PutText(frame, fpsText,
                    new Point(frame.Width - 250, statusBarHeight - 10),
                    HersheyFonts.HersheySimplex, 0.5, fpsColor, 1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding camera status: {ex.Message}");
            }
        }

        private Scalar GetCameraMovementColor(CameraMovementType movement, double stability)
        {
            if (stability > 80) return new Scalar(0, 255, 0);    // Green - stable
            if (stability > 60) return new Scalar(0, 255, 255);  // Yellow - moderate
            return new Scalar(0, 0, 255);                        // Red - unstable
        }

        private void UpdateFPS(DetectionStats stats)
        {
            var now = DateTime.UtcNow;
            if (stats.LastFPSCalculation == DateTime.MinValue)
            {
                stats.LastFPSCalculation = now;
                stats.FramesSinceLastCalculation = 0;
            }
            else if ((now - stats.LastFPSCalculation).TotalSeconds >= 1.0)
            {
                stats.CurrentFPS = stats.FramesSinceLastCalculation;
                stats.FramesSinceLastCalculation = 0;
                stats.LastFPSCalculation = now;
            }
            else
            {
                stats.FramesSinceLastCalculation++;
            }
        }

        // Session management methods
        private void InitializeSessionIfNotExists(string sessionId)
        {
            _sessions.AddOrUpdate(sessionId,
                id => new SessionData
                {
                    SessionId = id,
                    Stats = new DetectionStats(),
                    Settings = new DetectionSettings(),
                    MonitoringConfig = new MonitoringConfiguration
                    {
                        SessionId = id,
                        EnabledOptions = new List<MonitoringOption>(_availableMonitoringOptions),
                        FrameRateControl = new FrameRateControl(),
                        CameraMovementConfig = new CameraMovementConfig(),
                        AlertConfig = new AlertConfiguration()
                    },
                    LastDetectedText = null,
                    CreatedAt = DateTime.UtcNow,
                    LastFrameTime = DateTime.UtcNow,
                    ProcessingTimes = new Queue<double>(),
                    MovementHistory = new List<MovementVector>(),
                    InferenceProvider = _inferenceProvider
                },
                (id, existing) => existing);

            if (_sessions.TryGetValue(sessionId, out var session) && !session.ProfileLoaded)
            {
                ApplyPersistedSessionProfile(sessionId, session, "default");
                var lastSelectedProfile = GetLastSelectedProfile(sessionId);
                if (!string.IsNullOrWhiteSpace(lastSelectedProfile) &&
                    !string.Equals(lastSelectedProfile, "default", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPersistedSessionProfile(sessionId, session, lastSelectedProfile);
                }
                session.ProfileLoaded = true;
            }
        }

        public DetectionStats GetSessionStats(string sessionId)
        {
            return _sessions.TryGetValue(sessionId, out var session)
                ? session.Stats.Clone()
                : new DetectionStats();
        }

        public DetectionSettings GetSessionSettings(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                return session.Settings ?? new DetectionSettings();
            }

            return new DetectionSettings();
        }

        public SessionAnalytics GetSessionAnalytics(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                return new SessionAnalytics
                {
                    SessionId = sessionId,
                    Stats = session.Stats.Clone(),
                    SessionDuration = DateTime.UtcNow - session.CreatedAt,
                    StartedAt = session.CreatedAt,
                    LastActivity = session.Stats.LastUpdate,
                    RecentNotifications = new List<DetectionNotification>(),
                    Performance = new PerformanceMetrics
                    {
                        AverageFPS = session.Stats.ActualProcessingFPS,
                        PeakFPS = session.Stats.ActualProcessingFPS,
                        AverageProcessingTime = session.GetAverageProcessingTime(),
                        TotalFramesProcessed = session.Stats.TotalFramesProcessed,
                        TotalFacesDetected = session.Stats.FacesDetected,
                        TotalHandsDetected = session.Stats.HandsDetected,
                        TotalTextCaptures = session.LastDetectedText != null ? 1 : 0,
                        CalculatedAt = DateTime.UtcNow
                    },
                    DetectedObjects = new List<string>()
                };
            }
            return null;
        }

        public void UpdateSessionSettings(string sessionId, DetectionSettings settings)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Settings = settings ?? new DetectionSettings();
                ApplyDetectionSettingsToMonitoringOptions(session);
                PersistSessionProfile(session, "default");
            }
        }

        /// <summary>
        /// Keeps <see cref="MonitoringConfiguration.EnabledOptions"/> in sync with <see cref="DetectionSettings"/>
        /// so API POST /settings actually enables/disables pipeline stages (IsMonitoringEnabled reads options, not Settings alone).
        /// </summary>
        private static void ApplyDetectionSettingsToMonitoringOptions(SessionData session)
        {
            if (session?.MonitoringConfig?.EnabledOptions == null || session.Settings == null)
                return;

            void SetOption(string name, bool enabled)
            {
                var opt = session.MonitoringConfig.EnabledOptions.FirstOrDefault(o => o.Name == name);
                if (opt != null)
                    opt.IsEnabled = enabled;
            }

            SetOption("FaceDetection", session.Settings.EnableFaceDetection);
            SetOption("EyeDetection", session.Settings.EnableEyeDetection);
            SetOption("HandDetection", session.Settings.EnableHandDetection);
            SetOption("MovementDetection", session.Settings.EnableMovementDetection);
            SetOption("TextDetection", session.Settings.EnableTextDetection);
            SetOption("CameraMovementAnalysis", session.Settings.EnableCameraMovementAnalysis);
        }

        public void InitializeSession(string sessionId)
        {
            InitializeSessionIfNotExists(sessionId);
        }

        public void CleanupSession(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                PersistSessionProfile(session, "default");
                SafeDispose(session.PreviousFrame);
                SafeDispose(session.PreviousGrayFrame);
            }
        }

        public List<string> GetActiveSessions()
        {
            return _sessions.Keys.ToList();
        }

        public DetectorHealthSnapshot GetDetectorHealth(string sessionId = null)
        {
            SessionData session = null;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                _sessions.TryGetValue(sessionId, out session);
            }

            var status = session?.LastSceneAnalysis?.ModelStatus;
            return new DetectorHealthSnapshot
            {
                SessionId = sessionId ?? string.Empty,
                TimestampUtc = DateTime.UtcNow,
                ActiveSessions = _sessions.Count,
                LastScenePipeline = session?.LastSceneAnalysis?.Pipeline ?? "unknown",
                LastSceneFrameMs = session?.LastSceneAnalysis?.FrameProcessingMs ?? 0,
                FaceCascadeReady = status?.FaceCascadeReady ?? (_yunetFaceNet != null),
                EyeCascadeReady = _yunetFaceNet != null,
                HandCascadeReady = true,
                SsdLoaded = status?.SsdLoaded ?? (_yoloV8Net != null),
                SsdLoadFailed = _yoloLoadFailed
            };
        }

        public DetectorDiagnosticsReport GetDetectorDiagnostics(string sessionId)
        {
            static ModuleTiming BuildTiming(Queue<double> q)
            {
                if (q == null || q.Count == 0) return new ModuleTiming();
                var arr = q.ToArray();
                var avg = arr.Average();
                return new ModuleTiming
                {
                    AvgMs = avg,
                    LastMs = arr.Last(),
                    Fps = avg > 0 ? Math.Min(1000.0 / avg, 999.0) : 0
                };
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return new DetectorDiagnosticsReport
                {
                    SessionId = "",
                    TimestampUtc = DateTime.UtcNow,
                    ActiveSessions = _sessions.Count,
                    Health = GetDetectorHealth(null),
                    InferenceProvider = "cpu",
                    SceneModelBackend = "yolo"
                };
            }

            _sessions.TryGetValue(sessionId, out var session);
            var lastScene = session?.LastSceneAnalysis;
            var entities = lastScene?.Entities ?? new List<SceneEntity>();

            return new DetectorDiagnosticsReport
            {
                SessionId = sessionId,
                TimestampUtc = DateTime.UtcNow,
                ActiveSessions = _sessions.Count,
                AverageTimings = session?.GetAverageStageTimings() ?? new ProcessingStageTimings(),
                LastTimings = session?.GetLastStageTiming() ?? new ProcessingStageTimings(),
                LastScenePipeline = lastScene?.Pipeline ?? "unknown",
                LastEntityBySource = entities
                    .GroupBy(e => string.IsNullOrWhiteSpace(e.Source) ? "unknown" : e.Source)
                    .ToDictionary(g => g.Key, g => g.Count()),
                LastEntityByCategory = entities
                    .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "unknown" : e.Category)
                    .ToDictionary(g => g.Key, g => g.Count()),
                Health = GetDetectorHealth(sessionId),
                ModulePerformance = new ModulePerformanceMetrics
                {
                    Face = BuildTiming(session?.FaceModuleMs ?? new Queue<double>()),
                    Hand = BuildTiming(session?.HandModuleMs ?? new Queue<double>()),
                    Scene = BuildTiming(session?.SceneModuleMs ?? new Queue<double>()),
                    Ocr = BuildTiming(session?.OcrModuleMs ?? new Queue<double>()),
                    DroppedFrames = session?.DroppedFrameCount ?? 0,
                    ScheduledSkips = session?.ScheduledSkipCount ?? 0,
                    QueueDrops = 0
                },
                RecentEvents = session?.RecentEvents?.ToList() ?? new List<IntelligenceEvent>(),
                InferenceProvider = session?.InferenceProvider ?? "cpu",
                SceneModelBackend = session?.Settings?.SceneModelBackend ?? "yolo"
            };
        }

        public MonitoringConfiguration GetMonitoringConfiguration(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                return session.MonitoringConfig;
            }
            return new MonitoringConfiguration { SessionId = sessionId };
        }

        public void UpdateMonitoringConfiguration(string sessionId, MonitoringConfiguration config)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.MonitoringConfig = config ?? new MonitoringConfiguration();

                // Update settings based on monitoring configuration
                if (session.MonitoringConfig.EnabledOptions != null)
                {
                    foreach (var option in session.MonitoringConfig.EnabledOptions)
                    {
                        switch (option.Name)
                        {
                            case "FaceDetection":
                                session.Settings.EnableFaceDetection = option.IsEnabled;
                                break;
                            case "EyeDetection":
                                session.Settings.EnableEyeDetection = option.IsEnabled;
                                break;
                            case "HandDetection":
                                session.Settings.EnableHandDetection = option.IsEnabled;
                                break;
                            case "MovementDetection":
                                session.Settings.EnableMovementDetection = option.IsEnabled;
                                break;
                            case "TextDetection":
                                session.Settings.EnableTextDetection = option.IsEnabled;
                                break;
                            case "CameraMovementAnalysis":
                                session.Settings.EnableCameraMovementAnalysis = option.IsEnabled;
                                break;
                        }
                    }
                }

                // Update frame rate control
                if (session.MonitoringConfig.FrameRateControl != null)
                {
                    session.Settings.TargetFPS = session.MonitoringConfig.FrameRateControl.TargetFPS;
                    session.Settings.EnableFrameRateControl = session.MonitoringConfig.FrameRateControl.AdaptiveMode;
                    session.FramesToSkip = session.MonitoringConfig.FrameRateControl.FrameSkip;
                }
            }
        }

        public FrameRateInfo GetFrameRateInfo(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                var stats = session.Stats;
                var avgProcessingTime = session.GetAverageProcessingTime();

                return new FrameRateInfo
                {
                    TargetFPS = stats.TargetFPS,
                    ActualFPS = stats.ActualProcessingFPS,
                    ProcessingTimeMs = avgProcessingTime,
                    IsOptimal = stats.ActualProcessingFPS >= stats.TargetFPS * 0.8,
                    Recommendation = GetFrameRateRecommendation(stats),
                    FrameDropRate = CalculateFrameDropRate(session),
                    LastUpdated = DateTime.UtcNow
                };
            }
            return new FrameRateInfo();
        }

        private double CalculateFrameDropRate(SessionData session)
        {
            if (session.Stats.TotalFramesProcessed == 0) return 0.0;

            var expectedFrames = (DateTime.UtcNow - session.CreatedAt).TotalSeconds * session.Settings.TargetFPS;
            var actualFrames = session.Stats.TotalFramesProcessed;

            return expectedFrames > 0 ? Math.Max(0, (expectedFrames - actualFrames) / expectedFrames) : 0.0;
        }

        private string GetFrameRateRecommendation(DetectionStats stats)
        {
            if (stats.ActualProcessingFPS >= stats.TargetFPS * 0.9)
                return "Optimal performance";
            if (stats.ActualProcessingFPS >= stats.TargetFPS * 0.7)
                return "Good performance";
            if (stats.ActualProcessingFPS >= stats.TargetFPS * 0.5)
                return "Consider reducing target FPS or disabling some features";
            return "Reduce target FPS or disable heavy processing features";
        }

        public CameraMovementAnalysis GetCameraMovementAnalysis(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                var stats = session.Stats;
                return new CameraMovementAnalysis
                {
                    MovementType = stats.CameraMovement,
                    StabilityScore = stats.CameraStability,
                    HorizontalMovement = stats.RecentMovements.Count > 0 ? stats.RecentMovements.Average(m => m.X) : 0,
                    VerticalMovement = stats.RecentMovements.Count > 0 ? stats.RecentMovements.Average(m => m.Y) : 0,
                    ZoomLevel = 0,
                    Status = GetCameraStatus(stats.CameraStability),
                    Recommendation = GetCameraRecommendation(stats.CameraMovement, stats.CameraStability),
                    RecentVectors = new List<MovementVector>(stats.RecentMovements),
                    AnalysisTime = DateTime.UtcNow
                };
            }
            return new CameraMovementAnalysis();
        }

        private string GetCameraStatus(double stability)
        {
            if (stability > 80) return "Very Stable";
            if (stability > 60) return "Stable";
            if (stability > 40) return "Moderate";
            if (stability > 20) return "Unstable";
            return "Very Unstable";
        }

        private string GetCameraRecommendation(CameraMovementType movement, double stability)
        {
            if (stability < 40) return "Use tripod or stabilize camera";
            if (movement == CameraMovementType.Shaking) return "Reduce camera shake";
            if (movement == CameraMovementType.FastPan) return "Slow down panning movements";
            return "Camera movement is good";
        }

        public List<MonitoringOption> GetAvailableMonitoringOptions()
        {
            return new List<MonitoringOption>(_availableMonitoringOptions);
        }

        public void SetTargetFPS(string sessionId, double targetFPS)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Settings.TargetFPS = Math.Max(0.5, Math.Min(100, targetFPS));
                session.Stats.TargetFPS = session.Settings.TargetFPS;

                // Update monitoring config as well
                if (session.MonitoringConfig.FrameRateControl != null)
                {
                    session.MonitoringConfig.FrameRateControl.TargetFPS = session.Settings.TargetFPS;
                }
            }
        }

        public void EnableMonitoringOption(string sessionId, string optionName, bool enable)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                var option = session.MonitoringConfig.EnabledOptions?.FirstOrDefault(o => o.Name == optionName);
                if (option != null)
                {
                    option.IsEnabled = enable;
                }

                UpdateMonitoringConfiguration(sessionId, session.MonitoringConfig);
            }
        }

        public CalibrationResult StartCalibration(string sessionId, int frameCount)
        {
            InitializeSessionIfNotExists(sessionId);
            var session = _sessions[sessionId];
            var calibrationFrames = Math.Max(20, Math.Min(180, frameCount));

            session.IsCalibrating = true;
            session.CalibrationFramesTotal = calibrationFrames;
            session.CalibrationFramesRemaining = calibrationFrames;
            session.BaselineMovementSum = 0.0;
            session.BaselineMovement = 5.0;
            session.CalibrationStartedAt = DateTime.UtcNow;
            PersistSessionProfile(session, "default");

            return new CalibrationResult
            {
                SessionId = sessionId,
                IsCalibrating = true,
                FramesRemaining = session.CalibrationFramesRemaining,
                TotalFrames = session.CalibrationFramesTotal,
                BaselineMovement = session.BaselineMovement,
                StartedAt = session.CalibrationStartedAt,
                Message = "Calibration started. Keep camera stable for best results."
            };
        }

        private void UpdateCalibration(SessionData session)
        {
            if (!session.IsCalibrating || session.CalibrationFramesRemaining <= 0)
            {
                return;
            }

            session.BaselineMovementSum += session.Stats.CurrentMovementLevel;
            session.CalibrationFramesRemaining--;

            if (session.CalibrationFramesRemaining <= 0)
            {
                session.IsCalibrating = false;
                session.BaselineMovement = session.BaselineMovementSum / Math.Max(1, session.CalibrationFramesTotal);
                session.Settings.MovementThreshold = Math.Max(8.0, session.BaselineMovement * 1.5);
                PersistSessionProfile(session, "default");
            }
        }

        public void SaveSessionProfile(string sessionId, string profileName)
        {
            InitializeSessionIfNotExists(sessionId);
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                var normalizedProfileName = string.IsNullOrWhiteSpace(profileName) ? "default" : profileName.Trim();
                PersistSessionProfile(session, normalizedProfileName);
                SetLastSelectedProfile(sessionId, normalizedProfileName);
            }
        }

        public bool LoadSessionProfile(string sessionId, string profileName)
        {
            InitializeSessionIfNotExists(sessionId);
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                var normalizedProfileName = string.IsNullOrWhiteSpace(profileName) ? "default" : profileName.Trim();
                var loaded = ApplyPersistedSessionProfile(sessionId, session, normalizedProfileName);
                if (loaded)
                {
                    SetLastSelectedProfile(sessionId, normalizedProfileName);
                }

                return loaded;
            }

            return false;
        }

        public List<string> GetSessionProfiles(string sessionId)
        {
            var safeSession = SanitizeKey(sessionId);
            if (!Directory.Exists(_sessionProfilesPath))
            {
                return new List<string>();
            }

            var prefix = $"{safeSession}__";
            return Directory
                .GetFiles(_sessionProfilesPath, $"{safeSession}__*.json")
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                .Select(name => name.Substring(prefix.Length))
                .OrderBy(name => name)
                .ToList();
        }

        private string GetSessionProfilePath(string sessionId, string profileName)
        {
            var safeSession = SanitizeKey(sessionId);
            var normalizedProfile = string.IsNullOrWhiteSpace(profileName) ? "default" : profileName;
            var safeProfile = SanitizeKey(normalizedProfile);
            return Path.Combine(_sessionProfilesPath, $"{safeSession}__{safeProfile}.json");
        }

        private string GetSessionProfileMetaPath(string sessionId)
        {
            var safeSession = SanitizeKey(sessionId);
            return Path.Combine(_sessionProfilesPath, $"{safeSession}.meta.json");
        }

        private static string SanitizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "default";
            }

            return new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        }

        private string GetLastSelectedProfile(string sessionId)
        {
            try
            {
                var metaPath = GetSessionProfileMetaPath(sessionId);
                if (!File.Exists(metaPath))
                {
                    return null;
                }

                var json = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize<SessionProfileMeta>(json);
                return string.IsNullOrWhiteSpace(meta?.LastSelectedProfile) ? null : meta.LastSelectedProfile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Session profile meta load failed for {sessionId}: {ex.Message}");
                return null;
            }
        }

        private void SetLastSelectedProfile(string sessionId, string profileName)
        {
            try
            {
                var meta = new SessionProfileMeta
                {
                    SessionId = sessionId,
                    LastSelectedProfile = string.IsNullOrWhiteSpace(profileName) ? "default" : profileName,
                    UpdatedAt = DateTime.UtcNow
                };
                var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetSessionProfileMetaPath(sessionId), json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Session profile meta save failed for {sessionId}: {ex.Message}");
            }
        }

        private bool ApplyPersistedSessionProfile(string sessionId, SessionData session, string profileName)
        {
            try
            {
                var profilePath = GetSessionProfilePath(sessionId, profileName);
                if (!File.Exists(profilePath))
                {
                    return false;
                }

                var json = File.ReadAllText(profilePath);
                var profile = JsonSerializer.Deserialize<SessionProfile>(json);
                if (profile == null)
                {
                    return false;
                }

                session.Settings = profile.Settings ?? session.Settings ?? new DetectionSettings();
                ApplyDetectionSettingsToMonitoringOptions(session);
                session.BaselineMovement = profile.BaselineMovement > 0 ? profile.BaselineMovement : session.BaselineMovement;
                session.IsCalibrating = false;
                session.CalibrationFramesTotal = 0;
                session.CalibrationFramesRemaining = 0;
                session.BaselineMovementSum = 0;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Session profile load failed for {sessionId}: {ex.Message}");
                return false;
            }
        }

        private void PersistSessionProfile(SessionData session, string profileName)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.SessionId))
            {
                return;
            }

            try
            {
                var profile = new SessionProfile
                {
                    SessionId = session.SessionId,
                    ProfileName = string.IsNullOrWhiteSpace(profileName) ? "default" : profileName,
                    Settings = session.Settings ?? new DetectionSettings(),
                    BaselineMovement = session.BaselineMovement,
                    UpdatedAt = DateTime.UtcNow
                };
                var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetSessionProfilePath(session.SessionId, profileName), json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Session profile save failed for {session.SessionId}: {ex.Message}");
            }
        }

        private sealed class SessionProfile
        {
            public string SessionId { get; set; }
            public string ProfileName { get; set; }
            public DetectionSettings Settings { get; set; }
            public double BaselineMovement { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private sealed class SessionProfileMeta
        {
            public string SessionId { get; set; }
            public string LastSelectedProfile { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private static readonly string[] FerPlusEmotionLabels =
        {
            "neutral", "happiness", "surprise", "sadness", "anger", "disgust", "fear", "contempt"
        };

        /// <summary>
        /// Real 8-class emotion classification via the FER+ ONNX model (CNTK export, raw logit
        /// output, softmax applied here). Replaces the old Haar smile/eye-count heuristic that
        /// used to stand in for "emotion" — smile is now just whatever "happiness" comes out to.
        /// </summary>
        private FaceExpression AnalyzeFaceEmotion(Mat bgrFrame, Rect faceRect)
        {
            var expression = new FaceExpression
            {
                DominantEmotion = "neutral",
                Confidence = 0.6,
                Emotions = FerPlusEmotionLabels.ToDictionary(l => l, l => l == "neutral" ? 0.6 : 0.4 / 7.0)
            };

            var emotionNet = GetOrLoadEmotionFerPlus();
            if (emotionNet == null)
                return expression;

            try
            {
                using var faceRegion = new Mat(bgrFrame, faceRect);
                using var gray = new Mat();
                Cv2.CvtColor(faceRegion, gray, ColorConversionCodes.BGR2GRAY);
                using var resized = new Mat();
                Cv2.Resize(gray, resized, new Size(64, 64));
                using var blob = CvDnn.BlobFromImage(resized);
                emotionNet.SetInput(blob);
                using var output = emotionNet.Forward();

                var logits = new float[FerPlusEmotionLabels.Length];
                System.Runtime.InteropServices.Marshal.Copy(output.Data, logits, 0, logits.Length);

                var maxLogit = logits.Max();
                var expValues = logits.Select(v => Math.Exp(v - maxLogit)).ToArray();
                var sum = Math.Max(expValues.Sum(), double.Epsilon);

                var probabilities = new Dictionary<string, double>();
                for (var i = 0; i < FerPlusEmotionLabels.Length; i++)
                    probabilities[FerPlusEmotionLabels[i]] = expValues[i] / sum;

                expression.Emotions = probabilities;
                expression.DominantEmotion = probabilities.OrderByDescending(kv => kv.Value).First().Key;
                expression.Confidence = probabilities[expression.DominantEmotion];
            }
            catch
            {
                // Keep neutral fallback expression
            }

            return expression;
        }

        private HandGesture InferGestureWithModel(HandDetection hand, bool useEnhancedPipeline = false)
        {
            if (useEnhancedPipeline && hand?.Landmarks?.Count >= 5)
            {
                return InferGestureFromLandmarkHeuristics(hand);
            }

            var width = Math.Max(1.0, hand.BBox.Width);
            var height = Math.Max(1.0, hand.BBox.Height);
            var area = width * height;
            var aspect = width / height;

            var logits = new Dictionary<string, double>
            {
                { "OpenPalm", 0.20 + 0.80 * (1.0 - Math.Abs(1.0 - aspect)) + 0.40 * Math.Min(1.0, area / 50000.0) },
                { "Pointing", 0.10 + 1.20 * Math.Max(0.0, aspect - 1.20) },
                { "RaisedHand", 0.15 + 0.60 * Math.Min(1.0, area / 30000.0) },
                { "Fist", 0.10 + 0.90 * Math.Max(0.0, 1.0 - (area / 18000.0)) }
            };

            var probabilities = Softmax(logits);
            var top = probabilities.OrderByDescending(kv => kv.Value).First();

            return new HandGesture
            {
                BBox = hand.BBox,
                Handedness = hand.Handedness ?? "Unknown",
                Type = top.Key,
                Confidence = Math.Clamp(top.Value, 0.1, 0.99),
                Meaning = top.Key switch
                {
                    "OpenPalm" => "Open hand detected",
                    "Pointing" => "Pointing-like hand pose",
                    "RaisedHand" => "Raised hand gesture",
                    _ => "Closed hand posture"
                }
            };
        }

        private HandGesture InferGestureFromLandmarkHeuristics(HandDetection hand)
        {
            var points = hand.Landmarks;
            var spreadX = points.Max(p => p.X) - points.Min(p => p.X);
            var spreadY = points.Max(p => p.Y) - points.Min(p => p.Y);
            var spreadScore = Math.Clamp((spreadX + spreadY) / 2.0, 0.0, 1.0);

            var type = spreadScore > 0.35 ? "OpenPalm" : "Fist";
            var confidence = Math.Clamp(0.55 + spreadScore * 0.35, 0.1, 0.99);

            return new HandGesture
            {
                BBox = hand.BBox,
                Handedness = hand.Handedness ?? "Unknown",
                Type = type,
                Confidence = confidence,
                Meaning = type == "OpenPalm" ? "Open hand detected (landmark fusion)" : "Closed hand detected (landmark fusion)",
                KeyPoints = new List<LandmarkPoint>(points)
            };
        }

        private void ApplyConfidenceFusion(
            DetectionData detectionData,
            List<FaceExpression> faceExpressions,
            List<HandGesture> handGestures,
            List<EyeMovement> eyeMovements)
        {
            foreach (var expression in faceExpressions)
            {
                var face = detectionData.Faces.FirstOrDefault(f => f.TrackId == expression.FaceId);
                if (face != null)
                {
                    expression.Confidence = Math.Clamp(expression.Confidence * 0.70 + face.Confidence * 0.30, 0.1, 0.99);
                }
            }

            for (var i = 0; i < handGestures.Count && i < detectionData.Hands.Count; i++)
            {
                handGestures[i].Confidence = Math.Clamp(
                    handGestures[i].Confidence * 0.75 + detectionData.Hands[i].Confidence * 0.25,
                    0.1,
                    0.99);
            }

            for (var i = 0; i < eyeMovements.Count && i < detectionData.Eyes.Count; i++)
            {
                eyeMovements[i].Confidence = Math.Clamp(
                    eyeMovements[i].Confidence * 0.70 + detectionData.Eyes[i].Confidence * 0.30,
                    0.1,
                    0.99);
                eyeMovements[i].GazeConfidence = eyeMovements[i].Confidence;
            }
        }

        private Dictionary<string, double> Softmax(Dictionary<string, double> logits)
        {
            var maxLogit = logits.Values.Max();
            var expValues = logits.ToDictionary(kv => kv.Key, kv => Math.Exp(kv.Value - maxLogit));
            var sum = expValues.Values.Sum();
            return expValues.ToDictionary(kv => kv.Key, kv => kv.Value / Math.Max(sum, double.Epsilon));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose cascade classifiers
                    _yoloV8Net?.Dispose();
                    _yunetFaceNet?.Dispose();
                    _emotionFerPlusNet?.Dispose();

                    // Dispose session resources
                    foreach (var session in _sessions.Values)
                    {
                        SafeDispose(session.PreviousFrame);
                        SafeDispose(session.PreviousGrayFrame);
                    }
                    _sessions.Clear();
                }
                _disposed = true;
            }
        }

        ~RealTimeDetectionService()
        {
            Dispose(false);
        }
    }
}
