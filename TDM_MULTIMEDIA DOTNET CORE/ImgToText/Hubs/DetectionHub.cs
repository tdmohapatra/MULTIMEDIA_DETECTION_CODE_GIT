using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using STAR_MUTIMEDIA.Models;
using STAR_MUTIMEDIA.Services;
using System;
using System.Threading.Tasks;

namespace STAR_MUTIMEDIA.Hubs
{
    public class DetectionHub : Hub
    {
        private readonly IRealTimeDetectionService _detectionService;
        private readonly ILogger<DetectionHub> _logger;

        public DetectionHub(
            IRealTimeDetectionService detectionService,
            ILogger<DetectionHub> logger)
        {
            _detectionService = detectionService ?? throw new ArgumentNullException(nameof(detectionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task JoinSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Task.CompletedTask;
            }

            return Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        }

        public Task LeaveSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Task.CompletedTask;
            }

            return Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        }

        public async Task<DetectionResult> ProcessFrame(FrameData frameData)
        {
            if (frameData == null || string.IsNullOrWhiteSpace(frameData.ImageData))
            {
                throw new HubException("Frame payload is required.");
            }

            if (string.IsNullOrWhiteSpace(frameData.SessionId))
            {
                frameData.SessionId = Context.ConnectionId;
            }

            var result = await _detectionService.ProcessFrameAsync(frameData);

            await Clients.Group(frameData.SessionId).SendAsync("detectionUpdate", new
            {
                sessionId = frameData.SessionId,
                timestamp = DateTime.UtcNow,
                result
            });

            _logger.LogDebug("SignalR frame processed for {SessionId}", frameData.SessionId);
            return result;
        }
    }
}
