using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using STAR_MUTIMEDIA.Models;
using STAR_MUTIMEDIA.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace STAR_MUTIMEDIA.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DetectionController : ControllerBase
    {
        private readonly IRealTimeDetectionService _detectionService;
        private readonly ILogger<DetectionController> _logger;

        public DetectionController(
            IRealTimeDetectionService detectionService,
            ILogger<DetectionController> logger)
        {
            _detectionService = detectionService ?? throw new ArgumentNullException(nameof(detectionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost("process-frame")]
        public async Task<ActionResult<DetectionResult>> ProcessFrame([FromBody] FrameData frameData)
        {
            try
            {
                if (frameData == null)
                {
                    _logger.LogWarning("ProcessFrame called with null frame data");
                    return BadRequest(new { error = "Frame data is required" });
                }

                // Validate required fields
                if (string.IsNullOrWhiteSpace(frameData.ImageData))
                {
                    return BadRequest(new { error = "Image data is required" });
                }

                if (string.IsNullOrEmpty(frameData.SessionId))
                {
                    // Generate a new session ID if not provided
                    frameData.SessionId = Guid.NewGuid().ToString();
                    _logger.LogInformation("Generated new session ID: {SessionId}", frameData.SessionId);
                }

                _logger.LogDebug("Processing frame for session {SessionId}, frame {FrameNumber}",
                    frameData.SessionId, frameData.FrameNumber);

                var result = await _detectionService.ProcessFrameAsync(frameData);

                _logger.LogDebug("Frame processed successfully for session {SessionId}", frameData.SessionId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing frame for session {SessionId}", frameData?.SessionId);
                return StatusCode(500, new { error = "Internal server error processing frame" });
            }
        }

        [HttpGet("stats/{sessionId}")]
        public ActionResult<DetectionStats> GetStats(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest(new { error = "Session ID is required" });
                }

                _logger.LogDebug("Getting stats for session {SessionId}", sessionId);
                var stats = _detectionService.GetSessionStats(sessionId);

                if (stats == null)
                {
                    _logger.LogWarning("Stats not found for session {SessionId}", sessionId);
                    return NotFound(new { error = "Session not found" });
                }

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting stats for session {SessionId}", sessionId);
                return StatusCode(500, new { error = "Internal server error retrieving stats" });
            }
        }

        [HttpPost("settings/{sessionId}")]
        public IActionResult UpdateSettings(string sessionId, [FromBody] DetectionSettings settings)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest(new { error = "Session ID is required" });
                }

                if (settings == null)
                {
                    return BadRequest(new { error = "Settings data is required" });
                }

                _logger.LogDebug("Updating settings for session {SessionId}", sessionId);
                _detectionService.UpdateSessionSettings(sessionId, settings);

                return Ok(new { message = "Settings updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating settings for session {SessionId}", sessionId);
                return StatusCode(500, new { error = "Internal server error updating settings" });
            }
        }

        [HttpPost("initialize/{sessionId}")]
        public IActionResult InitializeSession(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest(new { error = "Session ID is required" });
                }

                _logger.LogInformation("Initializing session {SessionId}", sessionId);
                _detectionService.InitializeSession(sessionId);

                return Ok(new
                {
                    message = "Session initialized successfully",
                    sessionId = sessionId,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing session {SessionId}", sessionId);
                return StatusCode(500, new { error = "Internal server error initializing session" });
            }
        }

        [HttpPost("cleanup/{sessionId}")]
        public IActionResult CleanupSession(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest(new { error = "Session ID is required" });
                }

                _logger.LogInformation("Cleaning up session {SessionId}", sessionId);
                _detectionService.CleanupSession(sessionId);

                return Ok(new
                {
                    message = "Session cleaned up successfully",
                    sessionId = sessionId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up session {SessionId}", sessionId);
                return StatusCode(500, new { error = "Internal server error cleaning up session" });
            }
        }

        [HttpGet("active-sessions")]
        public ActionResult<List<string>> GetActiveSessions()
        {
            try
            {
                var sessions = _detectionService.GetActiveSessions();
                _logger.LogDebug("Retrieved {SessionCount} active sessions", sessions.Count);

                return Ok(sessions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving active sessions");
                return StatusCode(500, new { error = "Internal server error retrieving active sessions" });
            }
        }

        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            try
            {
                var activeSessions = _detectionService.GetActiveSessions();

                var healthInfo = new
                {
                    status = "Healthy",
                    activeSessions = activeSessions.Count,
                    timestamp = DateTime.UtcNow,
                    service = "DetectionController",
                    version = "1.0.0"
                };

                _logger.LogDebug("Health check - Active sessions: {ActiveSessions}", activeSessions.Count);
                return Ok(healthInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return StatusCode(500, new
                {
                    status = "Unhealthy",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("monitoring-options")]
        public ActionResult<List<MonitoringOption>> GetMonitoringOptions()
        {
            try
            {
                var options = _detectionService.GetAvailableMonitoringOptions();
                return Ok(options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving monitoring options");
                return StatusCode(500, new { error = "Internal server error retrieving monitoring options" });
            }
        }

        [HttpGet("monitoring-config/{sessionId}")]
        public ActionResult<MonitoringConfiguration> GetMonitoringConfig(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest(new { error = "Session ID is required" });
                }

                var config = _detectionService.GetMonitoringConfiguration(sessionId);

                if (config == null)
                {
                    return NotFound(new { error = "Monitoring configuration not found for session" });
                }

                return Ok(config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving monitoring config for session {SessionId}", sessionId);
                return StatusCode(500, new { error = "Internal server error retrieving monitoring configuration" });
            }
        }

        [HttpPost("monitoring-config/{sessionId}")]
        public IActionResult UpdateMonitoringConfig(string sessionId, [FromBody] MonitoringConfiguration config)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest(new { error = "Session ID is required" });
                }

                if (config == null)
                {
                    return BadRequest(new { error = "Configuration data is required" });
                }

                _detectionService.UpdateMonitoringConfiguration(sessionId, config);
                _logger.LogInformation("Updated monitoring config for session {SessionId}", sessionId);

                return Ok(new { message = "Monitoring configuration updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating monitoring config for session {SessionId}", sessionId);
                return StatusCode(500, new { error = "Internal server error updating monitoring configuration" });
            }
        }

        [HttpGet("frame-rate/{sessionId}")]
        public ActionResult<FrameRateInfo> GetFrameRateInfo(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest(new { error = "Session ID is required" });
                }

                var info = _detectionService.GetFrameRateInfo(sessionId);

                if (info == null)
                {
                    return NotFound(new { error = "Frame rate info not found for session" });
                }

                return Ok(info);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving frame rate info for session {SessionId}", sessionId);
                return StatusCode(500, new { error = "Internal server error retrieving frame rate information" });
            }
        }

        [HttpPost("frame-rate/{sessionId}")]
        public IActionResult SetTargetFPS(string sessionId, [FromBody] FrameRateRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest(new { error = "Session ID is required" });
                }

                if (request == null || request.TargetFPS <= 0 || request.TargetFPS > 60)
                {
                    return BadRequest(new { error = "Target FPS must be between 1 and 60" });
                }

                _detectionService.SetTargetFPS(sessionId, request.TargetFPS);
                _logger.LogInformation("Set target FPS to {TargetFPS} for session {SessionId}", request.TargetFPS, sessionId);

                return Ok(new
                {
                    message = $"Target FPS set to {request.TargetFPS}",
                    targetFPS = request.TargetFPS
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting target FPS for session {SessionId}", sessionId);
                return StatusCode(500, new { error = "Internal server error setting target FPS" });
            }
        }

        [HttpGet("camera-movement/{sessionId}")]
        public ActionResult<CameraMovementAnalysis> GetCameraMovementAnalysis(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest(new { error = "Session ID is required" });
                }

                var analysis = _detectionService.GetCameraMovementAnalysis(sessionId);

                if (analysis == null)
                {
                    return NotFound(new { error = "Camera movement analysis not found for session" });
                }

                return Ok(analysis);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving camera movement analysis for session {SessionId}", sessionId);
                return StatusCode(500, new { error = "Internal server error retrieving camera movement analysis" });
            }
        }
    }

    // Request/Response DTOs
    public class FrameRateRequest
    {
        [Required]
        [Range(1, 60)]
        public double TargetFPS { get; set; }
    }

    public class EnableOptionRequest
    {
        [Required]
        public string OptionName { get; set; }

        [Required]
        public bool Enable { get; set; }
    }
}

