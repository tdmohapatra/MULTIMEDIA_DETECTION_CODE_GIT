using STAR_MUTIMEDIA.Models;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Tesseract;
using System;
using System.Linq;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Mediapipe.Net.Framework;
using Mediapipe.Net.Framework.Protobuf;

using Mediapipe.Net.Core;
namespace STAR_MUTIMEDIA.Services
{
    public class RealTimeDetectionService_test : IRealTimeDetectionService_test, IDisposable
    {
        private readonly string _tessDataPath;
        private readonly ConcurrentDictionary<string, SessionData> _sessions;
        private readonly object _lockObject = new object();

        public RealTimeDetectionService_test(string tessDataPath)
        {
            _tessDataPath = tessDataPath;
            _sessions = new ConcurrentDictionary<string, SessionData>();
            Environment.SetEnvironmentVariable("TESSDATA_PREFIX", _tessDataPath);
        }

        //public async Task<DetectionResult> ProcessFrameAsync(FrameData frameData)
        //{
        //    var sessionId = frameData.SessionId;
        //    InitializeSessionIfNotExists(sessionId);

        //    var session = _sessions[sessionId];
        //    var result = new DetectionResult();
        //    var notifications = new List<string>();
        //    var logs = new List<string>();

        //    try
        //    {
        //        // Convert base64 to image
        //        var imageBytes = Convert.FromBase64String(frameData.ImageData.Split(',')[1]);
        //        using (var ms = new MemoryStream(imageBytes))
        //        using (var image = SD.Image.FromStream(ms))
        //        using (var bitmap = new SD.Bitmap(image))
        //        {
        //            // Convert to OpenCV Mat
        //            using (var mat = BitmapConverter.ToMat(bitmap))
        //            {
        //                var processedMat = ProcessFrame(mat, session, notifications, logs);

        //                // Convert back to base64
        //                using (var processedBitmap = BitmapConverter.ToBitmap(processedMat))
        //                using (var outputMs = new MemoryStream())
        //                {
        //                    processedBitmap.Save(outputMs, SDI.ImageFormat.Jpeg);
        //                    result.ImageData = "data:image/jpeg;base64," + Convert.ToBase64String(outputMs.ToArray());
        //                }
        //            }
        //        }

        //        result.Stats = session.Stats.Clone();
        //        result.Notifications = notifications;
        //        result.Logs = logs;
        //        session.Stats.LastUpdate = DateTime.Now;
        //    }
        //    catch (Exception ex)
        //    {
        //        logs.Add($"Error processing frame: {ex.Message}");
        //        result.Logs = logs;
        //    }

        //    return result;
        //}

        public async Task<DetectionResult> ProcessFrameAsync(FrameData frameData)
        {
            var sessionId = frameData.SessionId;
            InitializeSessionIfNotExists(sessionId);

            var session = _sessions[sessionId];
            var result = new DetectionResult();
            var notifications = new List<string>();
            var logs = new List<string>();

            try
            {
                // Convert base64 image data to OpenCV Mat
                var imageBytes = Convert.FromBase64String(frameData.ImageData.Split(',')[1]);
                using (var ms = new MemoryStream(imageBytes))
                using (var image = SD.Image.FromStream(ms))
                using (var bitmap = new SD.Bitmap(image))
                {
                    using (var mat = BitmapConverter.ToMat(bitmap))
                    {
                        var processedMat = ProcessFrame(mat, session, notifications, logs);

                        // Convert processed Mat back to base64 image
                        using (var processedBitmap = BitmapConverter.ToBitmap(processedMat))
                        using (var outputMs = new MemoryStream())
                        {
                            processedBitmap.Save(outputMs, SDI.ImageFormat.Jpeg);
                            result.ImageData = "data:image/jpeg;base64," + Convert.ToBase64String(outputMs.ToArray());
                        }
                    }
                }

                // Assign session stats
                result.Stats = session.Stats.Clone();

                // Convert string notifications/logs into model objects
                result.Notifications = notifications.Select(n => new DetectionNotification
                {
                    Message = n,
                    Timestamp = DateTime.Now
                }).ToList();

                result.Logs = logs.Select(l => new SystemLog
                {
                    Message = l,
                    Timestamp = DateTime.Now
                }).ToList();

                session.Stats.LastUpdate = DateTime.Now;
            }
            catch (Exception ex)
            {
                logs.Add($"Error processing frame: {ex.Message}");

                result.Logs = logs.Select(l => new SystemLog
                {
                    Message = l,
                    Timestamp = DateTime.Now
                }).ToList();
            }

            return result;
        }


