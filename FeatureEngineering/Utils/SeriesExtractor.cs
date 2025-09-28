using System.Collections.Generic;

namespace FeatureEngineering.Utils;


public static class SeriesExtractor
{
    public enum Field { Open, High, Low, Close, Volume, MovingAverage, SlowMovingAverage }

    public static void FillSeries(List<BaseBar> bars, Field field, int lookback, List<double> dest)
    {
        dest.Clear();
        if (bars == null || bars.Count == 0) return;

        int count = (lookback > 0 && bars.Count > lookback) ? lookback : bars.Count;
        int start = bars.Count - count;

        if (dest.Capacity < count) dest.Capacity = count;

        for (int i = 0; i < count; i++)
        {
            var b = bars[start + i];
            double val = field switch
            {
                Field.Open => b.Open,
                Field.High => b.High,
                Field.Low => b.Low,
                Field.Close => b.Close,
                Field.Volume => b.Volume,
                Field.MovingAverage => b.MovingAverage,
                Field.SlowMovingAverage => b.SlowMovingAverage,
                _ => 0.0
            };
            dest.Add(val);
        }
    }

    public static List<double> ExtractSeries(List<BaseBar> bars, Field field, int lookback = 0)
    {
        var tmp = new List<double>();
        FillSeries(bars, field, lookback, tmp);
        return tmp;
    }
}
