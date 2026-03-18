using Microsoft.ML.Data;

namespace TrashMailPanda.Providers.ML.Training;

/// <summary>
/// Extension methods for <see cref="MulticlassClassificationMetrics"/> that compute
/// macro-averaged precision, recall, and F1.
/// ML.NET exposes macro accuracy but not macro precision/recall/F1 directly;
/// this class derives them from <c>ConfusionMatrix.PerClassPrecision</c> /
/// <c>ConfusionMatrix.PerClassRecall</c>.
/// </summary>
internal static class MulticlassMetricsExtensions
{
    /// <summary>
    /// Computes macro-averaged precision across all classes.
    /// </summary>
    public static double MacroPrecision(this MulticlassClassificationMetrics metrics)
    {
        var values = metrics.ConfusionMatrix.PerClassPrecision;
        return values.Count > 0 ? values.Average() : 0.0;
    }

    /// <summary>
    /// Computes macro-averaged recall across all classes.
    /// </summary>
    public static double MacroRecall(this MulticlassClassificationMetrics metrics)
    {
        var values = metrics.ConfusionMatrix.PerClassRecall;
        return values.Count > 0 ? values.Average() : 0.0;
    }

    /// <summary>
    /// Computes macro-averaged F1 score across all classes.
    /// </summary>
    public static double MacroF1(this MulticlassClassificationMetrics metrics)
    {
        var precision = metrics.ConfusionMatrix.PerClassPrecision;
        var recall = metrics.ConfusionMatrix.PerClassRecall;
        if (precision.Count == 0) return 0.0;

        var f1Values = Enumerable.Range(0, precision.Count).Select(i =>
        {
            var p = precision[i];
            var r = recall[i];
            var denom = p + r;
            return denom > 0 ? 2 * p * r / denom : 0.0;
        });

        return f1Values.Average();
    }
}
