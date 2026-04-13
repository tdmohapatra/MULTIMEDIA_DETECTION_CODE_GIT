using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace STAR_MUTIMEDIA.Services.Observability
{
    public class MemoryMonitoringBackgroundService : BackgroundService
    {
        private readonly IMemoryMonitoringService _memory;
        private readonly IApplicationLoggingService _appLog;
        private readonly ILogger<MemoryMonitoringBackgroundService> _logger;
        private readonly int _samplingSeconds;
        private readonly double _warningThresholdMb;

        public MemoryMonitoringBackgroundService(
            IMemoryMonitoringService memory,
            IApplicationLoggingService appLog,
            IConfiguration config,
            ILogger<MemoryMonitoringBackgroundService> logger)
        {
            _memory = memory;
            _appLog = appLog;
            _logger = logger;
            _samplingSeconds = Math.Max(5, config.GetValue<int?>("Observability:MemorySamplingSeconds") ?? 10);
            _warningThresholdMb = Math.Max(128, config.GetValue<double?>("Observability:MemoryWarningMb") ?? 1024);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Memory monitoring background service started: {SamplingSeconds}s interval, warning at {ThresholdMb} MB", _samplingSeconds, _warningThresholdMb);

            while (!stoppingToken.IsCancellationRequested)
            {
                var s = _memory.CaptureSnapshot("background");

                if (s.WorkingSetMb >= _warningThresholdMb)
                {
                    _appLog.LogEvent(
                        category: "MemoryMonitoring",
                        message: $"High memory usage: {s.WorkingSetMb:F1} MB",
                        level: LogLevel.Warning,
                        metadata: new Dictionary<string, object>
                        {
                            ["workingSetMb"] = Math.Round(s.WorkingSetMb, 2),
                            ["managedHeapMb"] = Math.Round(s.ManagedHeapMb, 2),
                            ["privateMemoryMb"] = Math.Round(s.PrivateMemoryMb, 2),
                            ["threads"] = s.ThreadCount
                        });
                }

                await Task.Delay(TimeSpan.FromSeconds(_samplingSeconds), stoppingToken);
            }
        }
    }
}
