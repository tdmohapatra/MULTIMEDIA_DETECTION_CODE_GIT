using STAR_MUTIMEDIA.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace STAR_MUTIMEDIA.Services
{
    public interface IRealTimeDetectionService
    {
        Task<DetectionResult> ProcessFrameAsync(FrameData frameData);
        Task<EnhancedDetectionResult> ProcessEnhancedFrameAsync(EnhancedFrameData frameData);

        // Basic session management
        DetectionStats GetSessionStats(string sessionId);
        SessionAnalytics GetSessionAnalytics(string sessionId);
        void UpdateSessionSettings(string sessionId, DetectionSettings settings);
        void InitializeSession(string sessionId);
        void CleanupSession(string sessionId);
        List<string> GetActiveSessions();

        // New enhanced methods
        MonitoringConfiguration GetMonitoringConfiguration(string sessionId);
        void UpdateMonitoringConfiguration(string sessionId, MonitoringConfiguration config);
        FrameRateInfo GetFrameRateInfo(string sessionId);
        CameraMovementAnalysis GetCameraMovementAnalysis(string sessionId);
        List<MonitoringOption> GetAvailableMonitoringOptions();

        // Performance control
        void SetTargetFPS(string sessionId, double targetFPS);
        void EnableMonitoringOption(string sessionId, string optionName, bool enable);
    }
}