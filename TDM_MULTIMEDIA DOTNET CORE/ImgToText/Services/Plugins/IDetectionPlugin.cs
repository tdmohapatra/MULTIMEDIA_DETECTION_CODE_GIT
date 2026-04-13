using STAR_MUTIMEDIA.Models;
using System.Collections.Generic;

namespace STAR_MUTIMEDIA.Services.Plugins
{
    internal interface IDetectionPlugin
    {
        string Name { get; }
        void Evaluate(
            SessionData session,
            DetectionData detectionData,
            List<DetectionNotification> notifications,
            List<SystemLog> logs);
    }
}