//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Http;
//using System;
//using System.Threading.Tasks;
//using STAR_MUTIMEDIA.Models;
//using STAR_MUTIMEDIA.Services;
//using System.Collections.Generic;
//using Microsoft.AspNetCore.Http;
//using System;
//using System.Threading.Tasks;
//namespace STAR_MUTIMEDIA.Controllers
//{
//    [ApiController]
//    [Route("api/[controller]")]
//    public class DetectionController : ControllerBase
//    {
//        private readonly IRealTimeDetectionService _detectionService;

//        public DetectionController(IRealTimeDetectionService detectionService)
//        {
//            _detectionService = detectionService ?? throw new ArgumentNullException(nameof(detectionService));
//        }


//        [HttpPost("process-frame")]
//        public async Task<ActionResult<DetectionResult>> ProcessFrame([FromBody] FrameData frameData)
//        {
//            try
//            {
//                if (frameData == null)
//                    return BadRequest(new { error = "Frame data is required" });

//                if (string.IsNullOrEmpty(frameData.SessionId))
//                {
//                    frameData.SessionId = HttpContext.Session.Id;
//                }

//                var result = await _detectionService.ProcessFrameAsync(frameData);
//                return Ok(result);
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        //[HttpPost("process-frame")]
//        //public async Task<ActionResult<DetectionResult>> ProcessFrame([FromBody] FrameData frameData)
//        //{
//        //    try
//        //    {
//        //        if (frameData == null)
//        //            return BadRequest(new { error = "Frame data is required" });

