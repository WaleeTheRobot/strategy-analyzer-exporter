namespace FeatureEngineering.Features.Price;

public readonly struct PriceFeatures
{
    public readonly double OpenLocationValue;
    public readonly double CloseLocationValue;

    public PriceFeatures(
        double openLocationValue,
        double closeLocationValue)
    {
        OpenLocationValue = openLocationValue;
        CloseLocationValue = closeLocationValue;
    }

    public static PriceFeatures Empty => new PriceFeatures(0, 0);
}
