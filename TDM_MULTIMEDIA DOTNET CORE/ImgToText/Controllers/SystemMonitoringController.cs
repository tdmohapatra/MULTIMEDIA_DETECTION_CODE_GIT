using System;
using Microsoft.AspNetCore.Mvc;
using STAR_MUTIMEDIA.Services.Observability;

namespace STAR_MUTIMEDIA.Controllers
{
    [ApiController]
    [Route("api/system")]
    public class SystemMonitoringController : ControllerBase
    {
        private readonly IMemoryMonitoringService _memory;
        private readonly IApplicationLoggingService _logs;

        public SystemMonitoringController(IMemoryMonitoringService memory, IApplicationLoggingService logs)
        {
            _memory = memory;
            _logs = logs;
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            var latest = _memory.GetLatest();
            return Ok(new
            {
                status = "Healthy",
                timestampUtc = DateTime.UtcNow,
                memory = latest
            });
        }

        [HttpGet("memory")]
        public IActionResult Memory([FromQuery] int count = 120)
        {
            return Ok(new
            {
                latest = _memory.GetLatest(),
                recent = _memory.GetRecent(count)
            });
        }

        [HttpPost("memory/capture")]
        public IActionResult CaptureMemory()
        {
            var snapshot = _memory.CaptureSnapshot("manual");
            _logs.LogEvent("MemoryMonitoring", "Manual memory snapshot captured.");
            return Ok(snapshot);
        }

        [HttpGet("logs")]
        public IActionResult Logs([FromQuery] int count = 150)
        {
            return Ok(new
            {
                recent = _logs.GetRecent(count)
            });
        }
    }
}
