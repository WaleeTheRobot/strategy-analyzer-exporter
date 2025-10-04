# Strategy Analyzer Exporter

A NinjaTrader strategy that exports market data with calculated features to DuckDB database for machine learning and backtesting analysis.

NinjaTrader Minimum Version `8.1.6.0`

Make sure you are considering the bars required to trade in your start time. For example, if you want to start adding to the database on a one minute chart at 0900, then you should set the start time to around 0830. This will start the calculations and populate the buffer before it actually writes to the database and prevent possible leakage between days.

## Overview

This strategy captures and processes market data and exports enriched data with technical features to a DuckDB database. It's designed for use with NinjaTrader's Strategy Analyzer to build comprehensive datasets that can be extracted for quantitative analysis.

## Features

### Technical Features Calculated

There are some moving averages and open/close location value for the bar that are included. Other features can easily be added similar to them.

- **Moving Average Distance**: Distance between close price and EMA
- **Moving Average Slope**: Trend direction of the moving average
- **Moving Average Autocorrelation**: Autocorrelation of the moving average
- **Open Location Value**: Relative position of open within the bar range
- **Close Location Value**: Relative position of close within the bar range

### Data Export

- **DuckDB Database**: Stores datasets
- **Dynamic Schema**: Automatically creates tables based on data structure
- **Batch Processing**: Optimized for high-volume data with configurable batch sizes

## Usage

This is meant for someone to extend on with custom features, which you will have to install some packages.

NuGet install:

```
DuckDB.NET.Bindings.Full
DuckDB.NETE.Data.Full
```

This was tested with version 1.3.2. Copy from your version.

**Files to copy**

```
C:\Users\<user>\.nuget\packages\duckdb.net.bindings.full\1.3.2\lib\netstandard2.0\DuckDB.NET.Bindings.dll
C:\Users\<user>\.nuget\packages\duckdb.net.data.full\1.3.2\lib\netstandard2.0\DuckDB.NET.Data.dll
C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll

C:\Users\<user>\.nuget\packages\duckdb.net.bindings.full\1.3.2\runtimes\win-x64\native\duckdb.dll
```

Both the Bindings and Data dll were moved to `C:\DuckDB` to prevent a popup from NinjaTrader on every startup. It is also hardcoded into the `DatabaseWriter.cs`.

**Copied file locations**

```
// Reference in NinjaScript Editor
C:\DuckDB\DuckDB.NET.Bindings.dll
C:\DuckDB\DuckDB.NET.Data.dll
C:\Users\<user>\Documents\NinjaTrader 8\bin\Custom\netstandard.dll

// Don't reference in NinjaScript Editor
C:\Users\<user>\Documents\NinjaTrader 8\bin\Custom\duckdb.dll
```

This should set you up so you can write to DuckDB. It seems that many of the NinjaTrader indicator and value's data type is double. The default for this strategy is to save it as a float32 when it writes to the database. Don't use the option if you need the precision.

### In NinjaTrader Strategy Analyzer

1. Load the strategy in Strategy Analyzer
2. Configure parameters (database path, time window, etc.)
3. Run historical backtests to generate dataset
4. Data is automatically exported during strategy execution
