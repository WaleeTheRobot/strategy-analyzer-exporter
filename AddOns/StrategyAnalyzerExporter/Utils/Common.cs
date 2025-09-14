using System;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Utils
{
    public static class Common
    {
        // Single function to round value
        public static double Round(double value)
        {
            return Math.Round(value, 6, MidpointRounding.AwayFromZero);
        }
    }
}
