using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Utils;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Features.Price
{
    public static class Price
    {
        public static double GetCloseLocationValue(DataBar bar, double tolerance = 1e-6)
        {
            if (bar == null)
                return 0.0;

            double high = bar.High;
            double low = bar.Low;
            double close = bar.Close;

            double range = high - low;
            if (range < tolerance)
                return 0.0;

            return Common.Round(((2 * close) - high - low) / range);
        }

        public static double GetOpenLocationValue(DataBar bar, double tolerance = 1e-6)
        {
            if (bar == null)
                return 0.0;

            double high = bar.High;
            double low = bar.Low;
            double open = bar.Open;

            double range = high - low;
            if (range < tolerance)
                return 0.0;

            return Common.Round(((2 * open) - high - low) / range);
        }
    }
}
