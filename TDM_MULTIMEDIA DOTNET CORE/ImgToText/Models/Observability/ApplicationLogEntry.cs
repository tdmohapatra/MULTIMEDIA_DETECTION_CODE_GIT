using System;
using System.Collections.Generic;

namespace STAR_MUTIMEDIA.Models.Observability
{
    public class ApplicationLogEntry
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string Level { get; set; } = "Information";
        public string Category { get; set; } = "General";
        public string Message { get; set; } = "";
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}
