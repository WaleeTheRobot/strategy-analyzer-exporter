using System;
using System.Collections.Generic;

namespace FeatureEngineering.Utils;

public static class Common
{
    public static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    // Calculates the slope normalized by the recent range of the series
    public static double CalculateSlope(IReadOnlyList<double> series, double tolerance = 1e-6)
    {
        if (series == null || series.Count < 2)
            return 0.0;

        int n = series.Count;

        double sumX = 0.0, sumY = 0.0, sumXY = 0.0, sumXX = 0.0;
        double minValue = series[0];
        double maxValue = series[0];

        for (int i = 0; i < n; i++)
        {
            double y = series[i];

            // Linear regression calculations
            sumX += i;
            sumY += y;
            sumXY += i * y;
            sumXX += i * i;

            // Min/max tracking
            if (y < minValue) minValue = y;
            if (y > maxValue) maxValue = y;
        }

        double denom = n * sumXX - sumX * sumX;
        if (Math.Abs(denom) < tolerance)
            return 0.0;

        double slope = (n * sumXY - sumX * sumY) / denom;
        double range = maxValue - minValue;

        if (range < tolerance)
            return 0.0;

        return slope / range;
    }

    public static double CalculateAutocorrelation(
        IReadOnlyList<double> series,
        int lag = 1,
        double tolerance = 1e-6)
    {
        if (series == null || series.Count <= lag)
            return 0.0;

        int n = series.Count;

        // Calculate mean
        double sum = 0.0;
        for (int i = 0; i < n; i++)
            sum += series[i];
        double mean = sum / n;

        // Calculate numerator and denominator
        double num = 0.0, den = 0.0;
        for (int i = 0; i < n; i++)
        {
            double d = series[i] - mean;
            den += d * d;

            if (i >= lag)
            {
                double dLag = series[i - lag] - mean;
                num += d * dLag;
            }
        }

        return Math.Abs(den) < tolerance ? 0.0 : num / den;
    }
}
