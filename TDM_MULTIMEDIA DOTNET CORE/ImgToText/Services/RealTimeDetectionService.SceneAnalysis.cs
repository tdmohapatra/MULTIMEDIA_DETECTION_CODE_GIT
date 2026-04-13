using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                var proto = Path.Combine(dir, "MobileNetSSD_deploy.prototxt");
                var candidateWeights = new[]
                {
                    Path.Combine(dir, "MobileNetSSD_deploy.caffemodel"),
                    Path.Combine(dir, "mobilenet_iter_73000.caffemodel")
                };
                var weights = candidateWeights.FirstOrDefault(File.Exists);
                if (!File.Exists(proto) || string.IsNullOrWhiteSpace(weights))
                {
                    _mobileNetSsdLoadFailed = true;
                    return null;
                }

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

        private void RunSceneEntityDetection(Mat processedFrameBgr, Mat grayFrame, SessionData session, DetectionData detectionData, List<SystemLog> logs)
        {
            var scene = new SceneAnalysisResult
            {
                Pipeline = "CascadeHybrid",
                Entities = new List<SceneEntity>(),
                Notes = "Place MobileNetSSD_deploy.prototxt + .caffemodel under Uploads/models for full VOC object detection. Optional: haarcascade_frontalcatface.xml in cascades for cats without SSD."
            };

            var fw = processedFrameBgr.Width;
            var fh = processedFrameBgr.Height;

            var net = GetOrLoadMobileNetSsd();
            if (net != null)
            {
                try
                {
                    using var blob = CvDnn.BlobFromImage(processedFrameBgr, 1.0, new Size(300, 300), new Scalar(104, 117, 123), false, false);
                    net.SetInput(blob);
                    using var output = net.Forward();
                    scene.Pipeline = "MobileNetSSD";
                    var n = output.Size(2);
                    for (var i = 0; i < n; i++)
                    {
                        var confidence = output.At<float>(0, 0, i, 2);
                        if (confidence < 0.35f)
                            continue;
                        var classId = (int)output.At<float>(0, 0, i, 1);
                        if (classId < 0 || classId >= MobileNetSsdClassNames.Length)
                            continue;
                        var label = MobileNetSsdClassNames[classId];
                        if (string.Equals(label, "background", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var x1 = output.At<float>(0, 0, i, 3) * fw;
                        var y1 = output.At<float>(0, 0, i, 4) * fh;
                        var x2 = output.At<float>(0, 0, i, 5) * fw;
                        var y2 = output.At<float>(0, 0, i, 6) * fh;
                        var w = Math.Max(1, x2 - x1);
                        var h = Math.Max(1, y2 - y1);
                        var cat = MapLabelToCategory(label);
                        var ent = new SceneEntity
                        {
                            Category = cat,
                            Label = label,
                            Confidence = confidence,
                            BBox = new BoundingBox { X = x1, Y = y1, Width = w, Height = h },
                            Source = "ssd"
                        };
                        scene.Entities.Add(ent);
                        detectionData.Objects.Add(new ObjectDetection
                        {
                            Type = $"scene:{label}",
                            Confidence = confidence,
                            BBox = ent.BBox,
                            AdditionalInfo = cat
                        });
                    }
                }
                catch (Exception ex)
                {
                    scene.Pipeline = "MobileNetSSD_Error";
                    logs.Add(new SystemLog
                    {
                        Message = $"Scene SSD error: {ex.Message}",
                        Timestamp = DateTime.UtcNow,
                        Level = "Warning",
                        Component = "SceneAnalysis"
                    });
                }
            }

            AddHumanEntitiesFromFaces(detectionData, scene);
            AddHumanFromFullBody(processedFrameBgr, grayFrame, detectionData, scene);
            AddCatsFromCascade(grayFrame, detectionData, scene);

            scene.Entities = DedupeSceneEntities(scene.Entities);
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

        private void AddHumanFromFullBody(Mat processedFrameBgr, Mat grayFrame, DetectionData detectionData, SceneAnalysisResult scene)
        {
            if (_fullbody == null || _fullbody.Empty())
                return;

            try
            {
                var bodies = _fullbody.DetectMultiScale(grayFrame, 1.1, 3, HaarDetectionTypes.ScaleImage, new Size(40, 40));
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

        private void AddCatsFromCascade(Mat grayFrame, DetectionData detectionData, SceneAnalysisResult scene)
        {
            if (_catFaceCascade == null || _catFaceCascade.Empty())
                return;
            if (scene.Entities.Any(e => e.Label == "cat" && e.Source == "ssd"))
                return;

            try
            {
                var cats = _catFaceCascade.DetectMultiScale(grayFrame, 1.1, 3, HaarDetectionTypes.ScaleImage, new Size(20, 20));
                foreach (var r in cats)
                {
                    var bb = BBoxFromRect(r);
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
