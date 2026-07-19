using System;
using System.Collections.Generic;
using STAR_MUTIMEDIA.Models;

namespace STAR_MUTIMEDIA.Services
{
    /// <summary>
    /// Greedy IoU tracker that assigns stable per-session TrackIds to scene entities across
    /// frames, so YOLO detections that flicker between frames read as one persistent object
    /// instead of a new detection each time. One instance lives per session (see SessionData).
    /// </summary>
    internal sealed class SceneEntityTracker
    {
        private sealed class Track
        {
            public int Id;
            public string Label;
            public BoundingBox BBox;
            public int MissedFrames;
        }

        private const double MatchIouThreshold = 0.3;
        private const int MaxMissedFrames = 8;

        private readonly List<Track> _tracks = new List<Track>();
        private int _nextId = 1;

        /// <summary>Mutates each entity's TrackId in place, matching against tracks from prior frames.</summary>
        public void Assign(List<SceneEntity> entities)
        {
            var candidates = new List<(Track Track, SceneEntity Entity, double Iou)>();
            foreach (var track in _tracks)
            {
                foreach (var entity in entities)
                {
                    if (!string.Equals(track.Label, entity.Label, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var iou = ComputeIoU(track.BBox, entity.BBox);
                    if (iou >= MatchIouThreshold)
                        candidates.Add((track, entity, iou));
                }
            }

            candidates.Sort((a, b) => b.Iou.CompareTo(a.Iou));

            var matchedTracks = new HashSet<Track>();
            var matchedEntities = new HashSet<SceneEntity>();
            foreach (var (track, entity, _) in candidates)
            {
                if (matchedTracks.Contains(track) || matchedEntities.Contains(entity))
                    continue;
                entity.TrackId = track.Id;
                track.BBox = entity.BBox;
                track.MissedFrames = 0;
                matchedTracks.Add(track);
                matchedEntities.Add(entity);
            }

            foreach (var entity in entities)
            {
                if (matchedEntities.Contains(entity))
                    continue;
                var track = new Track { Id = _nextId++, Label = entity.Label, BBox = entity.BBox, MissedFrames = 0 };
                _tracks.Add(track);
                entity.TrackId = track.Id;
            }

            foreach (var track in _tracks)
            {
                if (!matchedTracks.Contains(track))
                    track.MissedFrames++;
            }
            _tracks.RemoveAll(t => t.MissedFrames > MaxMissedFrames);
        }

        private static double ComputeIoU(BoundingBox a, BoundingBox b)
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
            var intersection = iw * ih;
            if (intersection <= 0)
                return 0;
            var union = a.Width * a.Height + b.Width * b.Height - intersection;
            return union <= 0 ? 0 : intersection / union;
        }
    }
}
