namespace FeatureEngineering;

public readonly struct FeaturesBar
{
    public int Time { get; }
    public int Day { get; }
    public double Open { get; }
    public double High { get; }
    public double Low { get; }
    public double Close { get; }
    public double Volume { get; }

    // Moving Averages
    public double F_MovingAverageDistance { get; }
    public double F_MovingAverageAutocorrelation { get; }
    public double F_MovingAverageSlope { get; }
    public double F_MovingAverageSlowDistance { get; }
    public double F_MovingAverageSlowAutocorrelation { get; }

    // Price 
    public double F_OpenLocationValue { get; }
    public double F_CloseLocationValue { get; }

    public FeaturesBar(
           in BaseBar b,
           double f_MovingAverageDistance,
           double f_MovingAverageAutocorrelation,
           double f_MovingAverageSlope,
           double f_MovingAverageSlowDistance,
           double f_MovingAverageSlowAutocorrelation,
           double f_OpenLocationValue,
           double f_CloseLocationValue)
    {
        Time = b.Time;
        Day = b.Day;
        Open = b.Open;
        High = b.High;
        Low = b.Low;
        Close = b.Close;
        Volume = b.Volume;

        F_MovingAverageDistance = f_MovingAverageDistance;
        F_MovingAverageAutocorrelation = f_MovingAverageAutocorrelation;
        F_MovingAverageSlope = f_MovingAverageSlope;
        F_MovingAverageSlowDistance = f_MovingAverageSlowDistance;
        F_MovingAverageSlowAutocorrelation = f_MovingAverageSlowAutocorrelation;

        F_OpenLocationValue = f_OpenLocationValue;
        F_CloseLocationValue = f_CloseLocationValue;
    }
}
