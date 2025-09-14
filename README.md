# Strategy Analyzer Exporter

A NinjaTrader strategy that exports multi-timeframe market data with calculated features to SQLite database for machine learning and backtesting analysis.

## Overview

This strategy captures and processes market data across multiple timeframes (Primary, Secondary, Tertiary) and exports enriched data with technical features to a SQLite database. It's designed for use with NinjaTrader's Strategy Analyzer to build comprehensive datasets that can be extracted for quantitative analysis.

## Features

### Technical Features Calculated

There are some moving averages and open/close location value for the bar that are included. Other features can easily be added similar to them.

- **Moving Average Distance**: Distance between close price and EMA
- **Moving Average Slope**: Trend direction of the moving average
- **Open Location Value**: Relative position of open within the bar range
- **Close Location Value**: Relative position of close within the bar range

### Data Export

- **SQLite Database**: Stores datasets
- **Dynamic Schema**: Automatically creates tables based on data structure
- **Batch Processing**: Optimized for high-volume data with configurable batch sizes

## Usage

### In NinjaTrader Strategy Analyzer

1. Load the strategy in Strategy Analyzer
2. Configure parameters (database path, time window, etc.)
3. Run historical backtests to generate dataset
4. Data is automatically exported during strategy execution
