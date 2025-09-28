namespace FeatureEngineering.Features.MovingAverages;

public readonly struct MovingAverageFeatures
{
    public readonly double FastDistance;
    public readonly double FastAutocorr;
    public readonly double FastSlope;
    public readonly double SlowDistance;
    public readonly double SlowAutocorr;

    public MovingAverageFeatures(
        double fastDistance,
        double fastAutocorr,
        double fastSlope,
        double slowDistance,
        double slowAutocorr)
    {
        FastDistance = fastDistance;
        FastAutocorr = fastAutocorr;
        FastSlope = fastSlope;
        SlowDistance = slowDistance;
        SlowAutocorr = slowAutocorr;
    }

    public static MovingAverageFeatures Empty => new MovingAverageFeatures(0, 0, 0, 0, 0);
}
