using System;
using System.Collections.Generic;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Utils
{
    public static class Slope
    {
        // Calculates the normalized regression slope of the last N values in the series.
        public static double Calculate(IReadOnlyList<double> series, int lookback, double tolerance = 1e-6)
        {
            if (series == null || series.Count < 2 || lookback < 2)
                return 0.0;

            int n = Math.Min(lookback, series.Count);

            double sumX = 0.0, sumY = 0.0, sumXY = 0.0, sumXX = 0.0;

            // Only take the last n points
            for (int i = 0; i < n; i++)
            {
                double y = series[series.Count - n + i];
                sumX += i;
                sumY += y;
                sumXY += i * y;
                sumXX += i * i;
            }

            double denom = n * sumXX - sumX * sumX;
            if (Math.Abs(denom) < tolerance)
                return 0.0;

            double slope = (n * sumXY - sumX * sumY) / denom;

            // Normalize by mean of Y
            double mean = sumY / n;
            if (Math.Abs(mean) < tolerance)
                return 0.0;

            return Common.Round(slope / mean);
        }
    }
}
