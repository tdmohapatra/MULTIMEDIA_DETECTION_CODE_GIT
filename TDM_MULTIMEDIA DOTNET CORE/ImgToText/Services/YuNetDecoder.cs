using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace STAR_MUTIMEDIA.Services
{
    /// <summary>
    /// Manual decode of the raw face_detection_yunet ONNX outputs (cls/obj/bbox/kps at strides
    /// 8/16/32). OpenCvSharp does not expose cv::FaceDetectorYN, so this ports the anchor-free
    /// decode from OpenCV's own C++ implementation (modules/objdetect/src/face_detect.cpp,
    /// FaceDetectorYNImpl::postProcess) line for line: pad input to a multiple of 32, decode each
    /// pyramid level as score=sqrt(cls*obj), cx/cy/w/h relative to the anchor cell, and 5
    /// landmarks (right eye, left eye, nose tip, right mouth corner, left mouth corner) the same
    /// way, then NMS. Do not "simplify" the formulas below without re-checking against that file
    /// — small deviations silently produce plausible-looking but wrong boxes.
    /// </summary>
    internal static class YuNetDecoder
    {
        private static readonly int[] Strides = { 8, 16, 32 };
        private static readonly string[] OutputNames =
        {
            "cls_8", "cls_16", "cls_32", "obj_8", "obj_16", "obj_32",
            "bbox_8", "bbox_16", "bbox_32", "kps_8", "kps_16", "kps_32"
        };

        public struct DetectedFace
        {
            public Rect BBox;
            public float Score;
            /// <summary>5 points: right eye, left eye, nose tip, right mouth corner, left mouth corner.</summary>
            public PointF[] Landmarks;
        }

        public static List<DetectedFace> Detect(Net net, Mat bgrFrame, float scoreThreshold, float nmsThreshold, int targetLongSide = 320)
        {
            var results = new List<DetectedFace>();
            if (net == null || bgrFrame.Empty())
                return results;

            using var bgr = EnsureThreeChannelBgr(bgrFrame);

            var longSide = Math.Max(bgr.Width, bgr.Height);
            var scale = longSide <= targetLongSide ? 1.0 : (double)targetLongSide / longSide;
            var resizedW = Math.Max(1, (int)Math.Round(bgr.Width * scale));
            var resizedH = Math.Max(1, (int)Math.Round(bgr.Height * scale));

            using var resized = new Mat();
            Cv2.Resize(bgr, resized, new Size(resizedW, resizedH));

            const int divisor = 32;
            var padW = ((resizedW - 1) / divisor + 1) * divisor;
            var padH = ((resizedH - 1) / divisor + 1) * divisor;

            using var padded = new Mat();
            Cv2.CopyMakeBorder(resized, padded, 0, padH - resizedH, 0, padW - resizedW, BorderTypes.Constant, Scalar.All(0));

            using var blob = CvDnn.BlobFromImage(padded);
            net.SetInput(blob);

            var outputs = OutputNames.Select(_ => new Mat()).ToList();
            try
            {
                net.Forward(outputs, OutputNames);

                var boxes = new List<Rect>();
                var scores = new List<float>();
                var landmarksList = new List<PointF[]>();

                for (var i = 0; i < Strides.Length; i++)
                {
                    var stride = Strides[i];
                    var cols = padW / stride;
                    var rows = padH / stride;
                    var count = rows * cols;

                    var clsArr = ToFloatArray(outputs[i], count);
                    var objArr = ToFloatArray(outputs[3 + i], count);
                    var bboxArr = ToFloatArray(outputs[6 + i], count * 4);
                    var kpsArr = ToFloatArray(outputs[9 + i], count * 10);

                    for (var r = 0; r < rows; r++)
                    {
                        for (var c = 0; c < cols; c++)
                        {
                            var idx = r * cols + c;

                            var clsScore = Math.Clamp(clsArr[idx], 0f, 1f);
                            var objScore = Math.Clamp(objArr[idx], 0f, 1f);
                            var score = (float)Math.Sqrt(clsScore * objScore);
                            if (score < scoreThreshold)
                                continue;

                            var cx = (c + bboxArr[idx * 4 + 0]) * stride;
                            var cy = (r + bboxArr[idx * 4 + 1]) * stride;
                            var w = (float)Math.Exp(bboxArr[idx * 4 + 2]) * stride;
                            var h = (float)Math.Exp(bboxArr[idx * 4 + 3]) * stride;
                            var x1 = cx - w / 2f;
                            var y1 = cy - h / 2f;

                            var landmarks = new PointF[5];
                            for (var n = 0; n < 5; n++)
                            {
                                landmarks[n] = new PointF(
                                    (kpsArr[idx * 10 + 2 * n] + c) * stride,
                                    (kpsArr[idx * 10 + 2 * n + 1] + r) * stride);
                            }

                            boxes.Add(new Rect((int)Math.Round(x1), (int)Math.Round(y1), (int)Math.Round(w), (int)Math.Round(h)));
                            scores.Add(score);
                            landmarksList.Add(landmarks);
                        }
                    }
                }

                if (boxes.Count == 0)
                    return results;

                CvDnn.NMSBoxes(boxes, scores, scoreThreshold, nmsThreshold, out var picked);

                var invScale = scale <= 0 ? 1.0 : 1.0 / scale;
                foreach (var idx in picked)
                {
                    var b = boxes[idx];
                    var mappedBox = new Rect(
                        (int)Math.Round(b.X * invScale),
                        (int)Math.Round(b.Y * invScale),
                        (int)Math.Round(b.Width * invScale),
                        (int)Math.Round(b.Height * invScale));
                    var mappedLandmarks = landmarksList[idx]
                        .Select(p => new PointF((float)(p.X * invScale), (float)(p.Y * invScale)))
                        .ToArray();

                    results.Add(new DetectedFace
                    {
                        BBox = mappedBox,
                        Score = scores[idx],
                        Landmarks = mappedLandmarks
                    });
                }
            }
            finally
            {
                foreach (var m in outputs)
                    m.Dispose();
            }

            return results;
        }

        private static float[] ToFloatArray(Mat m, int count)
        {
            var arr = new float[count];
            Marshal.Copy(m.Data, arr, 0, count);
            return arr;
        }

        /// <summary>BlobFromImage requires 3 channels; webcam/PNG frames can arrive as BGRA (4) or gray (1).</summary>
        private static Mat EnsureThreeChannelBgr(Mat source)
        {
            if (source.Channels() == 3)
                return source.Clone();

            var bgr = new Mat();
            if (source.Channels() == 4)
                Cv2.CvtColor(source, bgr, ColorConversionCodes.BGRA2BGR);
            else if (source.Channels() == 1)
                Cv2.CvtColor(source, bgr, ColorConversionCodes.GRAY2BGR);
            else
                source.ConvertTo(bgr, MatType.CV_8UC3);
            return bgr;
        }
    }
}
