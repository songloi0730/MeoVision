using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MeoVision.Application.Results;

public interface IInspectionResultStore
{
    Task AddAsync(InspectionLogEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionLogEntry>> GetRecentAsync(int maxCount, CancellationToken ct = default);
}
