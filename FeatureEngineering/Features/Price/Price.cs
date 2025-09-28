using System.Collections.Generic;

namespace FeatureEngineering.Features.Price;

public static class Price
{
    public static PriceFeatures Compute(
        IReadOnlyList<BaseBar> bars,
        FeaturesBarConfig config)
    {
        if (bars?.Count == 0)
            return PriceFeatures.Empty;

        var lastBar = bars[bars.Count - 1];

        var olv = GetOpenLocationValue(lastBar);
        var clv = GetCloseLocationValue(lastBar);

        return new PriceFeatures(
            olv,
            clv);
    }

    public static double GetCloseLocationValue(in BaseBar bar, double tolerance = 1e-6)
    {
        double high = bar.High, low = bar.Low, close = bar.Close;
        double range = high - low;
        if (range < tolerance) return 0.0;
        return ((2 * close) - high - low) / range;
    }

    public static double GetOpenLocationValue(in BaseBar bar, double tolerance = 1e-6)
    {
        double high = bar.High, low = bar.Low, open = bar.Open;
        double range = high - low;
        if (range < tolerance) return 0.0;
        return ((2 * open) - high - low) / range;
    }
}