//        //        if (string.IsNullOrEmpty(frameData.SessionId))
//        //        {
//        //            frameData.SessionId = HttpContext.Session.Id;
//        //        }

//        //        var result = await _detectionService.ProcessFrameAsync(frameData);
//        //        return Ok(result);
//        //    }
//        //    catch (Exception ex)
//        //    {
//        //        return BadRequest(new { error = ex.Message });
//        //    }
//        //}

//        //[HttpPost("process-enhanced-frame")]
//        //public async Task<ActionResult<EnhancedDetectionResult>> ProcessEnhancedFrame([FromBody] EnhancedFrameData frameData)
//        //{
//        //    try
//        //    {
//        //        if (frameData == null)
//        //            return BadRequest(new { error = "Enhanced frame data is required" });

//        //        if (string.IsNullOrEmpty(frameData.SessionId))
//        //        {
//        //            frameData.SessionId = HttpContext.Session.Id;
//        //        }

//        //        var result = await _detectionService.ProcessEnhancedFrameAsync(frameData);
//        //        return Ok(result);
//        //    }
//        //    catch (Exception ex)
//        //    {
//        //        return BadRequest(new { error = ex.Message });
//        //    }
//        //}

//        [HttpGet("stats/{sessionId}")]
//        public ActionResult<DetectionStats> GetStats(string sessionId)
//        {
//            try
//            {
//                if (string.IsNullOrEmpty(sessionId))
//                    return BadRequest(new { error = "Session ID is required" });

//                var stats = _detectionService.GetSessionStats(sessionId);
//                return Ok(stats);
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        [HttpPost("settings/{sessionId}")]
//        public IActionResult UpdateSettings(string sessionId, [FromBody] DetectionSettings settings)
//        {
//            try
//            {
//                if (string.IsNullOrEmpty(sessionId))
//                    return BadRequest(new { error = "Session ID is required" });

//                _detectionService.UpdateSessionSettings(sessionId, settings);
//                return Ok(new { message = "Settings updated successfully" });
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        [HttpPost("initialize/{sessionId}")]
//        public IActionResult InitializeSession(string sessionId)
//        {
//            try
//            {
//                if (string.IsNullOrEmpty(sessionId))
//                    return BadRequest(new { error = "Session ID is required" });

//                _detectionService.InitializeSession(sessionId);
//                return Ok(new { message = "Session initialized successfully" });
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        [HttpPost("cleanup/{sessionId}")]
//        public IActionResult CleanupSession(string sessionId)
//        {
//            try
//            {
//                if (string.IsNullOrEmpty(sessionId))
//                    return BadRequest(new { error = "Session ID is required" });

