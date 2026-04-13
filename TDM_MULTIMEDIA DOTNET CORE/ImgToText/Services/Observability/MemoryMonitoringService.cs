using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using STAR_MUTIMEDIA.Models.Observability;

namespace STAR_MUTIMEDIA.Services.Observability
{
    public class MemoryMonitoringService : IMemoryMonitoringService
    {
        private const int MaxSnapshots = 2048;
        private readonly ConcurrentQueue<MemorySnapshot> _snapshots = new ConcurrentQueue<MemorySnapshot>();

        public MemorySnapshot CaptureSnapshot(string source = "manual")
        {
            var proc = Process.GetCurrentProcess();
            var snapshot = new MemorySnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                Source = source ?? "manual",
                WorkingSetBytes = proc.WorkingSet64,
                PrivateMemoryBytes = proc.PrivateMemorySize64,
                PeakWorkingSetBytes = proc.PeakWorkingSet64,
                ManagedHeapBytes = GC.GetTotalMemory(false),
                ThreadCount = proc.Threads.Count
            };

            _snapshots.Enqueue(snapshot);
            while (_snapshots.Count > MaxSnapshots && _snapshots.TryDequeue(out _))
            {
                // keep bounded memory
            }

            return snapshot;
        }

        public MemorySnapshot GetLatest()
        {
            if (_snapshots.TryPeek(out var existing))
            {
                return existing;
            }

            return CaptureSnapshot("initial");
        }

        public IReadOnlyList<MemorySnapshot> GetRecent(int count = 120)
        {
            if (count <= 0) count = 1;
            return _snapshots.Reverse().Take(count).ToList();
        }
    }
}
