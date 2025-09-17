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
        public TimeFrameFeatures Primary { get; set; }
        public TimeFrameFeatures Secondary { get; set; }
        public TimeFrameFeatures Tertiary { get; set; }

        public FeaturesDataBar()
        {
            Primary = new TimeFrameFeatures();
            Secondary = new TimeFrameFeatures();
            Tertiary = new TimeFrameFeatures();
        }
    }
}
