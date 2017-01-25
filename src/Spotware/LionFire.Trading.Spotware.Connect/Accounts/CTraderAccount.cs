﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json;
using LionFire.Instantiating;
using LionFire.Assets;
using LionFire.Applications;
using System.Threading.Tasks;
using LionFire.Execution;
using LionFire.ExtensionMethods;
using LionFire.Extensions.Logging;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using LionFire.MultiTyping;
using Microsoft.Extensions.DependencyInjection;
using System.Reactive.Linq;
#if NET462
using OpenApiLib;
#endif
using LionFire.Structures;
using LionFire.Trading;

using LionFire.Trading.Spotware.Connect.AccountApi;
using LionFire.Trading.Accounts;
using LionFire.Reactive;
using LionFire.Reactive.Subjects;

namespace LionFire.Trading.Spotware.Connect
{

    [AssetPath(@"Accounts/cTrader")]
    public class TCTraderAccount : TAccount, ITemplate<CTraderAccount>
    {
    }

    public partial class CTraderAccount : LiveAccountBase<TCTraderAccount>,
        //IRequiresServices,
        IStartable, IHasExecutionFlags, IHasRunTask, IConfigures<IServiceCollection>
        , IStoppable
        , IExecutable
    //, IHandler<SymbolTick>
    //, IDataSource
    //, IHasExecutionState, IChangesExecutionState
    {

        #region Compile-time

#if NETSTANDARD
        public override bool TicksAvailable { get { return false; } }
#endif

        #endregion

        #region Settings

        private TimeSpan HeartbeatDelay = TimeSpan.FromSeconds(20);
        DateTime lastSendTime { set { _nextHeartbeat = value + HeartbeatDelay; } }
        //DateTime _lastSendTime = DateTime.MinValue;
        DateTime _nextHeartbeat = DateTime.MinValue;

        protected ISpotwareConnectAppInfo ApiInfo { get { return Defaults.Get<ISpotwareConnectAppInfo>(); } }

        #region Derived (Convenience)

        // Test account: test002_access_token,  id: 62002
        // Account login: login 3000041 pass:123456 on http://sandbox-ct.spotware.com

        long AccountId => Convert.ToInt64(Template.AccountId);
        string AccessToken => Template.AccessToken;

        #endregion

        #endregion


        #region Relationships

        //IServiceProvider IRequiresServices.ServiceProvider { get { return ServiceProvider; } set { this.ServiceProvider = value; } }
        //protected IServiceProvider ServiceProvider { get; private set; }

        #endregion


        #region Construction

        public CTraderAccount()
        {
            CTraderAccount_NetFramework();
            logger = this.GetLogger();
            historicalDataProvider = new SpotwareConnectLoadHistoricalDataProvider(this);
            //this.Data.LoadHistoricalDataAction = LoadHistoricalDataAction;
        }

        //public IJob LoadHistoricalDataAction(MarketSeriesBase marketSeries, DateTime? startDate, DateTime endDate, int minBars = 0)
        //{
        //    var loadTask = new SpotwareLoadHistoricalDataJob(marketSeries)
        //    {
        //        EndTime = endDate,
        //        StartTime = startDate,
        //        MinBars = minBars
        //    };
        //    return loadTask;
        //}

        partial void CTraderAccount_NetFramework();

        #endregion

        #region Configuration

        void IConfigures<IServiceCollection>.Configure(IServiceCollection sc)
        {
            sc.AddSingleton(typeof(IAccount), this);
        }

        #endregion

        #region Initialization

        public bool TryInitialize()
        {
            return Template != null;
        }

        #endregion

        #region Execution

        public ExecutionFlag ExecutionFlags { get { return executionFlags; } set { executionFlags = value; } }
        private volatile ExecutionFlag executionFlags = ExecutionFlag.WaitForRunCompletion;

        //volatile bool isRestart;
        bool isRestart
        {
            get { return ExecutionFlags.HasFlag(ExecutionFlag.AutoRestart); }
            set
            {
                if (value) ExecutionFlags |= ExecutionFlag.AutoRestart;
                else ExecutionFlags &= ~ExecutionFlag.AutoRestart;
            }
        }

        //public ExecutionState ExecutionState
        //{
        //    get { return ExecutionStates.Value; }
        //    protected set { ExecutionStates.OnNext(value); }
        //}
        public IBehaviorObservable<ExecutionState> State { get { return state; } }
        BehaviorObservable<ExecutionState> state = new BehaviorObservable<ExecutionState>(ExecutionState.Unspecified);


        public async Task Stop(StopMode mode = StopMode.GracefulShutdown, StopOptions options = StopOptions.StopChildren)
        {
            if (this.IsStarted())
            {
                state.OnNext(ExecutionState.Stopping);
                if (IsTradeApiEnabled)
                {
                    await Task.Run(() => Stop_TradeApi());
                }
                state.OnNext(ExecutionState.Stopped);
            }
        }



        public async Task Start()
        {
            if (this.IsStarted()) return;

            lock (connectLock)
            {
                if (this.IsStarted()) return;

                state.OnNext(ExecutionState.Starting);
            }
            await OnStarting();

            RunTask = Task.Run(() => Run());
            //RunTask = Task.Factory.StartNew(Run);
        }

        public Task RunTask
        {
            get; private set;
        }

        #endregion

        partial void Run_TradeApi();
        partial void Stop_TradeApi();

        partial void SubscribeToDefaultSymbols();
        partial void CloseConnection();

        public string DumpPositions(IPositions positions)
        {
            var sb = new StringBuilder();

            foreach (var p in positions)
            {
                sb.AppendLine(p.ToString());
            }
            return sb.ToString();
        }

        public async Task UpdatePositions()
        {
            var positions = await SpotwareAccountApi.GetPositions(this);
            this.positions.Clear();
            this.positions.AddRange(positions);
            Console.WriteLine(DumpPositions(Positions));
        }


        public override IHistoricalDataProvider HistoricalDataProvider { get { return historicalDataProvider; } }
        SpotwareConnectLoadHistoricalDataProvider historicalDataProvider;


        protected override async void OnTradeApiEnabledChanging()
        {
            await Task.Run(() => Stop());
        }
        protected override void OnTradeApiEnabledChanged()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Start();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        public object connectLock = new object();
        

        public void Run()
        {

            // TODO: Verify account valid via web api
            if (!IsTradeApiEnabled)
            {
                StatusText = "Updating positions";

                UpdatePositions().Wait();

                StatusText = "Disconnected mode";
                state.OnNext(ExecutionState.Started);
            }
            else
            {
                do
                {
                    isRestart = false;

                    StatusText = "Updating positions";
                    UpdatePositions().Wait();

                    if (IsTradeApiEnabled)
                    {
                        StatusText = "Connecting";

                        Run_TradeApi();

                        StatusText = "Connected";
                        state.OnNext(ExecutionState.Started);
                    }
                    else
                    {
                        StatusText = "Disabled";
                    }

                    if (IsCommandLineEnabled)
                    {

                        while (IsCommandLineAlive)
                        {
                            //DisplayMenu();
#if NET462
                            if (!ProcessInput()) break;
#endif
                            Thread.Sleep(700);
                        }
                    }
                    else
                    {
                        while (IsTradeConnectionAlive)
                        {
                            Thread.Sleep(700);
                        }
                    }
                } while (isRestart);

                state.OnNext(ExecutionState.Stopping);

                StatusText = "Disconnecting";
                Stop_TradeApi();
                StatusText = "Disconnected";
            }
            state.OnNext(ExecutionState.Stopped);
        }

        //public bool IsCommandLineEnabled { get; set;  } = true;

        #region IsCommandLineEnabled

        public bool IsCommandLineEnabled
        {
            get { return isCommandLineEnabled; }
            set { isCommandLineEnabled = value; }
        }
        private bool isCommandLineEnabled = false;

        #endregion

        public bool IsCommandLineAlive
        {
            get { return IsCommandLineEnabled && IsTradeConnectionAlive; }
        }

#if !NET462
        public bool IsTradeConnectionAlive { get { return false; } }
#endif

        #region Outgoing ProtoBuffer objects to Raw data

        #endregion Outgoing ProtoBuffer objects to Raw data...

        #region Symbol Subscriptions

        ConcurrentDictionary<string, DateTime> subscriptionActive = new ConcurrentDictionary<string, DateTime>();
        ConcurrentDictionary<string, DateTime> subscriptionWaitingForConnection = new ConcurrentDictionary<string, DateTime>();
        ConcurrentDictionary<string, DateTime> subscriptionRequested = new ConcurrentDictionary<string, DateTime>();
        ConcurrentDictionary<string, DateTime> unsubscriptionRequested = new ConcurrentDictionary<string, DateTime>();

        // If false, need to get notified some other way: like if Spotware's protocol actually worked and gave me the bars once they were done
        public bool MinuteBarsFromTicks = true;
        public bool OtherBarsFromMinutes = true;


        protected override void Subscribe(string symbolCode, string timeFrame)
        {
#if NET462
            var key = symbolCode + ";" + timeFrame;
            if (subscriptionActive.ContainsKey(key))
            {
                return;
            }

            if (timeFrame == "t1")
            {
                var date = DateTime.UtcNow;
                var existing = subscriptionRequested.GetOrAdd(symbolCode, date);
                if (date == existing)
                {
                    try
                    {
                        RequestSubscribeForSymbol(symbolCode);
                        subscriptionRequested.AddOrUpdate(key, DateTime.UtcNow, (x, y) => y);
                    }
                    catch (NotConnectedException)
                    {
                        subscriptionWaitingForConnection.GetOrAdd(key, DateTime.UtcNow);
                    }
                }
            }
            else if (timeFrame == "m1")
            {
                if (MinuteBarsFromTicks)
                {
                    var t1Series = GetSymbol(symbolCode);
                    t1Series.Ticked += TickToMinuteHandler;
                }
                else
                {
                    throw new NotImplementedException("MinuteBarsFromTicks == false");
                }
            }
            else if (timeFrame.StartsWith("t"))
            {
                throw new NotImplementedException("tick timeframes other than t1 not supported yet");
            }
            else // Derived from m1
            {
                if (OtherBarsFromMinutes)
                {
                    MarketSeries series = GetMarketSeries(symbolCode, timeFrame);

                    var handler = BarToOtherBarHandlers.GetOrAdd(key, k => new BarToOtherBarHandler(this, series));
                    handler.IsEnabled = true;
                }
                else
                {
                    throw new NotImplementedException("HourBarsFromMinutes == false");
                }
            }
            //else
            //{
            //    throw new NotImplementedException();
            //}

#else
#endif
        }
        
        ConcurrentDictionary<string, BarToOtherBarHandler> BarToOtherBarHandlers = new ConcurrentDictionary<string, BarToOtherBarHandler>();


        #region Server Time from Tick

        private DateTime ServerTimeFromTick
        {
            get { return serverTimeFromTick; }
            set
            {
                if (serverTimeFromTick == value) return;
                if (serverTickToMinuteTime == default(DateTime)) { serverTickToMinuteTime = value; }

                var oldTime = serverTimeFromTick;
                serverTimeFromTick = value;
                if (ServerTime < value)
                {
                    ServerTime = value;
                }
                if (!oldTime.IsSameMinute(serverTimeFromTick))
                {
                    Task.Factory.StartNew(async () =>
                    {
                        await Task.Delay(TickToM1BarHandler.WaitForTickToMinuteToFinishInMilliseconds);
                        OnMinuteRollover(oldTime);
                        serverTickToMinuteTime = serverTimeFromTick;
                    });
                }
            }
        }
        DateTime serverTimeFromTick;
        DateTime serverTickToMinuteTime;

        #endregion

        private void OnMinuteRollover(DateTime previousMinute)
        {
            lock (TickToMinuteBarLock)
            {
                foreach (var kvp in tickToMinuteBars.ToArray())
                {
                    // Skip bars with no data
                    if (!kvp.Value.IsValid) continue; 

                    // Sanity check: skip if tick to minute bar is later than the minute that just passed
                    if (!previousMinute.IsSameMinute( kvp.Value.OpenTime)
                        && kvp.Value.OpenTime > previousMinute // Shouldn't happen - TOSANITYCHECK
                        ) continue;

                    TickToMinuteBar(kvp.Key, kvp.Value);
                }
             
            }
        }

        object TickToMinuteBarLock = new object();
        private void TickToMinuteBar(string symbolCode, TimedBar bar)
        {
            lock (TickToMinuteBarLock)
            {
                //var bar = tickToMinuteBars[symbolCode];
                if (!bar.IsValid) return;

                GetMarketSeriesInternal(symbolCode, TimeFrame.m1).OnBar(bar, true);

                tickToMinuteBars[symbolCode] = TimedBar.Invalid;
            }
        }

        Dictionary<string, TimedBar> tickToMinuteBars = new Dictionary<string, TimedBar>();
       
        // Hardcoded to Bid prices
        // Move elsewhere, potentially reuse?
        private void TickToMinuteHandler(SymbolTick tick)
        {
            if (!tick.Time.IsSameMinute(ServerTimeFromTick) && tick.Time < serverTickToMinuteTime)
            {
                logger.LogWarning($"[TICK] Got old {tick.Symbol} tick for time {tick.Time} when server tick to minute time is {serverTickToMinuteTime} and server time from tick is {ServerTimeFromTick}");
            }

            if (tick.Time > ServerTimeFromTick)
            {
                ServerTimeFromTick = tick.Time; // May trigger a bar from previous minute, for all symbols, after a delay (to wait for remaining ticks to come in)
            }

            TimedBar bar = tickToMinuteBars.TryGetValue(tick.Symbol, TimedBar.Invalid);
            if (bar.IsValid && !bar.OpenTime.IsSameMinute( tick.Time))
            {
                // Immediately Trigger a finished bar even after starting the timer above.
                TickToMinuteBar(tick.Symbol, bar);
                bar = TimedBar.Invalid;
            }

            if (!bar.IsValid)
            {
                var minuteBarOpen = new DateTime(tick.Time.Year, tick.Time.Month, tick.Time.Day, tick.Time.Hour, tick.Time.Minute, 0);

                bar = new TimedBar(minuteBarOpen);
            }

            if (!double.IsNaN(tick.Bid))
            {
                if (double.IsNaN(bar.Open))
                {
                    bar.Open = tick.Bid;
                }
                bar.Close = tick.Bid;
                if (double.IsNaN(bar.High) || bar.High < tick.Bid)
                {
                    bar.High = tick.Bid;
                }
                if (double.IsNaN(bar.Low) || bar.Low > tick.Bid)
                {
                    bar.Low = tick.Bid;
                }
            }

            if (double.IsNaN(bar.Volume)) // REVIEW - is this correct for volume?
            {
                bar.Volume = 1;
            }
            else
            {
                bar.Volume++;
            }

            if (tickToMinuteBars.ContainsKey(tick.Symbol))
            {
                tickToMinuteBars[tick.Symbol] = bar;
            }
            else
            {
                tickToMinuteBars.Add(tick.Symbol, bar);
            }

        }

        protected override void Unsubscribe(string symbolCode, string timeFrame)
        {
#if NET462
            var key = symbolCode + ";" + timeFrame;

            if (!subscriptionActive.ContainsKey(key))
            {
                return;
            }

            if (timeFrame == "t1")
            {
                var date = DateTime.UtcNow;
                var existing = unsubscriptionRequested.GetOrAdd(symbolCode, date);
                if (date == existing)
                {
                    RequestUnsubscribeForSymbol(symbolCode);
                }
            }
            else if (timeFrame == "m1")
            {
            }
            else if (timeFrame == "h1")
            {
            }
            else
            {
                throw new NotImplementedException();
            }
#else
            // Not implemented
#endif
        }

        #endregion

        #region Symbols

        public new CTraderSymbol GetSymbol(string symbolCode)
        {
            return (CTraderSymbol)base.GetSymbol(symbolCode);
        }

        protected override Symbol CreateSymbol(string symbolCode)
        {
            var result = new CTraderSymbol(symbolCode, this);
            result.TickHasObserversChanged += Result_TickHasObserversChanged;
            return result;
        }

        private void Result_TickHasObserversChanged(Symbol symbol, bool hasSubscribers)
        {
#if NET462
            if (hasSubscribers)
            {
                Subscribe(symbol.Code, "t1");
                //RequestSubscribeForSymbol(symbol.Code);
            }
            else
            {
                Unsubscribe(symbol.Code, "t1");
                //RequestUnsubscribeForSymbol(symbol.Code);
            }
#else
            // Not implemented
#endif
        }

        public Subject<Tick> GetTickSubject(string symbolCode, bool createIfMissing = true)
        {
            if (tickSubjects.ContainsKey(symbolCode)) return tickSubjects[symbolCode];
            if (!createIfMissing) return null;
            var subject = new Subject<Tick>();
            tickSubjects.Add(symbolCode, subject);
            return subject;
        }
        Dictionary<string, Subject<Tick>> tickSubjects = new Dictionary<string, Subject<Tick>>();

        #endregion

        #region Series

        public override MarketSeries GetSeries(Symbol symbol, TimeFrame timeFrame)
        {
            var kvp = new KeyValuePair<string, string>(symbol.Code, timeFrame.ToString());
            return marketSeries.GetOrAdd(kvp, _ => ((IAccount)this).CreateMarketSeries(symbol.Code, timeFrame));
        }

        public override MarketSeries CreateMarketSeries(string symbol, TimeFrame timeFrame)
        {
            var series = new MarketSeries(this, symbol, timeFrame);

            series.BarHasObserversChanged += Series_BarHasObserversChanged;

            var barCount = Context?.Options?.DefaultHistoricalDataBars ?? TradingOptions.DefaultHistoricalDataBarsDefault;

            DateTime startTime;

            if (timeFrame == TimeFrame.m1)
            {
                startTime = DateTime.UtcNow - TimeSpan.FromMinutes(barCount);
            }
            else if (timeFrame == TimeFrame.h1)
            {
                startTime = DateTime.UtcNow - TimeSpan.FromHours(barCount);
            }
            else if (timeFrame == TimeFrame.t1)
            {
                // Uses barCount
            }
            else
            {
                throw new NotImplementedException();
            }

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Data.EnsureDataAvailable(series, null, ExtrapolatedServerTime, barCount);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            ////barCount = 0; // TEMP DISABLE
            //var task = new SpotwareLoadHistoricalDataJob(symbol, timeFrame)
            //{
            //    MinBars = barCount,
            //    Account = this,
            //    //AccountId = this.AccountId.ToString(),
            //    //AccessToken = this.AccessToken,
            //};

            //series.LoadDataJobs.Add(task.Run());

            return series;
        }

        private void Series_BarHasObserversChanged(IMarketSeries series, bool hasSubscribers)
        {
#if NET462
            var period = (ProtoOATrendbarPeriod)Enum.Parse(typeof(ProtoOATrendbarPeriod), series.TimeFrame.ToString().ToUpper());

            if (hasSubscribers)
            {
                Subscribe(series.SymbolCode, series.TimeFrame.Name);
                //RequestSubscribeForSymbol(series.SymbolCode, period);
            }
            else
            {
                Unsubscribe(series.SymbolCode, series.TimeFrame.Name);
                //RequestUnsubscribeForSymbol(series.SymbolCode, period);
            }
#else
#endif
        }

        public List<ProgressiveJob> LoadHistoricalDataJobs { get; set; }


        #endregion


        #region Order Execution

        public override TradeResult ExecuteMarketOrder(TradeType tradeType, Symbol symbol, long volume, string label = null, double? stopLossPips = default(double?), double? takeProfitPips = default(double?), double? marketRangePips = default(double?), string comment = null)
        {
            logger.LogError($"NOT IMPLEMENTED: ExecuteMarketOrder {tradeType} {volume} {symbol.Code} sl:{stopLossPips} tp:{takeProfitPips}");
            return TradeResult.NotImplemented;
        }

        public override TradeResult ClosePosition(Position position)
        {
            logger.LogError($"NOT IMPLEMENTED: ClosePosition {position.Id}");
            return TradeResult.NotImplemented;
        }

        public override TradeResult ModifyPosition(Position position, double? stopLoss, double? takeProfit)
        {
            logger.LogError($"NOT IMPLEMENTED: ModifyPosition {position.Id} sl:{stopLoss} tp:{takeProfit}");
            return TradeResult.NotImplemented;
        }

        #endregion

    }
}

