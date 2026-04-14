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
        private static readonly string[] MobileNetSsdClassNames =
        {
            "background", "aeroplane", "bicycle", "bird", "boat", "bottle", "bus", "car", "cat", "chair", "cow",
            "diningtable", "dog", "horse", "motorbike", "person", "pottedplant", "sheep", "sofa", "train", "tvmonitor"
        };

        private static bool IsLikelyTextProto(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0)
                    return false;
                var sampleLen = Math.Min(bytes.Length, 4096);
                var text = Encoding.UTF8.GetString(bytes, 0, sampleLen);
                return text.Contains("layer", StringComparison.OrdinalIgnoreCase)
                       && text.Contains("{")
                       && text.Contains("name", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static (string proto, string weights)? ResolveSsdModelFiles(string dir)
        {
            if (!Directory.Exists(dir))
                return null;

            var protoCandidates = new List<string>();
            var modelCandidates = new List<string>();

            protoCandidates.AddRange(Directory.GetFiles(dir, "*.prototxt", SearchOption.TopDirectoryOnly));
            modelCandidates.AddRange(Directory.GetFiles(dir, "*.caffemodel", SearchOption.TopDirectoryOnly));

            var namedProto = Path.Combine(dir, "MobileNetSSD_deploy.prototxt");
            if (File.Exists(namedProto) && !protoCandidates.Contains(namedProto, StringComparer.OrdinalIgnoreCase))
                protoCandidates.Insert(0, namedProto);

            var namedModelA = Path.Combine(dir, "MobileNetSSD_deploy.caffemodel");
            var namedModelB = Path.Combine(dir, "mobilenet_iter_73000.caffemodel");
            if (File.Exists(namedModelA) && !modelCandidates.Contains(namedModelA, StringComparer.OrdinalIgnoreCase))
                modelCandidates.Insert(0, namedModelA);
            if (File.Exists(namedModelB) && !modelCandidates.Contains(namedModelB, StringComparer.OrdinalIgnoreCase))
                modelCandidates.Insert(0, namedModelB);

            var validProto = protoCandidates.FirstOrDefault(IsLikelyTextProto);
            var validModel = modelCandidates.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(validProto) && !string.IsNullOrWhiteSpace(validModel))
                return (validProto, validModel);

            // Some deployments accidentally swap names/content. Recover by scanning all files.
            var anyFiles = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);
            var fallbackProto = anyFiles.FirstOrDefault(IsLikelyTextProto);
            var fallbackModel = anyFiles.FirstOrDefault(f =>
                !string.Equals(f, fallbackProto, StringComparison.OrdinalIgnoreCase)
                && !IsLikelyTextProto(f));
            if (!string.IsNullOrWhiteSpace(fallbackProto) && !string.IsNullOrWhiteSpace(fallbackModel))
                return (fallbackProto, fallbackModel);

            return null;
        }

        private Net? GetOrLoadMobileNetSsd()
        {
            if (_mobileNetSsd != null)
                return _mobileNetSsd;
            if (_mobileNetSsdLoadFailed)
                return null;

            lock (_ssdLoadLock)
            {
                if (_mobileNetSsd != null)
                    return _mobileNetSsd;
                if (_mobileNetSsdLoadFailed)
                    return null;

                var dir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "models");
                var resolved = ResolveSsdModelFiles(dir);
                if (resolved == null)
                {
                    _mobileNetSsdLoadFailed = true;
                    return null;
                }
                var (proto, weights) = resolved.Value;

                try
                {
                    var net = CvDnn.ReadNetFromCaffe(proto, weights);
                    if (net == null)
                    {
                        _mobileNetSsdLoadFailed = true;
                        return null;
                    }

                    _mobileNetSsd = net;
                    return _mobileNetSsd;
                }
                catch
                {
                    _mobileNetSsdLoadFailed = true;
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
            public Net? SsdNet { get; set; }
        }

        private sealed class SsdSceneDetectionStrategy : ISceneDetectionStrategy
        {
            public string SourceKey => "ssd";
            public bool ShouldRun(SceneProcessingOptions options) => options.EnableSsdModel;

            public void Execute(SceneStrategyContext context)
            {
                if (context.SsdNet == null) return;
                try
                {
                    using var blob = CvDnn.BlobFromImage(context.ProcessedFrameBgr, 1.0, new Size(300, 300), new Scalar(104, 117, 123), false, false);
                    context.SsdNet.SetInput(blob);
                    using var output = context.SsdNet.Forward();
                    var n = output.Size(2);
                    for (var i = 0; i < n; i++)
                    {
                        var confidence = output.At<float>(0, 0, i, 2);
                        var ssdThreshold = Math.Clamp(context.Options.SsdConfidenceThreshold, 0.05, 0.95);
                        if (confidence < ssdThreshold) continue;
                        var classId = (int)output.At<float>(0, 0, i, 1);
                        if (classId < 0 || classId >= MobileNetSsdClassNames.Length) continue;
                        var label = MobileNetSsdClassNames[classId];
                        if (string.Equals(label, "background", StringComparison.OrdinalIgnoreCase)) continue;

                        var x1 = output.At<float>(0, 0, i, 3) * context.FrameWidth;
                        var y1 = output.At<float>(0, 0, i, 4) * context.FrameHeight;
                        var x2 = output.At<float>(0, 0, i, 5) * context.FrameWidth;
                        var y2 = output.At<float>(0, 0, i, 6) * context.FrameHeight;
                        var w = Math.Max(1, x2 - x1);
                        var h = Math.Max(1, y2 - y1);
                        var cat = MapLabelToCategory(label);
                        if ((cat == "Human" && !context.Options.IncludeHuman) ||
                            (cat == "Animal" && !context.Options.IncludeAnimal) ||
                            (cat == "Object" && !context.Options.IncludeObject))
                        {
                            continue;
                        }

                        var ent = new SceneEntity
                        {
                            Category = cat,
                            Label = label,
                            Confidence = confidence,
                            BBox = new BoundingBox { X = x1, Y = y1, Width = w, Height = h },
                            Source = SourceKey
                        };
                        context.Scene.Entities.Add(ent);
                        context.DetectionData.Objects.Add(new ObjectDetection
                        {
                            Type = $"scene:{label}",
                            Confidence = confidence,
                            BBox = ent.BBox,
                            AdditionalInfo = cat
                        });
                    }
                    context.ExecutedSources.Add(SourceKey);
                }
                catch (Exception ex)
                {
                    context.Scene.Pipeline = "MobileNetSSD_Error";
                    context.Logs.Add(new SystemLog
                    {
                        Message = $"Scene SSD error: {ex.Message}",
                        Timestamp = DateTime.UtcNow,
                        Level = "Warning",
                        Component = "SceneAnalysis"
                    });
                }
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

        private sealed class FullBodySceneDetectionStrategy : ISceneDetectionStrategy
        {
            public string SourceKey => "fullbody";
            public bool ShouldRun(SceneProcessingOptions options) => options.EnableFullBodyCascade && options.IncludeHuman;

            public void Execute(SceneStrategyContext context)
            {
                context.Service.AddHumanFromFullBody(context.ProcessedFrameBgr, context.GrayFrame, context.DetectionData, context.Scene, context.Options);
                context.ExecutedSources.Add(SourceKey);
            }
        }

        private sealed class CatSceneDetectionStrategy : ISceneDetectionStrategy
        {
            public string SourceKey => "cat_cascade";
            public bool ShouldRun(SceneProcessingOptions options) => options.EnableCatCascade && options.IncludeAnimal;

            public void Execute(SceneStrategyContext context)
            {
                context.Service.AddCatsFromCascade(context.GrayFrame, context.DetectionData, context.Scene, context.Options);
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
                Pipeline = "CascadeHybrid",
                Entities = new List<SceneEntity>(),
                Notes = "Place MobileNetSSD_deploy.prototxt + .caffemodel under Uploads/models for full VOC object detection. Optional: haarcascade_frontalcatface.xml in cascades for cats without SSD."
            };

            var fw = processedFrameBgr.Width;
            var fh = processedFrameBgr.Height;

            var executedSources = new List<string>();
            var runSsd = opts.EnableSsdModel;
            var runFace = opts.EnableFaceCascade;
            var runFullBody = opts.EnableFullBodyCascade;
            var runCat = opts.EnableCatCascade;

            scene.ModelStatus = new SceneModelStatus
            {
                SsdRequested = runSsd,
                FaceCascadeRequested = runFace,
                FullBodyRequested = runFullBody,
                CatCascadeRequested = runCat,
                ProcessAllModels = opts.ProcessAllModels,
                FaceCascadeReady = _faceCascade != null && !_faceCascade.Empty(),
                FullBodyReady = _fullbody != null && !_fullbody.Empty(),
                CatCascadeReady = _catFaceCascade != null && !_catFaceCascade.Empty()
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
                SsdNet = runSsd ? GetOrLoadMobileNetSsd() : null
            };
            scene.ModelStatus.SsdLoaded = context.SsdNet != null;

            var strategies = new ISceneDetectionStrategy[]
            {
                new SsdSceneDetectionStrategy(),
                new FaceSceneDetectionStrategy(),
                new FullBodySceneDetectionStrategy(),
                new CatSceneDetectionStrategy()
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

            scene.Pipeline = executedSources.Count == 0 ? "Disabled" : string.Join("+", executedSources.Distinct());
            scene.Notes = $"{scene.Notes} ActiveSources={scene.Pipeline}; Include(H:{opts.IncludeHuman},A:{opts.IncludeAnimal},O:{opts.IncludeObject})";
            session.LastSceneAnalysis = scene;
        }

        private static string MapLabelToCategory(string label)
        {
            if (string.Equals(label, "person", StringComparison.OrdinalIgnoreCase))
                return "Human";
            if (label is "cat" or "dog" or "bird" or "horse" or "cow" or "sheep")
                return "Animal";
            return "Object";
        }

        private void AddHumanEntitiesFromFaces(DetectionData detectionData, SceneAnalysisResult scene)
        {
            foreach (var face in detectionData.Faces)
            {
                if (face.BBox == null)
                    continue;
                if (scene.Entities.Any(e => e.Source == "ssd" && e.Label == "person" && IoU(e.BBox, face.BBox) > 0.4))
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

        private void AddHumanFromFullBody(Mat processedFrameBgr, Mat grayFrame, DetectionData detectionData, SceneAnalysisResult scene, SceneProcessingOptions opts)
        {
            if (_fullbody == null || _fullbody.Empty())
                return;

            try
            {
                var fullBodyMinNeighbors = Math.Clamp(opts.FullBodyMinNeighbors, 1, 12);
                var bodies = _fullbody.DetectMultiScale(grayFrame, 1.1, fullBodyMinNeighbors, HaarDetectionTypes.ScaleImage, new Size(40, 40));
                foreach (var r in bodies)
                {
                    if (detectionData.Faces.Any(f => f.BBox != null && IoU(BBoxFromRect(r), f.BBox) > 0.25))
                        continue;
                    if (scene.Entities.Any(e => e.Category == "Human" && IoU(BBoxFromRect(r), e.BBox) > 0.35))
                        continue;

                    var bb = BBoxFromRect(r);
                    scene.Entities.Add(new SceneEntity
                    {
                        Category = "Human",
                        Label = "person_fullbody",
                        Confidence = 0.55,
                        BBox = bb,
                        Source = "fullbody"
                    });
                    Cv2.Rectangle(processedFrameBgr, r, new Scalar(0, 200, 255), 2);
                    Cv2.PutText(processedFrameBgr, "person?",
                        new Point(r.X, Math.Max(12, r.Y - 6)),
                        HersheyFonts.HersheySimplex, 0.45, new Scalar(0, 200, 255), 1);
                }
            }
            catch
            {
                /* ignore */
            }
        }

        private void AddCatsFromCascade(Mat grayFrame, DetectionData detectionData, SceneAnalysisResult scene, SceneProcessingOptions opts)
        {
            if (_catFaceCascade == null || _catFaceCascade.Empty())
                return;
            if (scene.Entities.Any(e => e.Label == "cat" && e.Source == "ssd"))
                return;

            try
            {
                // Tightened thresholds to reduce false positives on non-cat scenes.
                var catMinNeighbors = Math.Clamp(opts.CatMinNeighbors, 1, 16);
                var catMinAreaRatio = Math.Clamp(opts.CatMinAreaRatio, 0.0005, 0.08);
                var cats = _catFaceCascade.DetectMultiScale(grayFrame, 1.1, catMinNeighbors, HaarDetectionTypes.ScaleImage, new Size(36, 36));
                foreach (var r in cats)
                {
                    var bb = BBoxFromRect(r);
                    var aspect = bb.Width <= 0 ? 0 : bb.Height / bb.Width;
                    if (aspect < 0.65 || aspect > 1.55)
                        continue;
                    if ((bb.Width * bb.Height) < (grayFrame.Width * grayFrame.Height * catMinAreaRatio))
                        continue;
                    // Ignore boxes mostly overlapping human face/fullbody candidates.
                    if (scene.Entities.Any(e =>
                            e.Category == "Human" &&
                            IoU(e.BBox, bb) > 0.3))
                    {
                        continue;
                    }
                    if (scene.Entities.Any(e => e.Label == "cat" && IoU(e.BBox, bb) > 0.35))
                        continue;
                    scene.Entities.Add(new SceneEntity
                    {
                        Category = "Animal",
                        Label = "cat",
                        Confidence = 0.5,
                        BBox = bb,
                        Source = "cat_cascade"
                    });
                    detectionData.Objects.Add(new ObjectDetection
                    {
                        Type = "scene:cat",
                        Confidence = 0.5,
                        BBox = bb,
                        AdditionalInfo = "Animal"
                    });
                }
            }
            catch
            {
                /* ignore */
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
