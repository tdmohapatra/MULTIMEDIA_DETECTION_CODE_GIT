using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using STAR_MUTIMEDIA.Models.Observability;

namespace STAR_MUTIMEDIA.Services.Observability
{
    public interface IApplicationLoggingService
    {
        void LogEvent(string category, string message, LogLevel level = LogLevel.Information, Dictionary<string, object>? metadata = null);
        IReadOnlyList<ApplicationLogEntry> GetRecent(int count = 100);
    }
}
