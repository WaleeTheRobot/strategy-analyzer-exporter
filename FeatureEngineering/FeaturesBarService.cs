using FeatureEngineering.Features.MovingAverages;
using FeatureEngineering.Features.Price;
using FeatureEngineering.Utils;
using System;

namespace FeatureEngineering;

public sealed class FeaturesBarService
{
    private readonly FeaturesBarConfig _config;

    private readonly CircularBuffer<BaseBar> _bars;
    private readonly CircularBuffer<double> _maFastBuf;
    private readonly CircularBuffer<double> _maSlowBuf;

    public FeaturesBarService(FeaturesBarConfig config, int? expectedBars = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        int capacity = _config.BarsRequiredToTrade + 1;

        _bars = new CircularBuffer<BaseBar>(capacity);
        _maFastBuf = new CircularBuffer<double>(capacity);
        _maSlowBuf = new CircularBuffer<double>(capacity);
    }

    public FeaturesBar? GetFeaturesBar(BaseBar bar)
    {
        _bars.Add(bar);
        _maFastBuf.Add(bar.MovingAverage);
        _maSlowBuf.Add(bar.SlowMovingAverage);

        if (_bars.Count < _config.BarsRequiredToTrade) return null;

        return CreateFeaturesBar(bar);
    }

    private FeaturesBar CreateFeaturesBar(BaseBar bar)
    {
        var ma = MovingAverages.Compute(_maFastBuf, _maSlowBuf, bar, _config);
        var price = Price.Compute(_bars, _config);

        return FeaturesBarCreator.Create(in bar, in ma, in price);
    }
}
