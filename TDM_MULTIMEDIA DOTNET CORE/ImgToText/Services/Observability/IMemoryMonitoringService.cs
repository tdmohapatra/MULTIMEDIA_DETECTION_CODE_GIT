using System.Collections.Generic;
using STAR_MUTIMEDIA.Models.Observability;

namespace STAR_MUTIMEDIA.Services.Observability
{
    public interface IMemoryMonitoringService
    {
        MemorySnapshot CaptureSnapshot(string source = "manual");
        MemorySnapshot GetLatest();
        IReadOnlyList<MemorySnapshot> GetRecent(int count = 120);
    }
}