        private Mat ProcessFrame(Mat frame, SessionData session, List<string> notifications, List<string> logs)
        {
            var processedFrame = frame.Clone();
            var stats = session.Stats;

            try
            {
                using (var grayFrame = new Mat())
                {
                    Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);
                    Cv2.EqualizeHist(grayFrame, grayFrame);

                    // Load cascades
                    var cascadesPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "cascades");

                    // Face detection
                    if (session.Settings.EnableFaceDetection)
                    {
                        var faceCascadePath = Path.Combine(cascadesPath, "haarcascade_frontalface_alt.xml");
                        if (File.Exists(faceCascadePath))
                        {
                            using (var faceCascade = new CascadeClassifier(faceCascadePath))
                            {
                                var faces = faceCascade.DetectMultiScale(grayFrame, 1.1, 5);
                                stats.FacesDetected = faces.Length;

                                foreach (var face in faces)
                                {
                                    Cv2.Rectangle(processedFrame, face, Scalar.Red, 2);
                                    Cv2.PutText(processedFrame, "Face",
                                        new OpenCvSharp.Point(face.X, face.Y - 10),
                                        HersheyFonts.HersheySimplex, 0.5, Scalar.Red, 1);

                                    // Eye detection
                                    if (session.Settings.EnableEyeDetection)
                                    {
                                        var eyeCascadePath = Path.Combine(cascadesPath, "haarcascade_eye.xml");
                                        if (File.Exists(eyeCascadePath))
                                        {
                                            using (var eyeCascade = new CascadeClassifier(eyeCascadePath))
                                            {
                                                var faceROI = grayFrame[face];
                                                var eyes = eyeCascade.DetectMultiScale(faceROI);
                                                stats.EyesDetected = eyes.Length;

                                                foreach (var eye in eyes)
                                                {
                                                    var eyeRect = new OpenCvSharp.Rect(face.X + eye.X, face.Y + eye.Y, eye.Width, eye.Height);
                                                    Cv2.Rectangle(processedFrame, eyeRect, Scalar.Blue, 1);
                                                    Cv2.PutText(processedFrame, "Eye",
                                                        new OpenCvSharp.Point(face.X + eye.X, face.Y + eye.Y - 5),
                                                        HersheyFonts.HersheySimplex, 0.3, Scalar.Blue, 1);
                                                }
                                            }
                                        }
                                    }
                                }

                                if (faces.Length > 0 && !session.LastFaceState)
                                {
                                    notifications.Add($"{faces.Length} face(s) detected");
                                    logs.Add($"Face detection: {faces.Length} faces found");
                                }

                                session.LastFaceState = faces.Length > 0;
                            }
                        }
                    }

                    // Movement detection
                    if (session.Settings.EnableMovementDetection && session.PreviousFrame != null)
                    {
                        var movementLevel = CalculateMovementLevel(frame, session.PreviousFrame);
                        stats.CurrentMovementLevel = movementLevel;
                        stats.MovementDetected = movementLevel > session.Settings.MovementThreshold;

                        if (stats.MovementDetected && !session.LastMovementState)
                        {
                            notifications.Add($"Movement detected: {movementLevel:0.0}%");
                            logs.Add($"Movement started: {movementLevel:0.0}%");
                        }
                        else if (!stats.MovementDetected && session.LastMovementState)
                        {
                            notifications.Add("Movement stopped");
                            logs.Add("Movement stopped");
                        }

                        session.LastMovementState = stats.MovementDetected;
                        DrawMovementIndicator(processedFrame, movementLevel);
                    }

                    // Text detection every 15 frames
                    if (session.Settings.EnableTextDetection && stats.TotalFramesProcessed % 15 == 0)
                    {
                        DetectText(processedFrame, stats, notifications, logs);
                    }

                    // Update previous frame
                    session.PreviousFrame?.Dispose();
                    session.PreviousFrame = frame.Clone();

                    // Update statistics
                    stats.TotalFramesProcessed++;
                    UpdateFPS(stats);

                    // Overlay stats
                    AddStatsOverlay(processedFrame, stats);
                }
            }
            catch (Exception ex)
            {
                logs.Add($"Frame processing error: {ex.Message}");
            }

