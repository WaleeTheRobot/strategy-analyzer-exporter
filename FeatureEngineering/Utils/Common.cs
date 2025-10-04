using System;
using System.Collections.Generic;

namespace FeatureEngineering.Utils;

public static class Common
{
    public static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    // Calculates the percentage change from start to end of the series window
    public static double CalculateSlope(IReadOnlyList<double> series, double tolerance = 1e-6)
    {
        if (series == null || series.Count < 2)
            return 0.0;

        double seriesStart = series[0];
        double seriesEnd = series[series.Count - 1];

        // Check for invalid values
        if (Math.Abs(seriesStart) < tolerance)
            return 0.0;

        return ((seriesEnd - seriesStart) / seriesStart) * 100.0;
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
