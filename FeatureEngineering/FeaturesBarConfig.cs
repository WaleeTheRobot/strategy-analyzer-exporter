namespace FeatureEngineering;

public class FeaturesBarConfig
{
    public double TickSize { get; set; }
    public int BarsRequiredToTrade { get; set; } // Should be the longest period for lookback
    public int LookbackPeriod { get; set; }
    public int LookbackPeriodSlow { get; set; }
}
