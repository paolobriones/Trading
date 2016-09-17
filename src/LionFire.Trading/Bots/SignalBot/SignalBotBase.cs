﻿#if DEBUG
#define NULLCHECKS
#define TRACE_RISK
#endif
#if cAlgo
using cAlgo.API;
using cAlgo.API.Internals;
#else 
using LionFire.Trading.Backtesting;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using LionFire.Trading.Indicators;
using LionFire.Extensions.Logging;
using LionFire.Trading;
using LionFire.Trading.Backtesting;

namespace LionFire.Trading.Bots
{

    public partial class SignalBotBase<TIndicator, TConfig, TIndicatorConfig> : BotBase<TConfig>, IBot
    where TIndicator : class, ISignalIndicator, new()
    where TConfig : SignalBotConfig<TIndicatorConfig>, new()
        where TIndicatorConfig : class, new()
    {
    }

    public partial class SingleSeriesSignalBotBase<TIndicator, TConfig, TIndicatorConfig> : SignalBotBase<TIndicator, TConfig, TIndicatorConfig>, IBot
    where TIndicator : class, ISignalIndicator, new()
    where TConfig : SingleSeriesSignalBotConfig<TIndicatorConfig>, new()
        where TIndicatorConfig : class, IIndicatorConfig, new()
    {

        #region Relationships

        protected ISignalIndicator Indicator { get; set; }

        #endregion

        #region Config

        public bool UseTakeProfit = false; // TEMP

        #endregion

        #region Construction

        public SingleSeriesSignalBotBase()
        {
            if (Indicator == null)
            {
                Indicator = new TIndicator();
            }
            SignalBotBase_();
            InitExchangeRates();
        }

        public SingleSeriesSignalBotBase(string symbolCode, string timeFrameCode) : this()
        {
            this.Config = new TConfig()
            {
                SymbolCode = symbolCode,
                TimeFrameCode = timeFrameCode,
                Indicator = new TIndicatorConfig
                {
                    Symbol = symbolCode,
                    TimeFrame = timeFrameCode
                },
            };
        }


        partial void SignalBotBase_();

        #endregion

        #region Computations by Derived Class

        public virtual double StopLossInPips { get { return 0; } }
        public virtual double TakeProfitInPips { get { return 0; } }

        #endregion

        #region State

        public int barCount = 0;

        #region Derived

        public bool CanOpenLong {
            get {
                var count = Positions.Where(p => p.TradeType == TradeType.Buy).Count();
                return count < Config.MaxLongPositions;
            }
        }
        public bool CanOpenShort {
            get {
                var count = Positions.Where(p => p.TradeType == TradeType.Sell).Count();
                return count < Config.MaxShortPositions;
            }
        }
        public bool CanOpen {
            get {
                var count = Positions.Count;
                return Config.MaxOpenPositions == 0 || count < Config.MaxOpenPositions;
            }
        }


        #endregion

        #endregion

        public int MinStopLossTimesSpread = 5; // TEMP TODO

        private int counter = 0;

        private void Evaluate()
        {

#if NULLCHECKS
            if (Indicator == null)
            {
                throw new ArgumentNullException("Indicator (Evaluate)");
            }
            //if (Market.IsBacktesting && Server == null)
            //{
            //    throw new ArgumentNullException("!IsBacktesting && Server");
            //}
#endif

            try
            {
                DateTime time =
#if !cAlgo
                    (Market as BacktestMarket)?.SimulationTime ??
#endif
                    Server.Time;
                Indicator.CalculateToTime(time);
            }
            catch (Exception ex)
            {
                throw new Exception("Indicator.CalculateToTime threw " + ex + " stack: " + ex.StackTrace, ex);
            }

#if TRACE_EVALUATE
            var traceThreshold = 0.0;

            if (Indicator.OpenLongPoints.LastValue > traceThreshold
                || Indicator.CloseLongPoints.LastValue > traceThreshold
            || Indicator.OpenShortPoints.LastValue > traceThreshold
                || Indicator.CloseShortPoints.LastValue > traceThreshold
                                )
            {
                l.Trace($"SignalBot Evaluate({Indicator.OpenLongPoints.Count}) Open long: " + Indicator.OpenLongPoints.LastValue.ToString("N2") + " Close long: " + Indicator.CloseLongPoints.LastValue.ToString("N2") +
                     " Open short: " + Indicator.OpenShortPoints.LastValue.ToString("N2") + " Close short: " + Indicator.CloseShortPoints.LastValue.ToString("N2")
                    );
            }
#endif

#if false
            if (Indicator == null)
            {
                var msg = "No indicator for SignalBot.";
                l.Error(msg);
                throw new Exception(msg);
            }
            if (Indicator.OpenLongPoints == null)
            {
                var msg = "No Indicator.OpenLongPoints.";
                l.Error(msg);
                throw new Exception(msg);
            }
#endif

            if (Config.AllowLong
                && Indicator.OpenLongPoints.LastValue >=
                1.0
                && Indicator.CloseLongPoints.LastValue <
                0.9
                && CanOpenLong && CanOpen)
            {
                _Open(TradeType.Buy, Indicator.LongStopLoss);
            }

            if (Config.AllowShort
            && -Indicator.OpenShortPoints.LastValue >=
                1.0
                && -Indicator.CloseShortPoints.LastValue <
                0.9
                && CanOpenShort && CanOpen)
            {
                _Open(TradeType.Sell, Indicator.ShortStopLoss);
            }

            if (Indicator.CloseLongPoints.LastValue >= 1.0 )
            {
                foreach (var position in Positions.Where(p => p.TradeType == TradeType.Buy))
                {
                    string plus = position.NetProfit > 0 ? "+" : "";
                    logger.LogInformation($"{Server.Time} [CLOSE LONG {position.Quantity} x {Symbol.Code} @ {Indicator.Symbol.Ask}] {plus}{position.NetProfit}");
                    ClosePosition(position);
                }
            }
            if (-Indicator.CloseShortPoints.LastValue >= 1.0)
            {
                foreach (var position in Positions.Where(p => p.TradeType == TradeType.Sell))
                {
                    string plus = position.NetProfit > 0 ? "+" : "";
                    logger.LogInformation($"{Server.Time} [CLOSE SHORT {position.Quantity} x {Symbol.Code} @ {Indicator.Symbol.Bid}] {plus}{position.NetProfit}");
                    ClosePosition(position);
                }
            }
        }


        #region Position Management

        public Dictionary<Position, BotPosition> BotPositions = new Dictionary<Position, BotPosition>();

        Dictionary<string, SortedList<int, double>> ExchangeRates = new Dictionary<string, SortedList<int, double>>();

        void InitExchangeRates()
        {
            {
                var rate = new SortedList<int, double>();
                //rate.Add("2011", 0.

                ExchangeRates.Add("USDJPY", rate);
            }
            {
                var rate = new SortedList<int, double>();
                //rate.Add("2011", 0.

                ExchangeRates.Add("USDCAD", rate);
            }
            {
                var rate = new SortedList<int, double>();
                //rate.Add("2011", 0.

                ExchangeRates.Add("EURUSD", rate);
            }
        }

        public double ConvertToCurrency(double amount, string fromCurrency, string toCurrency = null)
        {

            if (toCurrency == null) toCurrency = Account.Currency;
            if (fromCurrency == toCurrency) return amount;

            var conversionSymbolCode = fromCurrency + toCurrency;
            var conversionSymbolCodeInverse = toCurrency + fromCurrency;

            if (Symbol.Code == conversionSymbolCode)
            {
                return Symbol.Bid * amount;
            }
            else if (Symbol.Code == conversionSymbolCodeInverse)
            {
                return amount / Symbol.Ask;
            }

            if (IsBacktesting)
            {
                if (ExchangeRates != null && ExchangeRates.ContainsKey(conversionSymbolCode))
                {
                    var rates = ExchangeRates[conversionSymbolCode];
                    double rate = 0;
                    if (rates.ContainsKey(Server.Time.Year))
                    {
                        rate = rates[Server.Time.Year];
                        var symbolAmount = amount / rate;
                        return symbolAmount;
                    }
                    else
                    {
                        throw new NotImplementedException($"No exchange rate data available for {conversionSymbolCode} for {Server.Time}");
                    }
                }
                else
                {
                    throw new NotImplementedException($"cAlgo doesn't support currency conversion in backtesting.  Require conversion from {Symbol.Code.Substring(3)} to {toCurrency}");
                }
            }
            else
            {
                Symbol conversionSymbol = MarketData.GetSymbol(conversionSymbolCode);
                var symbolAmount = amount / conversionSymbol.Bid;
                return symbolAmount;
            }
        }

        public long VolumeToStep(long amount, long step = 0)
        {
            if (step == 0) step = Symbol.VolumeStep;
            return amount - (amount % Symbol.VolumeStep);
        }

        public long GetPositionVolume(double stopLossDistance, TradeType tradeType = TradeType.Buy)
        {
            if (Symbol.VolumeStep != Symbol.VolumeMin)
            {
                throw new NotImplementedException("Position sizing not implemented when Symbol.VolumeStep != Symbol.VolumeMin");
            }

            var stopLossValuePerVolumeStep = stopLossDistance * Symbol.VolumeStep;

            var price = (tradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask);
            long volume_MinPositionSize = long.MinValue;

            if (Config.MinPositionSize > 0)
            {
                volume_MinPositionSize = Symbol.VolumeMin + (Config.MinPositionSize - 1) * Symbol.VolumeStep;
            }

            long volume_MinPositionRiskPercent = long.MinValue;

            if (Config.PositionRiskPercent > 0)
            {
                var fromCurrency = Symbol.Code.Substring(3);
                var toCurrency = Account.Currency;

                var stopLossDistanceAccountCurrency = ConvertToCurrency(stopLossDistance, fromCurrency, toCurrency);

                var equityRiskAmount = this.Account.Equity * (Config.PositionRiskPercent / 100.0);

                var quantity = (long)(equityRiskAmount / (stopLossDistanceAccountCurrency));
                quantity = VolumeToStep(quantity);

                volume_MinPositionRiskPercent = (long)(quantity);

                var slDistAcct = stopLossDistanceAccountCurrency.ToString("N3");

#if TRACE_RISK
                //l.Debug($"[PositionRiskPercent] {BotConfig.PositionRiskPercent}% x {Account.Equity} equity = {equityRiskAmount}. SLDist({Symbol.Code.Substring(3)}): {stopLossDistance}, SLDist({Account.Currency}): {slDistAcct}, riskAmt/SLDist({Account.Currency}): {quantity} ");
#endif

                //var volumeByRisk = (long)(this.Account.Equity * BotConfig.MinPositionRiskPercent);
            }

            long volume_PositionRiskPricePercent = long.MinValue;

            //if (BotConfig.PositionRiskPricePercent > 0)
            //{
            //    var maxRiskEquity = Account.Equity * (BotConfig.PositionRiskPricePercent / 100.0);

            //    var maxVolume = (maxRiskEquity / Math.Abs(stopLossValuePerVolumeStep)) * Symbol.VolumeStep;

            //    volume_MinPositionRiskPercent = (long)maxVolume; // Rounds down

            //    l.Info($"MinPositionRiskPercent - stopLossValuePerVolumeStep: {stopLossValuePerVolumeStep},  MaxVol: {maxVolume}  (Eq: {Account.Equity}, PositionRiskPricePercent: {BotConfig.PositionRiskPricePercent}, Max risk equity: {maxRiskEquity})");

            //    //var volumeByRisk = (long)(this.Account.Equity * BotConfig.MinPositionRiskPercent);
            //}

            var volume = Math.Max(0, Math.Max(Math.Max(volume_MinPositionSize, volume_MinPositionRiskPercent), volume_PositionRiskPricePercent));
            if (volume == 0)
            {
                logger.LogWarning("volume == 0");
            }
            volume = VolumeToStep(volume);
            return volume;
        }

        private void _Open(TradeType tradeType, IndicatorDataSeries indicatorSL)
        {
            var price = tradeType == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            var stopLoss = indicatorSL.LastValue;
            var spread = Symbol.Ask - Symbol.Bid;
            var stopLossDistance = Math.Abs(price - stopLoss);
            stopLossDistance = Math.Max(spread * MinStopLossTimesSpread, stopLossDistance);
#warning NEXT: PipSize, VolumeStep
            var stopLossDistancePips = stopLossDistance / Symbol.PipSize;
            var risk = stopLossDistance * Symbol.VolumeStep;

            var TakeProfitInPips = 0.0;
            var volumeInUnits = GetPositionVolume(Math.Abs(stopLossDistance));

#if TRACE_RISK
            logger.LogTrace($"Risk calc: Symbol.Ask {Symbol.Ask}, stopLoss {stopLoss} stopLossDist {stopLossDistance.ToString("N3")} SL-Pips: {stopLossDistancePips}");
#endif

            LogOpen(tradeType, volumeInUnits, risk, stopLoss, stopLossDistance);

            if (IsBacktesting && Config.BacktestProfitTPMultiplierOnSL > 0)
            {
                UseTakeProfit = true;
                TakeProfitInPips = stopLossDistancePips * Config.BacktestProfitTPMultiplierOnSL;
            }
            OpenPosition(tradeType, stopLossDistancePips, UseTakeProfit ? TakeProfitInPips : double.NaN, volumeInUnits);
        }

        private void OpenPosition(TradeType tradeType, double stopLossInPips, double takeProfitInPips, long volumeInUnits)
        {
            if (volumeInUnits == 0) return;

            //if (tradeType == TradeType.Sell) { stopLossInPips = -stopLossInPips; }

            var result = ExecuteMarketOrder(tradeType, Symbol, volumeInUnits, Label, stopLossInPips, takeProfitInPips);

            if (result.Position != null)
            {
                // ShortPositions.Add(result.Position);
                var p = new BotPosition(result.Position, this);
                BotPositions.Add(result.Position, p);
                OnNewPosition(p);
            }
        }

        protected void OnNewPosition(BotPosition p)
        {
            //var trailers = new List<IOnBar>();

            /*
p.onBars.Add(new StopLossTrailer(p) 
{
    //EndValue = 0.85,
    EndValue = 0.5,
    ValueUnit = Unit.Profit,
    Key = new RangedNumber(15, Unit.Bars),
    Function = DoubleFunctions.Linear
});*/

            //var closePointsTSL = new StopLossTrailerConfig
            //{
            //    Input = new RangedNumber(1, Unit.ClosePoints, 0.2),
            //    StopLossLocation = new RangedNumber(0.85, Unit.NearChannel, 0.5),
            //    Function = DoubleFunctions.Linear
            //};

            //p.onBars.Add(new StopLossTrailer(p, closePointsTSL));
        }

        #endregion

        #region Misc

        public string TradeString(TradeType tradeType)
        {
            return tradeType == TradeType.Buy ? "LONG" : "SHORT";
        }

        private void LogOpen(TradeType tradeType, long volumeInUnits, double risk, double stopLoss, double stopLossDistance)
        {
            if (!Config.Log) return;
            string stopLossDistanceAccount = "";
            var purchaseCurrency = Symbol.Code.Substring(0, 3);
            if (purchaseCurrency != Account.Currency)
            {
                stopLossDistanceAccount = " / " + ConvertToCurrency(stopLossDistance, purchaseCurrency, Account.Currency) + " " + Account.Currency;
            }

            var openPoints = tradeType == TradeType.Buy ? Indicator.OpenLongPoints.LastValue : Indicator.OpenShortPoints.LastValue;
            var price = tradeType == TradeType.Buy ? Indicator.Symbol.Ask : Indicator.Symbol.Bid;

#if cAlgo
            var dateStr = this.MarketSeries.OpenTime.LastValue.ToString("yyyy.MM.dd HH:mm");
            logger.LogInformation($"{dateStr} [{TradeString(tradeType)} {volumeInUnits} {Symbol.Code} @ {price}] SL: {stopLoss} (dist: {stopLossDistance.ToString("N3")}{stopLossDistanceAccount}) risk: {risk.ToString("N2")}");
#else
            logger.LogInformation($" [{TradeString(tradeType)} {volumeInUnits} {Symbol.Code} @ {price}] SL: {stopLoss} (dist: {stopLossDistance.ToString("N3")}{stopLossDistanceAccount}) risk: {risk.ToString("N2")}");
#endif
        }

#endregion

        protected override double GetFitness(GetFitnessArgs args)
        {
            var initialBalance = args.History.Count == 0 ? args.Equity : args.History[0].Balance - args.History[0].NetProfit;

            var invDrawDown = 1 - (Math.Min(100, args.MaxEquityDrawdownPercentages) / 100);
            //var drawDownPenalty = 0.5;
            var drawDownPenalty = 0.8;
            invDrawDown = Math.Pow(invDrawDown, drawDownPenalty * Math.E);

            var tradeCount = args.History.Count;
            var tradeCountBonus = 2;
            var tradeCountMultiplier = Math.Log(tradeCount, 5 / tradeCountBonus);

            var fitness = (args.NetProfit / initialBalance) * invDrawDown * tradeCountMultiplier;

            if (Config.LogBacktest && (Config.LogBacktestThreshold == 0 || fitness > Config.LogBacktestThreshold))
            {
                var result = new BacktestResult()
                {
                    BacktestDate = DateTime.UtcNow,
                    BotType = this.GetType().FullName,
                    Config = this.Config,

                    Start = this.MarketSeries?.OpenTime?[0],
                    End = this.MarketSeries?.OpenTime?.LastValue,

                    AverageTrade = args.AverageTrade,
                    Equity = args.Equity,
                    //History
                    LosingTrades = args.LosingTrades,
                    MaxBalanceDrawdown = args.MaxBalanceDrawdown,
                    MaxBalanceDrawdownPercentages = args.MaxBalanceDrawdownPercentages,
                    MaxEquityDrawdown = args.MaxEquityDrawdown,
                    MaxEquityDrawdownPercentages = args.MaxEquityDrawdownPercentages,
                    NetProfit = args.NetProfit,
                    //PendingOrders
                    //Positions
                    ProfitFactor = args.ProfitFactor,
                    SharpeRatio = args.SharpeRatio,
                    SortinoRatio = args.SortinoRatio,
                    TotalTrades = args.TotalTrades,
                    WinningTrades = args.WinningTrades,
                };

                BacktestLogger = this.GetLogger(this.ToString().Replace(' ', '.') + ".Backtest");

                try
                {
                    string resultJson = "";
#if cAlgo
                    resultJson = Newtonsoft.Json.JsonConvert.SerializeObject(result);
#endif
                    var profit = args.Equity / initialBalance;

                    this.BacktestLogger.LogInformation($"${args.Equity} ({profit.ToString("N1")}x) #{args.History.Count} {args.MaxEquityDrawdownPercentages.ToString("N2")}%dd [from ${initialBalance.ToString("N2")} to ${args.Equity.ToString("N2")}] [fit {fitness.ToString("N1")}] {Environment.NewLine} result = {resultJson} ");
                }
                catch (Exception ex)
                {
                    this.BacktestLogger.LogError(ex.ToString());
                }
            }

            return fitness;
        }

        public Microsoft.Extensions.Logging.ILogger BacktestLogger { get; protected set; }

    }
}
