// Services/IRealTimeDetectionService.cs
using ImgToText.Models;
using STAR_MUTIMEDIA.Models;
using System;
using System.Threading.Tasks;

namespace STAR_MUTIMEDIA.Services
{
    public interface IRealTimeDetectionService_test
    {
        Task<DetectionResult> ProcessFrameAsync(FrameData frameData);
        DetectionStats GetSessionStats(string sessionId);
        void UpdateSessionSettings(string sessionId, DetectionSettings settings);
        void InitializeSession(string sessionId);
        void CleanupSession(string sessionId);
    }
}