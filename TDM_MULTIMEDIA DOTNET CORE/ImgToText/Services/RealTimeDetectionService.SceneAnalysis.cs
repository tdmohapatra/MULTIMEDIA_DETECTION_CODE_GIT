using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using STAR_MUTIMEDIA.Models;

namespace STAR_MUTIMEDIA.Services
{
    public partial class RealTimeDetectionService
    {
        private static readonly string[] YoloCocoClassNames =
        {
            "person","bicycle","car","motorcycle","airplane","bus","train","truck","boat","traffic light",
            "fire hydrant","stop sign","parking meter","bench","bird","cat","dog","horse","sheep","cow",
            "elephant","bear","zebra","giraffe","backpack","umbrella","handbag","tie","suitcase","frisbee",
            "skis","snowboard","sports ball","kite","baseball bat","baseball glove","skateboard","surfboard","tennis racket","bottle",
            "wine glass","cup","fork","knife","spoon","bowl","banana","apple","sandwich","orange",
            "broccoli","carrot","hot dog","pizza","donut","cake","chair","couch","potted plant","bed",
            "dining table","toilet","tv","laptop","mouse","remote","keyboard","cell phone","microwave","oven",
            "toaster","sink","refrigerator","book","clock","vase","scissors","teddy bear","hair drier","toothbrush"
        };

        // Tries GPU-accelerated inference via OpenCV's OpenCL target (works with the stock
        // OpenCvSharp4.runtime.win package, no CUDA build required). SetPreferableTarget alone
        // doesn't guarantee the combo works for every layer in the graph, so we validate with a
        // real warmup forward pass and fall back to CPU if it throws.
        private static bool TryAccelerateWithOpenCl(Net net, Size warmupInputSize)
        {
            try
            {
                net.SetPreferableBackend(Backend.OPENCV);
                net.SetPreferableTarget(Target.OPENCL);

                using var dummy = new Mat(warmupInputSize, MatType.CV_8UC3, Scalar.All(0));
                using var blob = CvDnn.BlobFromImage(dummy, 1.0 / 255.0, warmupInputSize, new Scalar(), swapRB: true, crop: false);
                net.SetInput(blob);
                using var warmupOutput = net.Forward();
                return true;
            }
            catch
            {
                try
                {
                    net.SetPreferableBackend(Backend.OPENCV);
                    net.SetPreferableTarget(Target.CPU);
                }
                catch
                {
                    // Leave the net on its default backend/target if even this fails.
                }
                return false;
            }
        }

        private Net? GetOrLoadYoloV8()
        {
            if (_yoloV8Net != null)
                return _yoloV8Net;
            if (_yoloLoadFailed)
                return null;

            lock (_yoloLoadLock)
            {
                if (_yoloV8Net != null)
                    return _yoloV8Net;
                if (_yoloLoadFailed)
                    return null;

                try
                {
                    var dir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "models");
                    if (!Directory.Exists(dir))
                    {
                        _yoloLoadFailed = true;
                        return null;
                    }

                    var model = Directory.GetFiles(dir, "*.onnx", SearchOption.TopDirectoryOnly)
                        .OrderBy(f => f.Contains("yolov8n", StringComparison.OrdinalIgnoreCase) ? 0 :
                                      f.Contains("yolov8s", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                        .FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(model))
                    {
                        _yoloLoadFailed = true;
                        return null;
                    }

                    var net = CvDnn.ReadNetFromOnnx(model);
                    if (net == null)
                    {
                        _yoloLoadFailed = true;
                        return null;
                    }

                    _yoloUsesOpenCl = TryAccelerateWithOpenCl(net, new Size(640, 640));
                    _yoloV8Net = net;
                    return _yoloV8Net;
                }
                catch
                {
                    _yoloLoadFailed = true;
                    return null;
                }
            }
        }

