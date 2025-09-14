namespace NinjaTrader.Custom.AddOns.StrategyAnalyzerExporter.DataBars
{
    public class FeaturesDataBar
    {
        public int Time { get; set; }
        public int Day { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double Volume { get; set; }
        // Primary
        public double F_PrimaryMovingAverageDistance { get; set; }
        public double F_PrimaryMovingAverageSlope { get; set; }
        public double F_PrimaryOpenLocationValue { get; set; }
        public double F_PrimaryCloseLocationValue { get; set; }
        // Secondary
        public double F_SecondaryMovingAverageDistance { get; set; }
        public double F_SecondaryMovingAverageSlope { get; set; }
        public double F_SecondaryOpenLocationValue { get; set; }
        public double F_SecondaryCloseLocationValue { get; set; }
        // Tertiary
        public double F_TertiaryMovingAverageDistance { get; set; }
        public double F_TertiaryMovingAverageSlope { get; set; }
        public double F_TertiaryOpenLocationValue { get; set; }
        public double F_TertiaryCloseLocationValue { get; set; }
    }
}
