using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Utils;
using System;
using System.Collections.Generic;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Features.MovingAverages
{
    public static class MovingAverages
    {
        public static (double MovingAverageDistance, double MovingAverageSlope, double MovingAverageAutocorrelation)
            GetMovingAverageFeatures(List<DataBar> dataBars, int lookbackPeriod = 9)
        {
            if (dataBars == null || dataBars.Count == 0) return (0.0, 0.0, 0.0);

            var lastBar = dataBars[dataBars.Count - 1];
            var distance = GetCloseMovingAverageDistance(lastBar);
            var maSeries = SeriesExtractor.ExtractSeries(dataBars, SeriesExtractor.Field.MovingAverage, lookbackPeriod);

            var slope = Slope.Calculate(maSeries);
            var autoCorrelation = GetMovingAverageAutocorrelation(maSeries, lag: 1);

            return (distance, slope, autoCorrelation);
        }

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

            return (close - movingAverage) / movingAverage;
        }

        public static double GetMovingAverageAutocorrelation(
            IReadOnlyList<double> movingAverageSeries,
            int lag = 1)
        {
            return Common.CalculateAutocorrelation(movingAverageSeries, lag);
        }
    }
}