        private Net? GetOrLoadYuNetFace()
        {
            if (_yunetFaceNet != null)
                return _yunetFaceNet;
            if (_yunetLoadFailed)
                return null;

            lock (_yunetLoadLock)
            {
                if (_yunetFaceNet != null)
                    return _yunetFaceNet;
                if (_yunetLoadFailed)
                    return null;

                try
                {
                    var path = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "models", "face_detection_yunet_2023mar.onnx");
                    if (!File.Exists(path))
                    {
                        _yunetLoadFailed = true;
                        return null;
                    }

                    var net = CvDnn.ReadNetFromOnnx(path);
                    if (net == null)
                    {
                        _yunetLoadFailed = true;
                        return null;
                    }

                    _yunetUsesOpenCl = TryAccelerateWithOpenCl(net, new Size(320, 320));
                    _yunetFaceNet = net;
                    return _yunetFaceNet;
                }
                catch
                {
                    _yunetLoadFailed = true;
                    return null;
                }
            }
        }

        private Net? GetOrLoadEmotionFerPlus()
        {
            if (_emotionFerPlusNet != null)
                return _emotionFerPlusNet;
            if (_emotionLoadFailed)
                return null;

            lock (_emotionLoadLock)
            {
                if (_emotionFerPlusNet != null)
                    return _emotionFerPlusNet;
                if (_emotionLoadFailed)
                    return null;

                try
                {
                    var path = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "models", "emotion-ferplus-8.onnx");
                    if (!File.Exists(path))
                    {
                        _emotionLoadFailed = true;
                        return null;
                    }

                    var net = CvDnn.ReadNetFromOnnx(path);
                    if (net == null)
                    {
                        _emotionLoadFailed = true;
                        return null;
                    }

                    _emotionUsesOpenCl = TryAccelerateWithOpenCl(net, new Size(64, 64));
                    _emotionFerPlusNet = net;
                    return _emotionFerPlusNet;
                }
                catch
                {
                    _emotionLoadFailed = true;
                    return null;
                }
            }
        }

        private interface ISceneDetectionStrategy
        {
            string SourceKey { get; }
            bool ShouldRun(SceneProcessingOptions options);
            void Execute(SceneStrategyContext context);
        }

        private sealed class SceneStrategyContext
        {
            public required RealTimeDetectionService Service { get; init; }
            public required Mat ProcessedFrameBgr { get; init; }
            public required Mat GrayFrame { get; init; }
            public required SessionData Session { get; init; }
            public required DetectionData DetectionData { get; init; }
            public required List<SystemLog> Logs { get; init; }
            public required SceneAnalysisResult Scene { get; init; }
            public required SceneProcessingOptions Options { get; init; }
            public required List<string> ExecutedSources { get; init; }
            public int FrameWidth { get; init; }
            public int FrameHeight { get; init; }
            public Net? DetectionNet { get; set; }
        }

        private sealed class YoloSceneDetectionStrategy : ISceneDetectionStrategy
        {
            public string SourceKey => "yolo";
            public bool ShouldRun(SceneProcessingOptions options) => options.EnableSsdModel;

            public void Execute(SceneStrategyContext context)
            {
                if (context.DetectionNet == null)
                    return;
                try
                {
                    using var bgr = EnsureThreeChannelBgr(context.ProcessedFrameBgr);
                    var yoloInput = Math.Clamp(context.Options.YoloInputSize, 320, 960);
                    using var blob = CvDnn.BlobFromImage(bgr, 1.0 / 255.0, new Size(yoloInput, yoloInput), new Scalar(), swapRB: true, crop: false);
                    context.DetectionNet.SetInput(blob);
                    using var output = context.DetectionNet.Forward();
                    ParseYoloOutputAndAppendEntities(output, context, yoloInput);
                    context.ExecutedSources.Add(SourceKey);
                }
                catch (Exception ex)
                {
                    context.Logs.Add(new SystemLog
                    {
                        Message = $"Scene YOLO error: {ex.Message}",
                        Timestamp = DateTime.UtcNow,
                        Level = "Warning",
                        Component = "SceneAnalysis"
                    });
                }
            }
        }

        private static void ParseYoloOutputAndAppendEntities(Mat output, SceneStrategyContext context, int modelInputSize)
        {
            // Handles common YOLOv8 output shapes:
            // - [1, 84, N]
            // - [1, N, 84]
            // where 84 = 4 bbox + 80 classes.
            var d1 = output.Size(1);
            var d2 = output.Size(2);
            var isChannelsFirst = d1 == 84;
            var proposalCount = isChannelsFirst ? d2 : d1;
            var features = isChannelsFirst ? d1 : d2;
            if (features < 6 || proposalCount <= 0)
            {
                return;
            }

            var threshold = Math.Clamp(context.Options.ObjectConfidenceThreshold, 0.05, 0.95);
            var scaleX = context.FrameWidth / (double)Math.Max(1, modelInputSize);
            var scaleY = context.FrameHeight / (double)Math.Max(1, modelInputSize);
            var boxes = new List<Rect>();
            var scores = new List<float>();
            var labels = new List<string>();
            var categories = new List<string>();
            var labelHistogram = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            float At(int proposal, int feature)
            {
                return isChannelsFirst
                    ? output.At<float>(0, feature, proposal)
                    : output.At<float>(0, proposal, feature);
            }

            for (var p = 0; p < proposalCount; p++)
            {
                var cx = At(p, 0);
                var cy = At(p, 1);
                var w = At(p, 2);
                var h = At(p, 3);
                if (w < 2 || h < 2) continue;

                var bestScore = 0f;
                var classId = -1;
                for (var c = 4; c < features; c++)
                {
                    var score = At(p, c);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        classId = c - 4;
                    }
                }
                if (bestScore < threshold || classId < 0 || classId >= YoloCocoClassNames.Length) continue;

                var label = YoloCocoClassNames[classId];
                var cat = MapLabelToCategory(label);
                if ((cat == "Human" && !context.Options.IncludeHuman) ||
                    (cat == "Animal" && !context.Options.IncludeAnimal) ||
                    (cat == "Object" && !context.Options.IncludeObject))
                {
                    continue;
                }

                var x = Math.Max(0, (int)Math.Round((cx - (w / 2.0f)) * scaleX));
                var y = Math.Max(0, (int)Math.Round((cy - (h / 2.0f)) * scaleY));
                var bw = Math.Max(1, (int)Math.Round(w * scaleX));
                var bh = Math.Max(1, (int)Math.Round(h * scaleY));
                if (x >= context.FrameWidth || y >= context.FrameHeight) continue;

                boxes.Add(new Rect(x, y, Math.Min(bw, context.FrameWidth - x), Math.Min(bh, context.FrameHeight - y)));
                scores.Add(bestScore);
                labels.Add(label);
                categories.Add(cat);
                if (!labelHistogram.ContainsKey(label))
                {
                    labelHistogram[label] = 0;
                }
                labelHistogram[label]++;
            }

            if (boxes.Count == 0) return;

            var nms = (float)Math.Clamp(context.Options.YoloNmsThreshold, 0.2, 0.8);
            CvDnn.NMSBoxes(boxes, scores, (float)threshold, nms, out var picked);
            foreach (var idx in picked)
            {
                if (idx < 0 || idx >= boxes.Count) continue;
                var bb = boxes[idx];
                context.Scene.Entities.Add(new SceneEntity
                {
                    Category = categories[idx],
                    Label = labels[idx],
                    Confidence = scores[idx],
                    BBox = new BoundingBox
                    {
                        X = bb.X,
                        Y = bb.Y,
                        Width = bb.Width,
                        Height = bb.Height
                    },
                    Source = "yolo"
                });
            }

            if (picked.Length > 0)
            {
                var top = labelHistogram
                    .OrderByDescending(kv => kv.Value)
                    .Take(6)
                    .Select(kv => $"{kv.Key}:{kv.Value}");
                context.Logs.Add(new SystemLog
                {
                    Message = $"YOLO detections {picked.Length} => {string.Join(", ", top)}",
                    Timestamp = DateTime.UtcNow,
                    Level = "Info",
                    Component = "SceneAnalysis"
                });
            }
        }

        private sealed class FaceSceneDetectionStrategy : ISceneDetectionStrategy
        {
            public string SourceKey => "face";
            public bool ShouldRun(SceneProcessingOptions options) => options.EnableFaceCascade && options.IncludeHuman;

            public void Execute(SceneStrategyContext context)
            {
                context.Service.AddHumanEntitiesFromFaces(context.DetectionData, context.Scene);
                context.ExecutedSources.Add(SourceKey);
            }
        }

        private void RunSceneEntityDetection(
            Mat processedFrameBgr,
            Mat grayFrame,
            SessionData session,
            DetectionData detectionData,
            List<SystemLog> logs,
            SceneProcessingOptions? options)
        {
            var opts = options ?? new SceneProcessingOptions();
            var scene = new SceneAnalysisResult
            {
                Pipeline = "YoloFaceHybrid",
                Entities = new List<SceneEntity>(),
                Notes = "YOLO for objects/people/animals, YuNet for faces."
            };

            var fw = processedFrameBgr.Width;
            var fh = processedFrameBgr.Height;

            var executedSources = new List<string>();
            var runObjectModel = opts.EnableSsdModel;
            var runFace = opts.EnableFaceCascade;

            scene.ModelStatus = new SceneModelStatus
            {
                SsdRequested = runObjectModel,
                FaceCascadeRequested = runFace,
                ProcessAllModels = opts.ProcessAllModels,
                FaceCascadeReady = _yunetFaceNet != null
            };

            var context = new SceneStrategyContext
            {
                Service = this,
                ProcessedFrameBgr = processedFrameBgr,
                GrayFrame = grayFrame,
                Session = session,
                DetectionData = detectionData,
                Logs = logs,
                Scene = scene,
                Options = opts,
                ExecutedSources = executedSources,
                FrameWidth = fw,
                FrameHeight = fh,
                DetectionNet = runObjectModel ? GetOrLoadYoloV8() : null
            };
            scene.ModelStatus.SsdLoaded = context.DetectionNet != null;

            var strategies = new ISceneDetectionStrategy[]
            {
                new YoloSceneDetectionStrategy(),
                new FaceSceneDetectionStrategy()
            };
            foreach (var strategy in strategies)
            {
                if (!strategy.ShouldRun(opts))
                    continue;
                strategy.Execute(context);
            }

            scene.Entities = DedupeSceneEntities(scene.Entities);
            if (!opts.IncludeObject)
                scene.Entities = scene.Entities.Where(e => !string.Equals(e.Category, "Object", StringComparison.OrdinalIgnoreCase)).ToList();
            if (!opts.IncludeAnimal)
                scene.Entities = scene.Entities.Where(e => !string.Equals(e.Category, "Animal", StringComparison.OrdinalIgnoreCase)).ToList();
            if (!opts.IncludeHuman)
                scene.Entities = scene.Entities.Where(e => !string.Equals(e.Category, "Human", StringComparison.OrdinalIgnoreCase)).ToList();

            session.EntityTracker.Assign(scene.Entities);

            scene.Pipeline = executedSources.Count == 0 ? "Disabled" : string.Join("+", executedSources.Distinct());
            scene.Notes = $"{scene.Notes} ActiveSources={scene.Pipeline}; Include(H:{opts.IncludeHuman},A:{opts.IncludeAnimal},O:{opts.IncludeObject})";
            session.LastSceneAnalysis = scene;
        }

        private static string MapLabelToCategory(string label)
        {
            if (string.Equals(label, "person", StringComparison.OrdinalIgnoreCase))
                return "Human";
            if (label is "cat" or "dog" or "bird" or "horse" or "cow" or "sheep" or "elephant" or "bear" or "zebra" or "giraffe")
                return "Animal";
            return "Object";
        }

        private static Mat EnsureThreeChannelBgr(Mat source)
        {
            if (source.Channels() == 3)
            {
                return source.Clone();
            }

            var bgr = new Mat();
            if (source.Channels() == 4)
            {
                Cv2.CvtColor(source, bgr, ColorConversionCodes.BGRA2BGR);
            }
            else if (source.Channels() == 1)
            {
                Cv2.CvtColor(source, bgr, ColorConversionCodes.GRAY2BGR);
            }
            else
            {
                source.ConvertTo(bgr, MatType.CV_8UC3);
            }
            return bgr;
        }

        private void AddHumanEntitiesFromFaces(DetectionData detectionData, SceneAnalysisResult scene)
        {
            foreach (var face in detectionData.Faces)
            {
                if (face.BBox == null)
                    continue;
                if (scene.Entities.Any(e => e.Source == "yolo" && e.Label == "person" && IoU(e.BBox, face.BBox) > 0.4))
                    continue;
                scene.Entities.Add(new SceneEntity
                {
                    Category = "Human",
                    Label = "face",
                    Confidence = face.Confidence,
                    BBox = CloneBBox(face.BBox),
                    Source = "face"
                });
            }
        }

        private static List<SceneEntity> DedupeSceneEntities(List<SceneEntity> list)
        {
            var ordered = list.OrderByDescending(e => e.Confidence).ToList();
            var kept = new List<SceneEntity>();
            foreach (var e in ordered)
            {
                if (kept.Any(k => IoU(k.BBox, e.BBox) > 0.45))
                    continue;
                kept.Add(e);
            }
            return kept;
        }

        private static double IoU(BoundingBox a, BoundingBox b)
        {
            if (a == null || b == null)
                return 0;
            var ax2 = a.X + a.Width;
            var ay2 = a.Y + a.Height;
            var bx2 = b.X + b.Width;
            var by2 = b.Y + b.Height;
            var ix1 = Math.Max(a.X, b.X);
            var iy1 = Math.Max(a.Y, b.Y);
            var ix2 = Math.Min(ax2, bx2);
            var iy2 = Math.Min(ay2, by2);
            var iw = Math.Max(0, ix2 - ix1);
            var ih = Math.Max(0, iy2 - iy1);
            var inter = iw * ih;
            var u = a.Width * a.Height + b.Width * b.Height - inter;
            return u <= 0 ? 0 : inter / u;
        }

        private static BoundingBox BBoxFromRect(Rect r) =>
            new BoundingBox { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };

        private static BoundingBox CloneBBox(BoundingBox b) =>
            new BoundingBox { X = b.X, Y = b.Y, Width = b.Width, Height = b.Height };

        private static string BuildSceneEntitySummary(List<SceneEntity> entities)
        {
            if (entities == null || entities.Count == 0)
                return "none";
            return string.Join(",",
                entities.GroupBy(e => string.IsNullOrEmpty(e.Label) ? e.Category : e.Label)
                    .Select(g => $"{g.Key}:{g.Count()}"));
        }

        private static void AppendSceneFrameTimeLog(string sessionId, int frameNumber, double processingMs, string summary, string pipeline)
        {
            try
            {
                var dir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "frame-logs");
                Directory.CreateDirectory(dir);
                var safe = string.IsNullOrEmpty(sessionId) ? "unknown" : sessionId.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
                var path = Path.Combine(dir, $"{safe}.log");
                var line = $"{DateTime.UtcNow:O}\t{frameNumber}\t{processingMs:F2}\t{pipeline}\t{summary.Replace('\t', ' ')}\r\n";
                File.AppendAllText(path, line);
            }
            catch
            {
                /* logging must not break pipeline */
            }
        }
    }
}
