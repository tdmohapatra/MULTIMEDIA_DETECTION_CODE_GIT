using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using STAR_MUTIMEDIA.Models.Observability;

namespace STAR_MUTIMEDIA.Services.Observability
{
    public class ApplicationLoggingService : IApplicationLoggingService
    {
        private const int MaxInMemoryEntries = 2000;
        private readonly ConcurrentQueue<ApplicationLogEntry> _entries = new ConcurrentQueue<ApplicationLogEntry>();
        private readonly object _fileLock = new object();
        private readonly string _logDirectory;
        private readonly ILogger<ApplicationLoggingService> _logger;

        public ApplicationLoggingService(ILogger<ApplicationLoggingService> logger)
        {
            _logger = logger;
            _logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs", "observability");
            Directory.CreateDirectory(_logDirectory);
        }

        public void LogEvent(string category, string message, LogLevel level = LogLevel.Information, Dictionary<string, object>? metadata = null)
        {
            var entry = new ApplicationLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Category = category ?? "General",
                Message = message ?? string.Empty,
                Level = level.ToString(),
                Metadata = metadata ?? new Dictionary<string, object>()
            };

            _entries.Enqueue(entry);
            while (_entries.Count > MaxInMemoryEntries && _entries.TryDequeue(out _))
            {
                // keep bounded memory
            }

            _logger.Log(level, "[{Category}] {Message}", entry.Category, entry.Message);
            PersistEntry(entry);
        }

        public IReadOnlyList<ApplicationLogEntry> GetRecent(int count = 100)
        {
            if (count <= 0) count = 1;
            return _entries.Reverse().Take(count).ToList();
        }

        private void PersistEntry(ApplicationLogEntry entry)
        {
            try
            {
                var path = Path.Combine(_logDirectory, $"app-events-{DateTime.UtcNow:yyyyMMdd}.jsonl");
                var payload = JsonSerializer.Serialize(entry);
                lock (_fileLock)
                {
                    File.AppendAllText(path, payload + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist observability entry.");
            }
        }
    }
}
