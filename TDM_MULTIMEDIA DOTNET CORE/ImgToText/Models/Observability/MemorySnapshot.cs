using System;

namespace STAR_MUTIMEDIA.Models.Observability
{
    public class MemorySnapshot
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public long WorkingSetBytes { get; set; }
        public long PrivateMemoryBytes { get; set; }
        public long ManagedHeapBytes { get; set; }
        public long PeakWorkingSetBytes { get; set; }
        public int ThreadCount { get; set; }
        public string Source { get; set; } = "unknown";

        public double WorkingSetMb => WorkingSetBytes / (1024d * 1024d);
        public double ManagedHeapMb => ManagedHeapBytes / (1024d * 1024d);
        public double PrivateMemoryMb => PrivateMemoryBytes / (1024d * 1024d);
    }
}
