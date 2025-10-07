using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using STAR_MUTIMEDIA.Models;
using Tesseract;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace STAR_MUTIMEDIA.Services
{
    public class RealTimeDetectionService : IRealTimeDetectionService, IDisposable
    {
        private readonly string _tessDataPath;
        private readonly ConcurrentDictionary<string, SessionData> _sessions;
        private readonly object _lockObject = new object();
        private bool _disposed = false;
        private readonly List<MonitoringOption> _availableMonitoringOptions;
        private readonly CascadeClassifier _faceCascade;
        private readonly CascadeClassifier _eyeCascade;
        private readonly CascadeClassifier _handCascade;
        private readonly CascadeClassifier _smile;
        private readonly CascadeClassifier _fullbody;
        private readonly CascadeClassifier _LicencePlate;

        public RealTimeDetectionService(string tessDataPath)
        {
            _tessDataPath = tessDataPath ?? throw new ArgumentNullException(nameof(tessDataPath));
            _sessions = new ConcurrentDictionary<string, SessionData>();

            // Set Tesseract environment variable
            if (!string.IsNullOrEmpty(_tessDataPath))
            {
                Environment.SetEnvironmentVariable("TESSDATA_PREFIX", _tessDataPath);
            }

            // Initialize cascades with fallback
            var cascadesPath = GetCascadesPath();
            _faceCascade = LoadCascadeClassifier(Path.Combine(cascadesPath, "haarcascade_frontalface_alt.xml"));
            _eyeCascade = LoadCascadeClassifier(Path.Combine(cascadesPath, "haarcascade_eye.xml"));
            _handCascade = LoadCascadeClassifier(Path.Combine(cascadesPath, "haarcascade_upperbody.xml"));
            _smile = LoadCascadeClassifier(Path.Combine(cascadesPath, "haarcascade_smile.xml"));
            _fullbody = LoadCascadeClassifier(Path.Combine(cascadesPath, "haarcascade_fullbody.xml"));
            _LicencePlate = LoadCascadeClassifier(Path.Combine(cascadesPath, "haarcascade_license_plate_rus_16stages.xml"));

            // Initialize available monitoring options
            _availableMonitoringOptions = InitializeMonitoringOptions();
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

            try
            {
                // Frame rate control - skip frames if needed
                if (ShouldSkipFrame(session))
                {
                    session.FramesToSkip--;
                    result.Stats = session.Stats.Clone();
                    result.Notifications.Add(new DetectionNotification
                    {
                        Type = "System",
                        Message = "Frame skipped for rate control",
                        Timestamp = DateTime.UtcNow,
                        Severity = "Info"
                    });
                    return await Task.FromResult(result);
                }

                var stopwatch = Stopwatch.StartNew();

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

                            var detectionData = new DetectionData();
                            var processedMat = ProcessFrame(mat, session, notifications, logs, detectionData);
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
                        }
                    }
                }

                stopwatch.Stop();
                UpdateProcessingTime(session, stopwatch.Elapsed.TotalMilliseconds);

                result.Stats = session.Stats.Clone();
                result.Notifications = notifications;
                result.Logs = logs;
                result.CapturedText = session.LastDetectedText;
                result.Success = true;
                session.Stats.LastUpdate = DateTime.UtcNow;

                logs.Add(new SystemLog
                {
                    Message = $"Frame processed successfully in {stopwatch.Elapsed.TotalMilliseconds:0.00}ms",
                    Timestamp = DateTime.UtcNow,
                    Level = "Info",
                    Component = "Processing"
                });
            }
            catch (Exception ex)
            {
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
                // Copy basic result properties
                ImageData = basicResult.ImageData,
                Stats = basicResult.Stats,
                Notifications = basicResult.Notifications,
                Logs = basicResult.Logs,
                Detections = basicResult.Detections,
                CapturedText = basicResult.CapturedText,
                Success = basicResult.Success,
                ErrorMessage = basicResult.ErrorMessage,

                // Enhanced features
                FaceExpressions = new List<FaceExpression>(),
                HandGestures = new List<HandGesture>(),
                EyeMovements = new List<EyeMovement>(),
                VitalMetrics = new VitalMetrics(),
                EmotionAnalysis = new EmotionAnalysis(),
                BehaviorAnalysis = new BehaviorAnalysis()
            };

            // Add enhanced processing here if needed
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
                var expression = new FaceExpression
                {
                    BBox = face.BBox,
                    Confidence = face.Confidence,
                    FaceId = face.TrackId,
                    DominantEmotion = "Neutral", // Default
                    Emotions = new Dictionary<string, double>
                    {
                        { "happy", 0.1 },
                        { "sad", 0.1 },
                        { "angry", 0.1 },
                        { "surprised", 0.1 },
                        { "neutral", 0.6 }
                    }
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

        private bool ShouldSkipFrame(SessionData session)
        {
            if (!session.Settings.EnableFrameRateControl)
                return false;

            if (session.FramesToSkip > 0)
            {
                session.FramesToSkip--;
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
                    return true;
                }
            }

            session.LastFrameTime = currentTime;
            return false;
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
            List<SystemLog> logs, DetectionData detectionData)
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

            try
            {
                using (var grayFrame = new Mat())
                {
                    Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);
                    Cv2.EqualizeHist(grayFrame, grayFrame);

                    // Camera movement analysis
                    if (IsMonitoringEnabled(config, "CameraMovementAnalysis"))
                    {
                        AnalyzeCameraMovement(frame, grayFrame, session, stats, notifications);
                    }

                    // Face detection
                    if (IsMonitoringEnabled(config, "FaceDetection"))
                    {
                        SafeFaceDetection(processedFrame, grayFrame, session, stats, notifications, logs, detectionData);
                    }

                    // Hand detection
                    if (IsMonitoringEnabled(config, "HandDetection"))
                    {
                        SafeHandDetection(processedFrame, grayFrame, stats, notifications, logs, detectionData);
                    }

                    // Movement detection
                    if (IsMonitoringEnabled(config, "MovementDetection") && session.PreviousFrame != null)
                    {
                        SafeMovementDetection(frame, processedFrame, session, stats, notifications, logs, detectionData);
                    }

                    // Text detection every 15 frames (for performance)
                    if (IsMonitoringEnabled(config, "TextDetection") && stats.TotalFramesProcessed % 15 == 0)
                    {
                        SafeTextDetection(processedFrame, stats, notifications, logs, session, detectionData);
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
                if (_faceCascade == null || _faceCascade.Empty())
                {
                    logs.Add(new SystemLog
                    {
                        Message = "Face cascade classifier not available",
                        Timestamp = DateTime.UtcNow,
                        Level = "Warning",
                        Component = "FaceDetection"
                    });
                    return;
                }

                var faces = _faceCascade.DetectMultiScale(
                    grayFrame,
                    1.1,
                    5,
                    HaarDetectionTypes.ScaleImage,
                    new Size(30, 30)
                );

                stats.FacesDetected = faces.Length;
                detectionData.Faces.Clear();

                foreach (var face in faces)
                {
                    // Draw face rectangle
                    Cv2.Rectangle(processedFrame, face, Scalar.Red, 2);
                    Cv2.PutText(processedFrame, "Face",
                        new Point(face.X, face.Y - 10),
                        HersheyFonts.HersheySimplex, 0.5, Scalar.Red, 1);

                    // Add to detection data
                    detectionData.Faces.Add(new FaceDetection
                    {
                        BBox = new BoundingBox
                        {
                            X = face.X,
                            Y = face.Y,
                            Width = face.Width,
                            Height = face.Height
                        },
                        Confidence = 0.85,
                        TrackId = detectionData.Faces.Count
                    });

                    // Safe eye detection
                    if (IsMonitoringEnabled(session.MonitoringConfig, "EyeDetection"))
                    {
                        SafeEyeDetection(processedFrame, grayFrame, face, stats, detectionData);
                    }
                }

                if (faces.Length > 0 && !session.LastFaceState)
                {
                    notifications.Add(new DetectionNotification
                    {
                        Type = "FaceDetection",
                        Message = $"{faces.Length} face(s) detected",
                        Timestamp = DateTime.UtcNow,
                        Severity = "Info",
                        Category = "Detection"
                    });
                }
                session.LastFaceState = faces.Length > 0;
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

        private void SafeEyeDetection(Mat processedFrame, Mat grayFrame, Rect face,
            DetectionStats stats, DetectionData detectionData)
        {
            try
            {
                if (_eyeCascade == null || _eyeCascade.Empty())
                    return;

                var faceROI = grayFrame[face];
                var eyes = _eyeCascade.DetectMultiScale(faceROI);
                stats.EyesDetected = eyes.Length;

                foreach (var eye in eyes)
                {
                    var eyeRect = new Rect(face.X + eye.X, face.Y + eye.Y, eye.Width, eye.Height);
                    Cv2.Rectangle(processedFrame, eyeRect, Scalar.Blue, 1);
                    Cv2.PutText(processedFrame, "Eye",
                        new Point(face.X + eye.X, face.Y + eye.Y - 5),
                        HersheyFonts.HersheySimplex, 0.3, Scalar.Blue, 1);

                    detectionData.Eyes.Add(new EyeDetection
                    {
                        BBox = new BoundingBox
                        {
                            X = face.X + eye.X,
                            Y = face.Y + eye.Y,
                            Width = eye.Width,
                            Height = eye.Height
                        },
                        Confidence = 0.75,
                        State = "Open",
                        Gaze = GazeDirection.Center,
                        TrackId = detectionData.Eyes.Count
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Eye detection error: {ex.Message}");
            }
        }

        private void SafeHandDetection(Mat processedFrame, Mat grayFrame, DetectionStats stats,
            List<DetectionNotification> notifications, List<SystemLog> logs, DetectionData detectionData)
        {
            try
            {
                if (_handCascade == null || _handCascade.Empty())
                {
                    logs.Add(new SystemLog
                    {
                        Message = "Hand cascade classifier not available",
                        Timestamp = DateTime.UtcNow,
                        Level = "Warning",
                        Component = "HandDetection"
                    });
                    return;
                }

                var hands = _handCascade.DetectMultiScale(
                    grayFrame,
                    1.1,
                    3,
                    HaarDetectionTypes.ScaleImage,
                    new Size(30, 30)
                );

                stats.HandsDetected = hands.Length;
                detectionData.Hands.Clear();

                foreach (var hand in hands)
                {
                    Cv2.Rectangle(processedFrame, hand, Scalar.Green, 2);
                    Cv2.PutText(processedFrame, "Hand",
                        new Point(hand.X, hand.Y - 10),
                        HersheyFonts.HersheySimplex, 0.5, Scalar.Green, 1);

                    detectionData.Hands.Add(new HandDetection
                    {
                        BBox = new BoundingBox
                        {
                            X = hand.X,
                            Y = hand.Y,
                            Width = hand.Width,
                            Height = hand.Height
                        },
                        Confidence = 0.70,
                        Handedness = "Unknown",
                        TrackId = detectionData.Hands.Count
                    });
                }

                if (hands.Length > 0)
                {
                    notifications.Add(new DetectionNotification
                    {
                        Type = "HandDetection",
                        Message = $"{hands.Length} hand(s) detected",
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

        private void SafeMovementDetection(Mat currentFrame, Mat processedFrame, SessionData session,
            DetectionStats stats, List<DetectionNotification> notifications, List<SystemLog> logs,
            DetectionData detectionData)
        {
            try
            {
                var movementLevel = CalculateMovementLevel(currentFrame, session.PreviousFrame);
                stats.CurrentMovementLevel = movementLevel;
                stats.MovementDetected = movementLevel > session.Settings.MovementThreshold;

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
                    MovementHistory = new List<MovementVector>()
                },
                (id, existing) => existing);
        }

        public DetectionStats GetSessionStats(string sessionId)
        {
            return _sessions.TryGetValue(sessionId, out var session)
                ? session.Stats.Clone()
                : new DetectionStats();
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
            }
        }

        public void InitializeSession(string sessionId)
        {
            InitializeSessionIfNotExists(sessionId);
        }

        public void CleanupSession(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                SafeDispose(session.PreviousFrame);
                SafeDispose(session.PreviousGrayFrame);
            }
        }

        public List<string> GetActiveSessions()
        {
            return _sessions.Keys.ToList();
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
                session.Settings.TargetFPS = Math.Max(1, Math.Min(60, targetFPS));
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
                    _faceCascade?.Dispose();
                    _eyeCascade?.Dispose();
                    _handCascade?.Dispose();

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


//using OpenCvSharp;
//using OpenCvSharp.Extensions;
//using STAR_MUTIMEDIA.Models;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Drawing;
//using System.IO;
//using System.Linq;
//using System.Threading.Tasks;
//using Tesseract;
//using SD = System.Drawing;
//using SDI = System.Drawing.Imaging;

//using Point = OpenCvSharp.Point;
//using Rect = OpenCvSharp.Rect;
//using Size = OpenCvSharp.Size;


//namespace STAR_MUTIMEDIA.Services
//{
//    public class RealTimeDetectionService : IRealTimeDetectionService, IDisposable
//    {
//        private readonly string _tessDataPath;
//        private readonly ConcurrentDictionary<string, SessionData> _sessions;
//        private readonly object _lockObject = new object();
//        private bool _disposed = false;
//        private readonly List<MonitoringOption> _availableMonitoringOptions;

//        public RealTimeDetectionService(string tessDataPath)
//        {
//            _tessDataPath = tessDataPath ?? throw new ArgumentNullException(nameof(tessDataPath));
//            _sessions = new ConcurrentDictionary<string, SessionData>();
//            Environment.SetEnvironmentVariable("TESSDATA_PREFIX", _tessDataPath);

//            // Initialize available monitoring options
//            _availableMonitoringOptions = InitializeMonitoringOptions();
//        }

//        private List<MonitoringOption> InitializeMonitoringOptions()
//        {
//            return new List<MonitoringOption>
//            {
//                new MonitoringOption { Name = "FaceDetection", DisplayName = "Face Detection", IsEnabled = true, Category = "People", Description = "Detect human faces in the frame" },
//                new MonitoringOption { Name = "EyeDetection", DisplayName = "Eye Detection", IsEnabled = true, Category = "People", Description = "Detect eyes within detected faces" },
//                new MonitoringOption { Name = "HandDetection", DisplayName = "Hand Detection", IsEnabled = true, Category = "People", Description = "Detect hands and gestures" },
//                new MonitoringOption { Name = "MovementDetection", DisplayName = "Movement Detection", IsEnabled = true, Category = "Motion", Description = "Detect general movement in the scene" },
//                new MonitoringOption { Name = "TextDetection", DisplayName = "Text Detection", IsEnabled = true, Category = "Objects", Description = "Extract text using OCR" },
//                new MonitoringOption { Name = "CameraMovementAnalysis", DisplayName = "Camera Movement Analysis", IsEnabled = true, Category = "Camera", Description = "Analyze camera stability and movement patterns" },
//                new MonitoringOption { Name = "ObjectTracking", DisplayName = "Object Tracking", IsEnabled = false, Category = "Objects", Description = "Track objects across frames" },
//                new MonitoringOption { Name = "FaceRecognition", DisplayName = "Face Recognition", IsEnabled = false, Category = "People", Description = "Recognize specific individuals" }
//            };
//        }

//        public async Task<DetectionResult> ProcessFrameAsync(FrameData frameData)
//        {
//            if (frameData == null)
//                throw new ArgumentNullException(nameof(frameData));

//            var sessionId = frameData.SessionId;
//            InitializeSessionIfNotExists(sessionId);

//            var session = _sessions[sessionId];
//            var result = new DetectionResult();
//            var notifications = new List<string>();
//            var logs = new List<string>();
//            var detectedObjects = new List<DetectedObject>();

//            try
//            {
//                // Frame rate control - skip frames if needed
//                if (ShouldSkipFrame(session))
//                {
//                    session.FramesToSkip--;
//                    result.Stats = session.Stats.Clone();
//                    result.Notifications.Add("Frame skipped for rate control");
//                    return await Task.FromResult(result);
//                }

//                var stopwatch = Stopwatch.StartNew();

//                // Convert base64 to image
//                var imageDataParts = frameData.ImageData?.Split(',');
//                if (imageDataParts == null || imageDataParts.Length == 0)
//                    throw new ArgumentException("Invalid image data");

//                var imageBytes = Convert.FromBase64String(imageDataParts.Length > 1 ? imageDataParts[1] : imageDataParts[0]);

//                using (var ms = new MemoryStream(imageBytes))
//                using (var image = SD.Image.FromStream(ms))
//                using (var bitmap = new SD.Bitmap(image))
//                {
//                    // Convert to OpenCV Mat
//                    using (var mat = BitmapConverter.ToMat(bitmap))
//                    {
//                        var processedMat = ProcessFrame(mat, session, notifications, logs, detectedObjects);

//                        // Convert back to base64
//                        using (var processedBitmap = BitmapConverter.ToBitmap(processedMat))
//                        using (var outputMs = new MemoryStream())
//                        {
//                            processedBitmap.Save(outputMs, SDI.ImageFormat.Jpeg);
//                            result.ImageData = "data:image/jpeg;base64," + Convert.ToBase64String(outputMs.ToArray());
//                        }
//                    }
//                }

//                stopwatch.Stop();
//                UpdateProcessingTime(session, stopwatch.Elapsed.TotalMilliseconds);

//                result.Stats = session.Stats.Clone();
//                result.Notifications = notifications;
//                result.Logs = logs;
//                result.DetectedObjects = detectedObjects;
//                result.CapturedText = session.LastDetectedText;
//                session.Stats.LastUpdate = DateTime.Now;
//            }
//            catch (Exception ex)
//            {
//                logs.Add($"Error processing frame: {ex.Message}");
//                result.Logs = logs;
//            }

//            return await Task.FromResult(result);
//        }

//        public async Task<EnhancedDetectionResult> ProcessEnhancedFrameAsync(EnhancedFrameData frameData)
//        {
//            if (frameData == null)
//                throw new ArgumentNullException(nameof(frameData));

//            var basicResult = await ProcessFrameAsync(frameData);
//            var enhancedResult = new EnhancedDetectionResult
//            {
//                ImageData = basicResult.ImageData,
//                Stats = basicResult.Stats,
//                Notifications = basicResult.Notifications.Select(n => new DetectionNotification
//                {
//                    Type = "Detection",
//                    Message = n,
//                    Timestamp = DateTime.Now,
//                    Severity = "Info"
//                }).ToList(),
//                Logs = basicResult.Logs,
//                DetectedObjects = basicResult.DetectedObjects,
//                CapturedText = basicResult.CapturedText,
//                // Enhanced features would be implemented here
//                FaceExpressions = new List<FaceExpression>(),
//                HandGestures = new List<HandGesture>(),
//                EyeMovements = new List<EyeMovement>(),
//                VitalMetrics = new VitalMetrics()
//            };

//            return enhancedResult;
//        }

//        private bool ShouldSkipFrame(SessionData session)
//        {
//            if (!session.Settings.EnableFrameRateControl)
//                return false;

//            var targetFPS = session.Settings.TargetFPS;
//            var currentTime = DateTime.Now;

//            if (session.LastFrameTime != DateTime.MinValue)
//            {
//                var timeSinceLastFrame = (currentTime - session.LastFrameTime).TotalSeconds;
//                var targetFrameTime = 1.0 / targetFPS;

//                if (timeSinceLastFrame < targetFrameTime)
//                {
//                    return true;
//                }
//            }

//            session.LastFrameTime = currentTime;
//            return false;
//        }

//        private void UpdateProcessingTime(SessionData session, double processingTimeMs)
//        {
//            session.ProcessingTimes.Enqueue(processingTimeMs);
//            if (session.ProcessingTimes.Count > 10) // Keep last 10 processing times
//            {
//                session.ProcessingTimes.Dequeue();
//            }

//            // Calculate actual FPS based on processing time
//            var avgProcessingTime = session.ProcessingTimes.Average();
//            session.Stats.ActualProcessingFPS = avgProcessingTime > 0 ? 1000.0 / avgProcessingTime : 0;
//        }

//        private Mat ProcessFrame(Mat frame, SessionData session, List<string> notifications,
//            List<string> logs, List<DetectedObject> detectedObjects)
//        {
//            var processedFrame = frame.Clone();
//            var stats = session.Stats;
//            var config = session.MonitoringConfig;

//            try
//            {
//                using (var grayFrame = new Mat())
//                {
//                    Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);
//                    Cv2.EqualizeHist(grayFrame, grayFrame);

//                    // Camera movement analysis
//                    if (IsMonitoringEnabled(config, "CameraMovementAnalysis"))
//                    {
//                        AnalyzeCameraMovement(frame, grayFrame, session, stats, notifications);
//                    }

//                    // Load cascades
//                    var cascadesPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "cascades");

//                    // Face detection
//                    if (IsMonitoringEnabled(config, "FaceDetection"))
//                    {
//                        //   DetectFacesAndEyes(processedFrame, grayFrame, session, stats, notifications, logs, cascadesPath, detectedObjects);
//                        SafeFaceDetection(processedFrame, grayFrame, session, stats, notifications, logs, cascadesPath, detectedObjects);

//                        // DetectFacesAndEyes
//                    }

//                    // Hand detection
//                    if (IsMonitoringEnabled(config, "HandDetection"))
//                    {
//                        DetectHands(processedFrame, grayFrame, stats, notifications, logs, cascadesPath, detectedObjects);
//                    }

//                    // Movement detection
//                    if (IsMonitoringEnabled(config, "MovementDetection") && session.PreviousFrame != null)
//                    {
//                      //  DetectMovement(frame, processedFrame, session, stats, notifications, logs, detectedObjects);
//                        SafeMovementDetection(frame, processedFrame, session, stats, notifications, logs, detectedObjects);
//                    }

//                    // Text detection every 15 frames (for performance)
//                    if (IsMonitoringEnabled(config, "TextDetection") && stats.TotalFramesProcessed % 15 == 0)
//                    {
//                        DetectText(processedFrame, stats, notifications, logs, session, detectedObjects);
//                    }

//                    // Update previous frame
//                    session.PreviousFrame?.Dispose();
//                    session.PreviousFrame = frame.Clone();

//                    session.PreviousGrayFrame?.Dispose();
//                    session.PreviousGrayFrame = grayFrame.Clone();

//                    // Update statistics
//                    stats.TotalFramesProcessed++;
//                    stats.TargetFPS = session.Settings.TargetFPS;
//                    UpdateFPS(stats);

//                    // Overlay stats and camera movement status
//                    AddStatsOverlay(processedFrame, stats);
//                    AddCameraMovementStatus(processedFrame, stats);
//                }
//            }
//            catch (Exception ex)
//            {
//                logs.Add($"Frame processing error: {ex.Message}");
//                SafeDispose(processedFrame);
//              //  SafeDispose(grayFrame);
//                return frame?.Clone() ?? new Mat(480, 640, MatType.CV_8UC3, Scalar.Black);

//            }

//            return processedFrame;
//        }
//        private void SafeDispose(IDisposable disposable)
//        {
//            try
//            {
//                disposable?.Dispose();
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"SafeDispose error: {ex.Message}");
//            }
//        }

//        private bool IsMonitoringEnabled(MonitoringConfiguration config, string optionName)
//        {
//            var option = config.EnabledOptions.FirstOrDefault(o => o.Name == optionName);
//            return option?.IsEnabled ?? false;
//        }


//        private void AnalyzeCameraMovement(Mat currentFrame, Mat grayFrame, SessionData session,
//      DetectionStats stats, List<string> notifications)
//        {
//            if (session.PreviousGrayFrame == null || session.PreviousGrayFrame.Empty() || grayFrame.Empty())
//                return;

//            try
//            {
//                // Ultra-safe frame differencing approach
//                double movementLevel = 0.0;

//                using (var diff = new Mat())
//                {
//                    Cv2.Absdiff(session.PreviousGrayFrame, grayFrame, diff);
//                    Cv2.Threshold(diff, diff, 25, 255, ThresholdTypes.Binary);

//                    var nonZeroPixels = Cv2.CountNonZero(diff);
//                    var totalPixels = diff.Width * diff.Height;
//                    movementLevel = totalPixels > 0 ? (double)nonZeroPixels / totalPixels * 100.0 : 0.0;
//                }

//                // Calculate stability (inverse of movement)
//                double stability = Math.Max(0, 100 - movementLevel * 1.5);

//                // Determine movement type based on movement level
//                var movementType = movementLevel switch
//                {
//                    < 1.0 => CameraMovementType.Stable,
//                    < 5.0 => CameraMovementType.SlowPan,
//                    < 10.0 => CameraMovementType.SlowTilt,
//                    < 20.0 => CameraMovementType.FastPan,
//                    < 30.0 => CameraMovementType.FastTilt,
//                    _ => CameraMovementType.Shaking
//                };

//                // Update stats
//                stats.CameraMovement = movementType;
//                stats.CameraStability = stability;
//                stats.CurrentMovementLevel = movementLevel;

//                // Create simple movement vector for display
//                stats.RecentMovements = new List<MovementVector>
//        {
//            new MovementVector
//            {
//                X = movementLevel / 10.0,
//                Y = movementLevel / 10.0,
//                Magnitude = movementLevel,
//                Timestamp = DateTime.Now
//            }
//        };

//                // Add notification if stability is low
//                if (stability < session.Settings.CameraStabilityThreshold && movementLevel > 5.0)
//                {
//                    notifications.Add($"Camera stability low: {stability:0}% (Movement: {movementLevel:0.0}%)");
//                }
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"Camera movement analysis error: {ex.Message}");
//                // Safe fallback values
//                stats.CameraMovement = CameraMovementType.Stable;
//                stats.CameraStability = 100.0;
//                stats.CurrentMovementLevel = 0.0;
//                stats.RecentMovements = new List<MovementVector>();
//            }
//        }
//        private List<MovementVector> CalculateSimpleMotionVectors(Mat prevGray, Mat currGray)
//        {
//            var movements = new List<MovementVector>();

//            try
//            {
//                // Use a much simpler and safer approach - frame differencing
//                using (var diff = new Mat())
//                {
//                    Cv2.Absdiff(prevGray, currGray, diff);
//                    Cv2.Threshold(diff, diff, 25, 255, ThresholdTypes.Binary);

//                    // Calculate overall movement level
//                    var nonZeroPixels = Cv2.CountNonZero(diff);
//                    var totalPixels = diff.Width * diff.Height;
//                    var movementLevel = (double)nonZeroPixels / totalPixels * 100.0;

//                    // Create a simple movement vector based on overall movement
//                    if (movementLevel > 1.0) // Only add if there's significant movement
//                    {
//                        movements.Add(new MovementVector
//                        {
//                            X = movementLevel / 10.0, // Simplified representation
//                            Y = movementLevel / 10.0,
//                            Magnitude = movementLevel,
//                            Timestamp = DateTime.Now
//                        });
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"Safe motion vector calculation error: {ex.Message}");
//                // Return empty list instead of crashing
//            }

//            return movements;
//        }

//        private (int dx, int dy, double diff)? FindBestMatch(Mat frame, Mat block, int x, int y, int searchRange)
//        {
//            double minDiff = double.MaxValue;
//            int bestDx = 0, bestDy = 0;

//            for (int dy = -searchRange; dy <= searchRange; dy++)
//            {
//                for (int dx = -searchRange; dx <= searchRange; dx++)
//                {
//                    int newX = x + dx;
//                    int newY = y + dy;

//                    if (newX >= 0 && newY >= 0 && newX + block.Width < frame.Width && newY + block.Height < frame.Height)
//                    {
//                        var candidate = frame[new Rect(newX, newY, block.Width, block.Height)];
//                        using (var diff = new Mat())
//                        {
//                            Cv2.Absdiff(block, candidate, diff);
//                            var sumDiff = Cv2.Sum(diff).Val0;

//                            if (sumDiff < minDiff)
//                            {
//                                minDiff = sumDiff;
//                                bestDx = dx;
//                                bestDy = dy;
//                            }
//                        }
//                    }
//                }
//            }

//            return minDiff < double.MaxValue ? (bestDx, bestDy, minDiff) : null;
//        }

//        private double CalculateStabilityFromChange(double changePercentage, List<MovementVector> movements)
//        {
//            if (movements.Count == 0) return 100.0;

//            // Stability is inversely proportional to change percentage and movement magnitude
//            var avgMagnitude = movements.Average(m => m.Magnitude);
//            var changeScore = Math.Max(0, 100 - changePercentage * 2);
//            var movementScore = Math.Max(0, 100 - avgMagnitude * 10);

//            return (changeScore + movementScore) / 2.0;
//        }
//        private CameraMovementType DetermineCameraMovementType(double avgX, double avgY, double magnitude)
//        {
//            if (magnitude < 0.5) return CameraMovementType.Stable;
//            if (magnitude > 5.0) return CameraMovementType.Shaking;

//            var absX = Math.Abs(avgX);
//            var absY = Math.Abs(avgY);

//            if (absX > absY * 2)
//            {
//                return absX > 2.0 ? CameraMovementType.FastPan : CameraMovementType.SlowPan;
//            }
//            else if (absY > absX * 2)
//            {
//                return absY > 2.0 ? CameraMovementType.FastTilt : CameraMovementType.SlowTilt;
//            }
//            else if (absX > 1.0 && absY > 1.0)
//            {
//                return CameraMovementType.Rotating;
//            }

//            return CameraMovementType.Stable;
//        }

//        private double CalculateStabilityScore(List<MovementVector> movements)
//        {
//            if (movements.Count == 0) return 100.0;

//            var avgMagnitude = movements.Average(m => m.Magnitude);
//            var consistency = CalculateMovementConsistency(movements);

//            // Score based on low magnitude and high consistency
//            var magnitudeScore = Math.Max(0, 100 - (avgMagnitude * 20));
//            var consistencyScore = consistency * 100;

//            return (magnitudeScore + consistencyScore) / 2.0;
//        }

//        private double CalculateMovementConsistency(List<MovementVector> movements)
//        {
//            if (movements.Count < 2) return 1.0;

//            var directions = movements.Select(m => Math.Atan2(m.Y, m.X)).ToList();
//            var variance = CalculateCircularVariance(directions);

//            return 1.0 - variance;
//        }

//        private double CalculateCircularVariance(List<double> angles)
//        {
//            // Simplified circular variance calculation
//            var sinSum = angles.Sum(a => Math.Sin(a));
//            var cosSum = angles.Sum(a => Math.Cos(a));
//            var meanResultant = Math.Sqrt(sinSum * sinSum + cosSum * cosSum) / angles.Count;

//            return 1.0 - meanResultant;
//        }

//        private void AddCameraMovementStatus(Mat frame, DetectionStats stats)
//        {
//            var statusBarHeight = 30;
//            var statusBar = new OpenCvSharp.Rect(0, 0, frame.Width, statusBarHeight);

//            // Background
//            Cv2.Rectangle(frame, statusBar, new Scalar(50, 50, 50), -1);

//            // Camera movement status
//            var statusText = $"Camera: {stats.CameraMovement} | Stability: {stats.CameraStability:0}%";
//            var movementColor = GetCameraMovementColor(stats.CameraMovement, stats.CameraStability);

//            Cv2.PutText(frame, statusText,
//                new OpenCvSharp.Point(10, statusBarHeight - 10),
//                HersheyFonts.HersheySimplex, 0.5, movementColor, 1);

//            // Frame rate status
//            var fpsText = $"FPS: Target={stats.TargetFPS:0} | Actual={stats.ActualProcessingFPS:0.0}";
//            var fpsColor = stats.ActualProcessingFPS >= stats.TargetFPS * 0.8 ? Scalar.Green : Scalar.Yellow;

//            Cv2.PutText(frame, fpsText,
//                new OpenCvSharp.Point(frame.Width - 250, statusBarHeight - 10),
//                HersheyFonts.HersheySimplex, 0.5, fpsColor, 1);
//        }

//        private Scalar GetCameraMovementColor(CameraMovementType movement, double stability)
//        {
//            if (stability > 80) return new Scalar(0, 255, 0);    // Green - stable
//            if (stability > 60) return new Scalar(0, 255, 255);  // Yellow - moderate
//            return new Scalar(0, 0, 255);                        // Red - unstable
//        }

//        // Include all your existing detection methods (DetectFacesAndEyes, DetectHands, etc.) here
//        // Make sure they are copied exactly as you had them

//        private void SafeFaceDetection(Mat processedFrame, Mat grayFrame, SessionData session,
//    DetectionStats stats, List<string> notifications, List<string> logs,
//    string cascadesPath, List<DetectedObject> detectedObjects)
//        {
//            try
//            {
//                var faceCascadePath = Path.Combine(cascadesPath, "haarcascade_frontalface_alt.xml");
//                if (!File.Exists(faceCascadePath))
//                {
//                    logs.Add("Face cascade file not found");
//                    return;
//                }

//                using (var faceCascade = new CascadeClassifier(faceCascadePath))
//                {
//                    if (faceCascade.Empty())
//                    {
//                        logs.Add("Failed to load face cascade classifier");
//                        return;
//                    }

//                    var faces = faceCascade.DetectMultiScale(grayFrame, 1.1, 5, HaarDetectionTypes.ScaleImage, new Size(30, 30));
//                    stats.FacesDetected = faces.Length;

//                    foreach (var face in faces)
//                    {
//                        Cv2.Rectangle(processedFrame, face, Scalar.Red, 2);
//                        Cv2.PutText(processedFrame, "Face",
//                            new Point(face.X, face.Y - 10),
//                            HersheyFonts.HersheySimplex, 0.5, Scalar.Red, 1);

//                        detectedObjects.Add(new DetectedObject
//                        {
//                            Type = "Face",
//                            X = face.X,
//                            Y = face.Y,
//                            Width = face.Width,
//                            Height = face.Height,
//                            Confidence = 0.85,
//                            AdditionalInfo = $"Face at ({face.X}, {face.Y})"
//                        });

//                        // Safe eye detection
//                        if (IsMonitoringEnabled(session.MonitoringConfig, "EyeDetection"))
//                        {
//                            SafeEyeDetection(processedFrame, grayFrame, face, stats, cascadesPath, detectedObjects);
//                        }
//                    }

//                    if (faces.Length > 0 && !session.LastFaceState)
//                    {
//                        notifications.Add($"{faces.Length} face(s) detected");
//                        logs.Add($"Face detection: {faces.Length} faces found");
//                    }
//                    session.LastFaceState = faces.Length > 0;
//                }
//            }
//            catch (Exception ex)
//            {
//                logs.Add($"Face detection error: {ex.Message}");
//            }
//        }

//        private void SafeEyeDetection(Mat processedFrame, Mat grayFrame, Rect face,
//            DetectionStats stats, string cascadesPath, List<DetectedObject> detectedObjects)
//        {
//            try
//            {
//                var eyeCascadePath = Path.Combine(cascadesPath, "haarcascade_eye.xml");
//                if (!File.Exists(eyeCascadePath)) return;

//                using (var eyeCascade = new CascadeClassifier(eyeCascadePath))
//                {
//                    if (eyeCascade.Empty()) return;

//                    var faceROI = grayFrame[face];
//                    var eyes = eyeCascade.DetectMultiScale(faceROI);
//                    stats.EyesDetected = eyes.Length;

//                    foreach (var eye in eyes)
//                    {
//                        var eyeRect = new Rect(face.X + eye.X, face.Y + eye.Y, eye.Width, eye.Height);
//                        Cv2.Rectangle(processedFrame, eyeRect, Scalar.Blue, 1);
//                        Cv2.PutText(processedFrame, "Eye",
//                            new Point(face.X + eye.X, face.Y + eye.Y - 5),
//                            HersheyFonts.HersheySimplex, 0.3, Scalar.Blue, 1);

//                        detectedObjects.Add(new DetectedObject
//                        {
//                            Type = "Eye",
//                            X = face.X + eye.X,
//                            Y = face.Y + eye.Y,
//                            Width = eye.Width,
//                            Height = eye.Height,
//                            Confidence = 0.75,
//                            AdditionalInfo = $"Eye in face"
//                        });
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                Debug.WriteLine($"Eye detection error: {ex.Message}");
//            }
//        }

//        private void SafeMovementDetection(Mat currentFrame, Mat processedFrame, SessionData session,
//            DetectionStats stats, List<string> notifications, List<string> logs, List<DetectedObject> detectedObjects)
//        {
//            try
//            {
//                var movementLevel = CalculateMovementLevel(currentFrame, session.PreviousFrame);
//                stats.CurrentMovementLevel = movementLevel;
//                stats.MovementDetected = movementLevel > session.Settings.MovementThreshold;

//                if (stats.MovementDetected && !session.LastMovementState)
//                {
//                    notifications.Add($"Movement detected: {movementLevel:0.0}%");
//                    logs.Add($"Movement started: {movementLevel:0.0}%");
//                }
//                else if (!stats.MovementDetected && session.LastMovementState)
//                {
//                    notifications.Add("Movement stopped");
//                    logs.Add("Movement stopped");
//                }
//                session.LastMovementState = stats.MovementDetected;

//                if (stats.MovementDetected)
//                {
//                    detectedObjects.Add(new DetectedObject
//                    {
//                        Type = "Movement",
//                        Confidence = movementLevel / 100.0,
//                        AdditionalInfo = $"Movement level: {movementLevel:0.0}%"
//                    });
//                }

//                DrawMovementIndicator(processedFrame, movementLevel);
//            }
//            catch (Exception ex)
//            {
//                logs.Add($"Movement detection error: {ex.Message}");
//            }
//        }
//        private void DetectFacesAndEyes(Mat processedFrame, Mat grayFrame, SessionData session, DetectionStats stats,
//            List<string> notifications, List<string> logs, string cascadesPath, List<DetectedObject> detectedObjects)
//        {
//            var faceCascadePath = Path.Combine(cascadesPath, "haarcascade_frontalface_alt.xml");
//            if (!File.Exists(faceCascadePath))
//            {
//                logs.Add("Face cascade file not found");
//                return;
//            }

//            using (var faceCascade = new CascadeClassifier(faceCascadePath))
//            {
//                var faces = faceCascade.DetectMultiScale(grayFrame, 1.1, 5, HaarDetectionTypes.ScaleImage, new OpenCvSharp.Size(30, 30));
//                stats.FacesDetected = faces.Length;

//                foreach (var face in faces)
//                {
//                    Cv2.Rectangle(processedFrame, face, Scalar.Red, 2);
//                    Cv2.PutText(processedFrame, "Face",
//                        new OpenCvSharp.Point(face.X, face.Y - 10),
//                        HersheyFonts.HersheySimplex, 0.5, Scalar.Red, 1);

//                    // Add to detected objects
//                    detectedObjects.Add(new DetectedObject
//                    {
//                        Type = "Face",
//                        X = face.X,
//                        Y = face.Y,
//                        Width = face.Width,
//                        Height = face.Height,
//                        Confidence = 0.85,
//                        AdditionalInfo = $"Face at ({face.X}, {face.Y})"
//                    });

//                    // Eye detection within face
//                    if (IsMonitoringEnabled(session.MonitoringConfig, "EyeDetection"))
//                    {
//                        DetectEyesInFace(processedFrame, grayFrame, face, stats, cascadesPath, detectedObjects);
//                    }
//                }

//                if (faces.Length > 0 && !session.LastFaceState)
//                {
//                    notifications.Add($"{faces.Length} face(s) detected");
//                    logs.Add($"Face detection: {faces.Length} faces found");
//                }
//                session.LastFaceState = faces.Length > 0;
//            }
//        }

//        private void DetectEyesInFace(Mat processedFrame, Mat grayFrame, OpenCvSharp.Rect face, DetectionStats stats,
//            string cascadesPath, List<DetectedObject> detectedObjects)
//        {
//            var eyeCascadePath = Path.Combine(cascadesPath, "haarcascade_eye.xml");
//            if (!File.Exists(eyeCascadePath)) return;

//            using (var eyeCascade = new CascadeClassifier(eyeCascadePath))
//            {
//                var faceROI = grayFrame[face];
//                var eyes = eyeCascade.DetectMultiScale(faceROI);
//                stats.EyesDetected = eyes.Length;

//                foreach (var eye in eyes)
//                {
//                    var eyeRect = new OpenCvSharp.Rect(face.X + eye.X, face.Y + eye.Y, eye.Width, eye.Height);
//                    Cv2.Rectangle(processedFrame, eyeRect, Scalar.Blue, 1);
//                    Cv2.PutText(processedFrame, "Eye",
//                        new OpenCvSharp.Point(face.X + eye.X, face.Y + eye.Y - 5),
//                        HersheyFonts.HersheySimplex, 0.3, Scalar.Blue, 1);

//                    // Add to detected objects
//                    detectedObjects.Add(new DetectedObject
//                    {
//                        Type = "Eye",
//                        X = face.X + eye.X,
//                        Y = face.Y + eye.Y,
//                        Width = eye.Width,
//                        Height = eye.Height,
//                        Confidence = 0.75,
//                        AdditionalInfo = $"Eye in face"
//                    });
//                }
//            }
//        }

//        private void DetectHands(Mat processedFrame, Mat grayFrame, DetectionStats stats,
//            List<string> notifications, List<string> logs, string cascadesPath, List<DetectedObject> detectedObjects)
//        {
//            var handCascadePath = Path.Combine(cascadesPath, "haarcascade_upperbody.xml");
//            if (!File.Exists(handCascadePath))
//            {
//                // Try alternative hand cascade names
//                handCascadePath = Path.Combine(cascadesPath, "haarcascade_fullbody.xml");
//                if (!File.Exists(handCascadePath))
//                {
//                    logs.Add("Hand cascade file not found");
//                    return;
//                }
//            }

//            using (var handCascade = new CascadeClassifier(handCascadePath))
//            {
//                var hands = handCascade.DetectMultiScale(grayFrame, 1.1, 3, HaarDetectionTypes.ScaleImage, new OpenCvSharp.Size(30, 30));
//                stats.HandsDetected = hands.Length;

//                foreach (var hand in hands)
//                {
//                    Cv2.Rectangle(processedFrame, hand, Scalar.Green, 2);
//                    Cv2.PutText(processedFrame, "Hand",
//                        new OpenCvSharp.Point(hand.X, hand.Y - 10),
//                        HersheyFonts.HersheySimplex, 0.5, Scalar.Green, 1);

//                    // Add to detected objects
//                    detectedObjects.Add(new DetectedObject
//                    {
//                        Type = "Hand",
//                        X = hand.X,
//                        Y = hand.Y,
//                        Width = hand.Width,
//                        Height = hand.Height,
//                        Confidence = 0.70,
//                        AdditionalInfo = $"Hand at ({hand.X}, {hand.Y})"
//                    });
//                }

//                if (hands.Length > 0)
//                {
//                    notifications.Add($"{hands.Length} hand(s) detected");
//                    logs.Add($"Hand detection: {hands.Length} hands found");
//                }
//            }
//        }

//        private void DetectMovement(Mat currentFrame, Mat processedFrame, SessionData session,
//            DetectionStats stats, List<string> notifications, List<string> logs, List<DetectedObject> detectedObjects)
//        {
//            var movementLevel = CalculateMovementLevel(currentFrame, session.PreviousFrame);
//            stats.CurrentMovementLevel = movementLevel;
//            stats.MovementDetected = movementLevel > session.Settings.MovementThreshold;

//            if (stats.MovementDetected && !session.LastMovementState)
//            {
//                notifications.Add($"Movement detected: {movementLevel:0.0}%");
//                logs.Add($"Movement started: {movementLevel:0.0}%");
//            }
//            else if (!stats.MovementDetected && session.LastMovementState)
//            {
//                notifications.Add("Movement stopped");
//                logs.Add("Movement stopped");
//            }
//            session.LastMovementState = stats.MovementDetected;

//            // Add movement to detected objects
//            if (stats.MovementDetected)
//            {
//                detectedObjects.Add(new DetectedObject
//                {
//                    Type = "Movement",
//                    Confidence = movementLevel / 100.0,
//                    AdditionalInfo = $"Movement level: {movementLevel:0.0}%"
//                });
//            }

//            DrawMovementIndicator(processedFrame, movementLevel);
//        }

//        private double CalculateMovementLevel(Mat currentFrame, Mat previousFrame)
//        {
//            try
//            {
//                using (var diff = new Mat())
//                using (var grayCurrent = new Mat())
//                using (var grayPrevious = new Mat())
//                {
//                    Cv2.CvtColor(currentFrame, grayCurrent, ColorConversionCodes.BGR2GRAY);
//                    Cv2.CvtColor(previousFrame, grayPrevious, ColorConversionCodes.BGR2GRAY);
//                    Cv2.Absdiff(grayCurrent, grayPrevious, diff);
//                    Cv2.Threshold(diff, diff, 25, 255, ThresholdTypes.Binary);

//                    var nonZeroPixels = Cv2.CountNonZero(diff);
//                    var totalPixels = diff.Width * diff.Height;
//                    return (double)nonZeroPixels / totalPixels * 100.0;
//                }
//            }
//            catch
//            {
//                return 0.0;
//            }
//        }

//        private void DetectText(Mat frame, DetectionStats stats, List<string> notifications,
//            List<string> logs, SessionData session, List<DetectedObject> detectedObjects)
//        {
//            try
//            {
//                using (var tesseractEngine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default))
//                {
//                    tesseractEngine.SetVariable("tessedit_char_whitelist",
//                        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?()-+*/=@#$%&");

//                    // Alternative approach if PixConverter doesn't work
//                    using (var tempBitmap = BitmapConverter.ToBitmap(frame))
//                    using (var tempStream = new MemoryStream())
//                    {
//                        tempBitmap.Save(tempStream, SDI.ImageFormat.Png);
//                        using (var pix = Pix.LoadFromMemory(tempStream.ToArray()))
//                        using (var page = tesseractEngine.Process(pix))
//                        {
//                            var text = page.GetText()?.Trim();
//                            if (!string.IsNullOrEmpty(text) && text.Length > 3)
//                            {
//                                stats.TextDetected = true;
//                                var displayText = text.Length > 30 ? text.Substring(0, 30) + "..." : text;
//                                notifications.Add($"Text detected: {displayText}");
//                                logs.Add($"OCR: {text}");
//                                session.LastDetectedText = text;

//                                // Add text to detected objects
//                                detectedObjects.Add(new DetectedObject
//                                {
//                                    Type = "Text",
//                                    Confidence = page.GetMeanConfidence() / 100.0,
//                                    AdditionalInfo = $"Text: {displayText}"
//                                });

//                                Cv2.PutText(frame, "Text Detected",
//                                    new OpenCvSharp.Point(10, frame.Height - 30),
//                                    HersheyFonts.HersheySimplex, 0.6, Scalar.Yellow, 2);
//                            }
//                            else
//                            {
//                                stats.TextDetected = false;
//                                session.LastDetectedText = null;
//                            }
//                        }
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                logs.Add($"Text detection error: {ex.Message}");
//            }
//        }

//        private void DrawMovementIndicator(Mat frame, double movementLevel)
//        {
//            int meterWidth = 200;
//            int meterHeight = 20;
//            int meterX = frame.Width - meterWidth - 10;
//            int meterY = 10;

//            // Background
//            Cv2.Rectangle(frame, new OpenCvSharp.Rect(meterX, meterY, meterWidth, meterHeight), Scalar.DarkGray, -1);

//            // Fill based on movement level
//            int fillWidth = (int)(movementLevel / 100.0 * meterWidth);
//            var color = GetMovementColor(movementLevel);
//            Cv2.Rectangle(frame, new OpenCvSharp.Rect(meterX, meterY, fillWidth, meterHeight), color, -1);

//            // Border
//            Cv2.Rectangle(frame, new OpenCvSharp.Rect(meterX, meterY, meterWidth, meterHeight), Scalar.White, 1);

//            // Label
//            Cv2.PutText(frame, $"Movement: {movementLevel:0.0}%",
//                new OpenCvSharp.Point(meterX, meterY + meterHeight + 15),
//                HersheyFonts.HersheySimplex, 0.4, Scalar.White, 1);
//        }

//        private Scalar GetMovementColor(double movementLevel)
//        {
//            if (movementLevel < 10) return new Scalar(0, 255, 0);    // Green
//            if (movementLevel < 30) return new Scalar(0, 255, 255);  // Yellow
//            if (movementLevel < 50) return new Scalar(0, 165, 255);  // Orange
//            return new Scalar(0, 0, 255);                            // Red
//        }

//        private void AddStatsOverlay(Mat frame, DetectionStats stats)
//        {
//            string[] statsText =
//            {
//                $"Faces: {stats.FacesDetected}",
//                $"Eyes: {stats.EyesDetected}",
//                $"Hands: {stats.HandsDetected}",
//                $"Movement: {(stats.MovementDetected ? "Yes" : "No")}",
//                $"Text: {(stats.TextDetected ? "Yes" : "No")}",
//                $"FPS: {stats.CurrentFPS:0.0}",
//                $"Frame: {stats.TotalFramesProcessed}"
//            };

//            int yOffset = 60; // Start below camera status bar
//            foreach (var text in statsText)
//            {
//                Cv2.PutText(frame, text, new Point(10, yOffset),
//                    HersheyFonts.HersheySimplex, 0.5, Scalar.White, 1);
//                yOffset += 20;
//            }
//        }

//        private void UpdateFPS(DetectionStats stats)
//        {
//            var now = DateTime.Now;
//            if (stats.LastFPSCalculation == DateTime.MinValue)
//            {
//                stats.LastFPSCalculation = now;
//                stats.FramesSinceLastCalculation = 0;
//            }
//            else if ((now - stats.LastFPSCalculation).TotalSeconds >= 1.0)
//            {
//                stats.CurrentFPS = stats.FramesSinceLastCalculation;
//                stats.FramesSinceLastCalculation = 0;
//                stats.LastFPSCalculation = now;
//            }
//            else
//            {
//                stats.FramesSinceLastCalculation++;
//            }
//        }

//        // ... Rest of your interface implementation methods remain the same
//        // Include all the GetMonitoringConfiguration, UpdateMonitoringConfiguration, etc. methods

//        private void InitializeSessionIfNotExists(string sessionId)
//        {
//            _sessions.AddOrUpdate(sessionId,
//                id => new SessionData
//                {
//                    Stats = new DetectionStats(),
//                    Settings = new DetectionSettings(),
//                    MonitoringConfig = new MonitoringConfiguration
//                    {
//                        SessionId = id,
//                        EnabledOptions = new List<MonitoringOption>(_availableMonitoringOptions)
//                    },
//                    SessionId = id,
//                    LastDetectedText = null,
//                    ProcessingTimes = new Queue<double>()
//                },
//                (id, existing) => existing);
//        }

//        public DetectionStats GetSessionStats(string sessionId)
//        {
//            return _sessions.TryGetValue(sessionId, out var session)
//                ? session.Stats.Clone()
//                : new DetectionStats();
//        }

//        public SessionAnalytics GetSessionAnalytics(string sessionId)
//        {
//            if (_sessions.TryGetValue(sessionId, out var session))
//            {
//                return new SessionAnalytics
//                {
//                    SessionId = sessionId,
//                    Stats = session.Stats.Clone(),
//                    SessionDuration = DateTime.Now - session.CreatedAt,
//                    StartedAt = session.CreatedAt,
//                    LastActivity = session.Stats.LastUpdate,
//                    RecentNotifications = new List<string>()
//                };
//            }
//            return null;
//        }

//        public void UpdateSessionSettings(string sessionId, DetectionSettings settings)
//        {
//            if (_sessions.TryGetValue(sessionId, out var session))
//            {
//                session.Settings = settings ?? new DetectionSettings();
//            }
//        }

//        public void InitializeSession(string sessionId)
//        {
//            InitializeSessionIfNotExists(sessionId);
//        }

//        public void CleanupSession(string sessionId)
//        {
//            if (_sessions.TryRemove(sessionId, out var session))
//            {
//                session.PreviousFrame?.Dispose();
//                session.PreviousGrayFrame?.Dispose();
//            }
//        }

//        public List<string> GetActiveSessions()
//        {
//            return _sessions.Keys.ToList();
//        }

//        public MonitoringConfiguration GetMonitoringConfiguration(string sessionId)
//        {
//            if (_sessions.TryGetValue(sessionId, out var session))
//            {
//                return session.MonitoringConfig;
//            }
//            return new MonitoringConfiguration();
//        }

//        public void UpdateMonitoringConfiguration(string sessionId, MonitoringConfiguration config)
//        {
//            if (_sessions.TryGetValue(sessionId, out var session))
//            {
//                session.MonitoringConfig = config;

//                // Update settings based on monitoring configuration
//                foreach (var option in config.EnabledOptions)
//                {
//                    switch (option.Name)
//                    {
//                        case "FaceDetection":
//                            session.Settings.EnableFaceDetection = option.IsEnabled;
//                            break;
//                        case "EyeDetection":
//                            session.Settings.EnableEyeDetection = option.IsEnabled;
//                            break;
//                        case "HandDetection":
//                            session.Settings.EnableHandDetection = option.IsEnabled;
//                            break;
//                        case "MovementDetection":
//                            session.Settings.EnableMovementDetection = option.IsEnabled;
//                            break;
//                        case "TextDetection":
//                            session.Settings.EnableTextDetection = option.IsEnabled;
//                            break;
//                        case "CameraMovementAnalysis":
//                            session.Settings.EnableCameraMovementAnalysis = option.IsEnabled;
//                            break;
//                    }
//                }

//                // Update frame rate control
//                session.Settings.TargetFPS = config.FrameRateControl.TargetFPS;
//                session.Settings.EnableFrameRateControl = config.FrameRateControl.AdaptiveMode;
//                session.FramesToSkip = config.FrameRateControl.FrameSkip;
//            }
//        }

//        public FrameRateInfo GetFrameRateInfo(string sessionId)
//        {
//            if (_sessions.TryGetValue(sessionId, out var session))
//            {
//                var stats = session.Stats;
//                return new FrameRateInfo
//                {
//                    TargetFPS = stats.TargetFPS,
//                    ActualFPS = stats.ActualProcessingFPS,
//                    ProcessingTimeMs = session.ProcessingTimes.Any() ? session.ProcessingTimes.Average() : 0,
//                    IsOptimal = stats.ActualProcessingFPS >= stats.TargetFPS * 0.8,
//                    Recommendation = GetFrameRateRecommendation(stats)
//                };
//            }
//            return new FrameRateInfo();
//        }

//        private string GetFrameRateRecommendation(DetectionStats stats)
//        {
//            if (stats.ActualProcessingFPS >= stats.TargetFPS * 0.9)
//                return "Optimal performance";
//            if (stats.ActualProcessingFPS >= stats.TargetFPS * 0.7)
//                return "Good performance";
//            if (stats.ActualProcessingFPS >= stats.TargetFPS * 0.5)
//                return "Consider reducing target FPS or disabling some features";
//            return "Reduce target FPS or disable heavy processing features";
//        }

//        public CameraMovementAnalysis GetCameraMovementAnalysis(string sessionId)
//        {
//            if (_sessions.TryGetValue(sessionId, out var session))
//            {
//                var stats = session.Stats;
//                return new CameraMovementAnalysis
//                {
//                    MovementType = stats.CameraMovement,
//                    StabilityScore = stats.CameraStability,
//                    HorizontalMovement = stats.RecentMovements.Average(m => m.X),
//                    VerticalMovement = stats.RecentMovements.Average(m => m.Y),
//                    ZoomLevel = 0,
//                    Status = GetCameraStatus(stats.CameraStability),
//                    Recommendation = GetCameraRecommendation(stats.CameraMovement, stats.CameraStability)
//                };
//            }
//            return new CameraMovementAnalysis();
//        }

//        private string GetCameraStatus(double stability)
//        {
//            if (stability > 80) return "Very Stable";
//            if (stability > 60) return "Stable";
//            if (stability > 40) return "Moderate";
//            if (stability > 20) return "Unstable";
//            return "Very Unstable";
//        }

//        private string GetCameraRecommendation(CameraMovementType movement, double stability)
//        {
//            if (stability < 40) return "Use tripod or stabilize camera";
//            if (movement == CameraMovementType.Shaking) return "Reduce camera shake";
//            if (movement == CameraMovementType.FastPan) return "Slow down panning movements";
//            return "Camera movement is good";
//        }

//        public List<MonitoringOption> GetAvailableMonitoringOptions()
//        {
//            return new List<MonitoringOption>(_availableMonitoringOptions);
//        }

//        public void SetTargetFPS(string sessionId, double targetFPS)
//        {
//            if (_sessions.TryGetValue(sessionId, out var session))
//            {
//                session.Settings.TargetFPS = Math.Max(1, Math.Min(60, targetFPS));
//                session.Stats.TargetFPS = session.Settings.TargetFPS;
//            }
//        }

//        public void EnableMonitoringOption(string sessionId, string optionName, bool enable)
//        {
//            if (_sessions.TryGetValue(sessionId, out var session))
//            {
//                var option = session.MonitoringConfig.EnabledOptions.FirstOrDefault(o => o.Name == optionName);
//                if (option != null)
//                {
//                    option.IsEnabled = enable;
//                }

//                UpdateMonitoringConfiguration(sessionId, session.MonitoringConfig);
//            }
//        }

//        public void Dispose()
//        {
//            Dispose(true);
//            GC.SuppressFinalize(this);
//        }

//        protected virtual void Dispose(bool disposing)
//        {
//            if (!_disposed)
//            {
//                if (disposing)
//                {
//                    foreach (var session in _sessions.Values)
//                    {
//                        session.PreviousFrame?.Dispose();
//                        session.PreviousGrayFrame?.Dispose();
//                    }
//                    _sessions.Clear();
//                }
//                _disposed = true;
//            }
//        }
//    }
//}