//                _detectionService.CleanupSession(sessionId);
//                return Ok(new { message = "Session cleaned up successfully" });
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        [HttpGet("active-sessions")]
//        public ActionResult<List<string>> GetActiveSessions()
//        {
//            try
//            {
//                var sessions = _detectionService.GetActiveSessions();
//                return Ok(sessions);
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        [HttpGet("health")]
//        public IActionResult HealthCheck()
//        {
//            try
//            {
//                var activeSessions = _detectionService.GetActiveSessions();
//                return Ok(new
//                {
//                    status = "Healthy",
//                    activeSessions = activeSessions.Count,
//                    timestamp = DateTime.UtcNow
//                });
//            }
//            catch (Exception ex)
//            {
//                return StatusCode(500, new { status = "Unhealthy", error = ex.Message });
//            }
//        }

//        //[HttpGet("session-analytics/{sessionId}")]
//        //public ActionResult<SessionAnalytics> GetSessionAnalytics(string sessionId)
//        //{
//        //    try
//        //    {
//        //        if (string.IsNullOrEmpty(sessionId))
//        //            return BadRequest(new { error = "Session ID is required" });

//        //        var analytics = _detectionService.GetSessionAnalytics(sessionId);
//        //        if (analytics == null)
//        //            return NotFound(new { error = "Session not found" });

//        //        return Ok(analytics);
//        //    }
//        //    catch (Exception ex)
//        //    {
//        //        return BadRequest(new { error = ex.Message });
//        //    }
//        //}


//        [HttpGet("monitoring-options")]
//        public ActionResult<List<MonitoringOption>> GetMonitoringOptions()
//        {
//            try
//            {
//                var options = _detectionService.GetAvailableMonitoringOptions();
//                return Ok(options);
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        [HttpGet("monitoring-config/{sessionId}")]
//        public ActionResult<MonitoringConfiguration> GetMonitoringConfig(string sessionId)
//        {
//            try
//            {
//                if (string.IsNullOrEmpty(sessionId))
//                    return BadRequest(new { error = "Session ID is required" });

//                var config = _detectionService.GetMonitoringConfiguration(sessionId);
//                return Ok(config);
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        [HttpPost("monitoring-config/{sessionId}")]
//        public IActionResult UpdateMonitoringConfig(string sessionId, [FromBody] MonitoringConfiguration config)
//        {
//            try
//            {
//                if (string.IsNullOrEmpty(sessionId))
//                    return BadRequest(new { error = "Session ID is required" });

//                _detectionService.UpdateMonitoringConfiguration(sessionId, config);
//                return Ok(new { message = "Monitoring configuration updated successfully" });
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        [HttpGet("frame-rate/{sessionId}")]
//        public ActionResult<FrameRateInfo> GetFrameRateInfo(string sessionId)
//        {
//            try
//            {
//                if (string.IsNullOrEmpty(sessionId))
//                    return BadRequest(new { error = "Session ID is required" });

//                var info = _detectionService.GetFrameRateInfo(sessionId);
//                return Ok(info);
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        [HttpPost("frame-rate/{sessionId}")]
//        public IActionResult SetTargetFPS(string sessionId, [FromBody] double targetFPS)
//        {
//            try
//            {
//                if (string.IsNullOrEmpty(sessionId))
//                    return BadRequest(new { error = "Session ID is required" });

//                _detectionService.SetTargetFPS(sessionId, targetFPS);
//                return Ok(new { message = $"Target FPS set to {targetFPS}" });
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        [HttpGet("camera-movement/{sessionId}")]
//        public ActionResult<CameraMovementAnalysis> GetCameraMovementAnalysis(string sessionId)
//        {
//            try
//            {
//                if (string.IsNullOrEmpty(sessionId))
//                    return BadRequest(new { error = "Session ID is required" });

//                var analysis = _detectionService.GetCameraMovementAnalysis(sessionId);
//                return Ok(analysis);
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { error = ex.Message });
//            }
//        }

//        //[HttpPost("enable-option/{sessionId}")]
//        //public IActionResult EnableMonitoringOption(string sessionId, [FromBody] EnableOptionRequest request)
//        //{
//        //    try
//        //    {
//        //        if (string.IsNullOrEmpty(sessionId))
//        //            return BadRequest(new { error = "Session ID is required" });

//        //        _detectionService.EnableMonitoringOption(sessionId, request.OptionName, request.Enable);
//        //        return Ok(new { message = $"Option {request.OptionName} {(request.Enable ? "enabled" : "disabled")}" });
//        //    }
//        //    catch (Exception ex)
//        //    {
//        //        return BadRequest(new { error = ex.Message });
//        //    }
//        //}
//    }
//}