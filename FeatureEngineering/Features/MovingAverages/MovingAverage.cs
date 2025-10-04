using FeatureEngineering.Utils;
using System;
using System.Collections.Generic;

namespace FeatureEngineering.Features.MovingAverages;

public static partial class MovingAverages
{
    public static MovingAverageFeatures Compute(
        CircularBuffer<double> maFastSeries,
        CircularBuffer<double> maSlowSeries,
        BaseBar last,
        FeaturesBarConfig config)
    {
        if (maFastSeries?.Count == 0)
            return new MovingAverageFeatures();

        // Calculate all features in one pass where possible
        double fastDist = GetCloseMovingAverageDistance(last);
        double slowDist = GetCloseMovingAverageDistance(last, isSlow: true);

        // Only calculate autocorrelation if we have sufficient data
        double fastAuto = maFastSeries.Count > 1
            ? GetMovingAverageAutocorrelation(maFastSeries, 1)
            : 0.0;
        double slowAuto = maSlowSeries.Count > 1
            ? GetMovingAverageAutocorrelation(maSlowSeries, 1)
            : 0.0;

        double fastSlope = Common.CalculateSlope(maFastSeries, config.LookbackPeriodSlow);

        return new MovingAverageFeatures(fastDist, fastAuto, fastSlope, slowDist, slowAuto);
    }

    public static double GetCloseMovingAverageDistance(in BaseBar bar, bool isSlow = false, double tolerance = 1e-6)
    {
        double movingAverage = isSlow ? bar.SlowMovingAverage : bar.MovingAverage;
        double close = bar.Close;

        // Fast path: check for invalid values
        if (!IsValidPrice(movingAverage) || !IsValidPrice(close))
            return 0.0;

        if (Math.Abs(movingAverage) < tolerance)
            return 0.0;

        return ((close - movingAverage) / movingAverage) * 100.0;
    }

    public static double GetMovingAverageAutocorrelation(
        IReadOnlyList<double> movingAverageSeries,
        int lag = 1)
    {
        return Common.CalculateAutocorrelation(movingAverageSeries, lag);
    }

    // Helper method to reduce repeated NaN/Infinity checks
    private static bool IsValidPrice(double price)
    {
        return !double.IsNaN(price) && !double.IsInfinity(price);
    }
}
