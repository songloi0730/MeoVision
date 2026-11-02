using System.Runtime.CompilerServices;

// Cho phép test project gọi thẳng SimulatedEvaluator (internal, thuật toán tất định — không random)
// mà không cần public hoá ra ngoài IVisionEngine.
[assembly: InternalsVisibleTo("MeoVision.Engine.Simulated.Tests")]
