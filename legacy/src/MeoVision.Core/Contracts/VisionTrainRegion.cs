using System.Drawing;

namespace MeoVision.Core.Contracts;

public sealed record VisionTrainRegion(string PatternName, Rectangle Region, Bitmap TrainingImage);
