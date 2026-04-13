using STAR_MUTIMEDIA.Models;
using System;
using System.Collections.Generic;

namespace STAR_MUTIMEDIA.Services.Plugins
{
    internal class LowLightDetectionPlugin : IDetectionPlugin
    {
        public string Name => "LowLightDetection";

        public void Evaluate(SessionData session, DetectionData detectionData, List<DetectionNotification> notifications, List<SystemLog> logs)
        {
            if (session.Stats.AverageBrightness >= session.Settings.LowLightThreshold)
            {
                return;
            }

            notifications.Add(new DetectionNotification
            {
                Type = "LowLight",
                Category = "Environment",
                Severity = "Warning",
                Message = $"Low light detected ({session.Stats.AverageBrightness:0.0}). Improve lighting for better accuracy.",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    internal class NoHandDetectionPlugin : IDetectionPlugin
    {
        public string Name => "NoHandDetection";

        public void Evaluate(SessionData session, DetectionData detectionData, List<DetectionNotification> notifications, List<SystemLog> logs)
        {
            if (!session.Settings.EnableHandDetection || detectionData.Hands.Count > 0)
            {
                return;
            }

            notifications.Add(new DetectionNotification
            {
                Type = "NoHandDetected",
                Category = "Tracking",
                Severity = "Info",
                Message = "No hand detected. Bring your hand inside the camera frame.",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    internal class MultipleFacesDetectionPlugin : IDetectionPlugin
    {
        public string Name => "MultipleFacesDetection";

        public void Evaluate(SessionData session, DetectionData detectionData, List<DetectionNotification> notifications, List<SystemLog> logs)
        {
            if (detectionData.Faces.Count <= 1)
            {
                return;
            }

            notifications.Add(new DetectionNotification
            {
                Type = "MultipleFaces",
                Category = "Tracking",
                Severity = "Warning",
                Message = $"Multiple faces detected ({detectionData.Faces.Count}). Primary subject tracking may degrade.",
                Timestamp = DateTime.UtcNow
            });
        }
    }
}
