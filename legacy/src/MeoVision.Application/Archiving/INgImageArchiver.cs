using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using MeoVision.Core.Contracts;

namespace MeoVision.Application.Archiving;

public interface INgImageArchiver
{
    Task<string?> ArchiveIfNeededAsync(
        Bitmap image, VisionOutcome outcome, string correlationId, CancellationToken ct = default);
}