            return processedFrame;
        }

        private double CalculateMovementLevel(Mat currentFrame, Mat previousFrame)
        {
            try
            {
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
                    return (double)nonZeroPixels / totalPixels * 100.0;
                }
            }
            catch
            {
                return 0.0;
            }
        }

        private void DetectText(Mat frame, DetectionStats stats, List<string> notifications, List<string> logs)
        {
            try
            {
                using (var tesseractEngine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default))
                {
                    tesseractEngine.SetVariable("tessedit_char_whitelist",
                        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?()-+*/=@#$%&");

                    using (var bitmap = BitmapConverter.ToBitmap(frame))
                    using (var pix = Pix.LoadFromMemory(BitmapToBytes(bitmap)))
                    using (var page = tesseractEngine.Process(pix))
                    {
                        var text = page.GetText()?.Trim();
                        if (!string.IsNullOrEmpty(text) && text.Length > 3)
                        {
                            stats.TextDetected = true;
                            notifications.Add($"Text detected: {text.Substring(0, Math.Min(30, text.Length))}...");
                            logs.Add($"OCR: {text}");

                            Cv2.PutText(frame, "Text Detected",
                                new OpenCvSharp.Point(10, frame.Height - 30),
                                HersheyFonts.HersheySimplex, 0.6, Scalar.Yellow, 2);
                        }
                        else
                        {
                            stats.TextDetected = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logs.Add($"Text detection error: {ex.Message}");
            }
        }

        private static byte[] BitmapToBytes(SD.Bitmap bitmap)
        {
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, SDI.ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private void DrawMovementIndicator(Mat frame, double movementLevel)
        {
            int meterWidth = 200;
            int meterHeight = 20;
            int meterX = frame.Width - meterWidth - 10;
            int meterY = 10;

            Cv2.Rectangle(frame, new OpenCvSharp.Rect(meterX, meterY, meterWidth, meterHeight), Scalar.DarkGray, -1);
            int fillWidth = (int)(movementLevel / 100.0 * meterWidth);
            var color = GetMovementColor(movementLevel);

            Cv2.Rectangle(frame, new OpenCvSharp.Rect(meterX, meterY, fillWidth, meterHeight), color, -1);
            Cv2.Rectangle(frame, new OpenCvSharp.Rect(meterX, meterY, meterWidth, meterHeight), Scalar.White, 1);

            Cv2.PutText(frame, $"Movement: {movementLevel:0.0}%",
                new OpenCvSharp.Point(meterX, meterY + meterHeight + 15),
                HersheyFonts.HersheySimplex, 0.4, Scalar.White, 1);
        }

        private Scalar GetMovementColor(double movementLevel)
        {
            if (movementLevel < 10) return new Scalar(0, 255, 0);
            if (movementLevel < 30) return new Scalar(0, 255, 255);
            if (movementLevel < 50) return new Scalar(0, 165, 255);
            return new Scalar(0, 0, 255);
        }

        private void AddStatsOverlay(Mat frame, DetectionStats stats)
        {
            string[] statsText =
            {
                $"Faces: {stats.FacesDetected}",
                $"Eyes: {stats.EyesDetected}",
                $"Movement: {(stats.MovementDetected ? "Yes" : "No")}",
                $"Text: {(stats.TextDetected ? "Yes" : "No")}",
                $"FPS: {stats.CurrentFPS:0.0}",
                $"Frame: {stats.TotalFramesProcessed}"
            };

            int yOffset = 30;
            foreach (var text in statsText)
            {
                Cv2.PutText(frame, text, new OpenCvSharp.Point(10, yOffset),
                    HersheyFonts.HersheySimplex, 0.5, Scalar.White, 1);
                yOffset += 20;
            }
        }

        private void UpdateFPS(DetectionStats stats)
        {
            var now = DateTime.Now;
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

        private void InitializeSessionIfNotExists(string sessionId)
        {
            _sessions.AddOrUpdate(sessionId,
                id => new SessionData
                {
                    Stats = new DetectionStats(),
                    Settings = new DetectionSettings(),
                    SessionId = id
                },
                (id, existing) => existing);
        }

        public DetectionStats GetSessionStats(string sessionId)
        {
            return _sessions.TryGetValue(sessionId, out var session)
                ? session.Stats.Clone()
                : new DetectionStats();
        }

        public void UpdateSessionSettings(string sessionId, DetectionSettings settings)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Settings = settings;
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
                session.PreviousFrame?.Dispose();
            }
        }

        public void Dispose()
        {
            foreach (var session in _sessions.Values)
            {
                session.PreviousFrame?.Dispose();
            }
            _sessions.Clear();
        }
    }
}
