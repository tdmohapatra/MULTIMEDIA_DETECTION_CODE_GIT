using ImgToText.Models;
using ImgToText.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using STAR_MUTIMEDIA.Models;
using STAR_MUTIMEDIA.Services;
using System;
using System.Threading.Tasks;

namespace STAR_MUTIMEDIA.Controllers
{
  

    [ApiController]
    [Route("api/[controller]")]
    public class DetectionController_test : ControllerBase
    {
        private readonly IRealTimeDetectionService_test _detectionService;

        public DetectionController_test(IRealTimeDetectionService_test detectionService)
        {
            _detectionService = detectionService;
        }

     
        [HttpPost("process-frame")]
        public async Task<ActionResult<DetectionResult>> ProcessFrame([FromBody] FrameData frameData)
        {
            try
            {
                if (string.IsNullOrEmpty(frameData.SessionId))
                {
                    frameData.SessionId = HttpContext.Session.Id;
                }

                var result = await _detectionService.ProcessFrameAsync(frameData);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("stats/{sessionId}")]
        public ActionResult<DetectionStats> GetStats(string sessionId)
        {
            try
            {
                var stats = _detectionService.GetSessionStats(sessionId);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("settings/{sessionId}")]
        public IActionResult UpdateSettings(string sessionId, [FromBody] DetectionSettings settings)
        {
            try
            {
                _detectionService.UpdateSessionSettings(sessionId, settings);
                return Ok(new { message = "Settings updated successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("initialize/{sessionId}")]
        public IActionResult InitializeSession(string sessionId)
        {
            try
            {
                _detectionService.InitializeSession(sessionId);
                return Ok(new { message = "Session initialized successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("cleanup/{sessionId}")]
        public IActionResult CleanupSession(string sessionId)
        {
            try
            {
                _detectionService.CleanupSession(sessionId);
                return Ok(new { message = "Session cleaned up successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}