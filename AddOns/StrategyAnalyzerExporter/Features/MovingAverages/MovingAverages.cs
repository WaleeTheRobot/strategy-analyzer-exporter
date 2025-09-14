using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Utils;
using System;
using System.Collections.Generic;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Features.MovingAverages
{
    public static class MovingAverages
    {
        public static double GetCloseMovingAverageDistance(DataBar dataBar, double tolerance = 1e-6)
        {
            if (dataBar == null)
                return 0.0;

            double movingAverage = dataBar.MovingAverage;
            double close = dataBar.Close;

            if (double.IsNaN(movingAverage) || double.IsInfinity(movingAverage) ||
                double.IsNaN(close) || double.IsInfinity(close))
            {
                return 0.0;
            }

            if (Math.Abs(movingAverage) < tolerance)
                return 0.0;

            double distance = (close - movingAverage) / movingAverage;

            return Common.Round(distance);
        }

        public static double GetMovingAverageAutocorrelation(
            IReadOnlyList<double> movingAverageSeries,
            int lag = 1)
        {
            return Common.Round(Common.CalculateAutocorrelation(movingAverageSeries, lag));
        }
    }
}
