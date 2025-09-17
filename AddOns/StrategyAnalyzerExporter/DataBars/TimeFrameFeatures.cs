using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Features.MovingAverages;
using NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.Features.Price;
using System.Collections.Generic;

namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars
{
    public class TimeFrameFeatures
    {
        public double MovingAverageDistance { get; set; }
        public double MovingAverageSlope { get; set; }
        public double MovingAverageAutocorrelation { get; set; }
        public double OpenLocationValue { get; set; }
        public double CloseLocationValue { get; set; }

        // Add more features...

        public static TimeFrameFeatures ExtractFrom(List<DataBar> dataBars, DataBarsConfig config)
        {
            var (maDistance, maSlope, maAutocorr) = MovingAverages.GetMovingAverageFeatures(dataBars, config.LookbackPeriod);
            var (olv, clv) = Price.GetPriceFeatures(dataBars);

            // Calculate more features...

            return new TimeFrameFeatures
            {
                MovingAverageDistance = maDistance,
                MovingAverageSlope = maSlope,
                MovingAverageAutocorrelation = maAutocorr,
                OpenLocationValue = olv,
                CloseLocationValue = clv,
            };
        }
    }
}
