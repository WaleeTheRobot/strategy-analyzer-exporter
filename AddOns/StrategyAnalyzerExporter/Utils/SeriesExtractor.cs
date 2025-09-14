using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars;
using System;
using System.Collections.Generic;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Utils
{
    public static class SeriesExtractor
    {
        public enum Field
        {
            Open,
            High,
            Low,
            Close,
            Volume,
            MovingAverage
        }

        // Builds a series of doubles from a list of DataBars using the selected field.
        public static List<double> ExtractSeries(List<DataBar> bars, Field field, int lookback = 0)
        {
            var series = new List<double>();
            if (bars == null) return series;

            var barsToProcess = lookback > 0 && bars.Count > lookback
                ? bars.GetRange(bars.Count - lookback, lookback)
                : bars;

            foreach (var bar in barsToProcess)
            {
                if (bar == null) continue;
                double val = field switch
                {
                    Field.Open => bar.Open,
                    Field.High => bar.High,
                    Field.Low => bar.Low,
                    Field.Close => bar.Close,
                    Field.Volume => bar.Volume,
                    Field.MovingAverage => bar.MovingAverage,
                    _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
                };
                series.Add(val);
            }
            return series;
        }
    }
}
