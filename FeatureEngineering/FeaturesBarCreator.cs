using FeatureEngineering.Features.MovingAverages;
using FeatureEngineering.Features.Price;

namespace FeatureEngineering;

public static class FeaturesBarCreator
{
    public static FeaturesBar Create(
        in BaseBar bar, in MovingAverageFeatures ma, in PriceFeatures price)
        => new FeaturesBar(
            in bar,
            ma.FastDistance, ma.FastAutocorr, ma.FastSlope, ma.SlowDistance, ma.SlowAutocorr,
            price.OpenLocationValue, price.CloseLocationValue);
}
