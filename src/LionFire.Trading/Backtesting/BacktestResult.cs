﻿using System;
using System.Collections.Generic;
using System.Linq;
using LionFire.Parsing.String;

using System.Threading.Tasks;
using LionFire.Trading.Bots;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.ComponentModel.DataAnnotations.Schema;
#if NewtonsoftJson
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
#endif
using System.Runtime.Serialization;

namespace LionFire.Trading.Backtesting
{
    public class BacktestResultConfig
    {
        public BacktestResult BacktestResult { get; set; }

        [Key]
        //[ForeignKey(BacktestResult)]
        [Required]
        public string BacktestResultId { get; set; }

        public byte[] ConfigDocument { get; set; }
    }

    [Assets.AssetPath("Results")]
    public class BacktestResult : IDisposable
    {

#region Identity

        [Key]
        [Required]
        [MaxLength(20)]
        [Unit("id", true)]
        public string Id { get; set; }
        public string Key => Id;
        public string GetKey() => Id;

#endregion

#region Relationships

        public BacktestResultConfig BacktestResultConfig { get; set; }

        /// <summary>
        /// True if there is an associated BacktestResultConfig
        /// </summary>
        public bool HasTradeData { get; set; }


#endregion

        public string BotName { get; set; }
        public string Tags { get; set; }

        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }

        [Required]
        [MaxLength(17)]
        [Unit("sym", true)]
        public string Symbol { get; set; }

#region Derived

        public TimeSpan Duration { get { return !Start.HasValue || !End.HasValue ? TimeSpan.Zero : End.Value - Start.Value; } }

        public double WinRate => 100.0 * WinningTrades / TotalTrades;

        [Unit("tpm")]
        public double TradesPerMonth => TotalTrades / Months;

        [Unit("d")]
        public double Days => Duration.TotalDays;
        public double Months => Duration.TotalDays / 31;

        /// <summary>
        /// Annual equity return on investment percent
        /// </summary>
        public double Aroi { get { return (NetProfit / InitialBalance) / (Duration.TotalDays / 365); } }

#endregion

        [Unit("bt", true)]
        public string BacktestInfo { get; set; }
        public bool NoTicks => BacktestInfo != null && BacktestInfo.Split(' ').Contains("no-ticks");

        /// <summary>
        /// Average trade duration in days
        /// </summary>
        public double AverageDaysPerTrade { get; set; }
        [Unit("adwt")]
        public double AverageDaysPerWinningTrade { get; set; }
        [Unit("adlt")]
        public double AverageDaysPerLosingTrade { get; set; }

        public double AverageTrade { get; set; }
        public double Equity { get; set; }
        //public History History { get; set; }
        public double LosingTrades { get; set; }
        public double MaxBalanceDrawdown { get; set; }
        public double MaxBalanceDrawdownPercentages { get; set; }
        public double MaxEquityDrawdown { get; set; }
        public double MaxEquityDrawdownPercentages { get; set; }
        public double NetProfit { get; set; }
        //public PendingOrders PendingOrders { get; set; }
        //public Positions Positions { get; set; }
        public double? ProfitFactor { get; set; }
        public double? SharpeRatio { get; set; }
        public double? SortinoRatio { get; set; }
        public double TotalTrades { get; set; }
        public double WinningTrades { get; set; }

        public DateTime BacktestDate { get; set; }
        public string Broker { get; set; }

        [Unit("bot", true)]
        public string BotType { get; set; }
        public string BotTypeName => BotType.Substring(BotType.LastIndexOf('.') + 1);
        public string BotConfigType { get; set; }

        [NotMapped]
        public object Config { get; set; }

        [Unit("tf", true)]
        public string TimeFrame
        {
            get
            {
#if NewtonsoftJson
                dynamic tbot = (Config as JObject);
                //var tbot = Config as Bots.TBot;
                return tbot?.TimeFrame.Value;
#else
                return (Config as Bots.TBot)?.TimeFrame;
#endif
            }
        }

        /// <summary>
        /// Computed at backtest time
        /// </summary>
        [Unit("f")]
        public double Fitness { get; set; }

        public double InitialBalance { get; set; }

        /// <summary>
        /// AnnualReturnPercentPerEquityDrawdown
        /// </summary>
        [Unit("ad")]
        public double AD { get; set; }

        public static bool RelaxedTypeResolving = true;

        public TBot TBot
        {
            get
            {
                if (tBot == null)
                {
                    if (Config as TBot != null)
                    {
                        return Config as TBot;
                    }

                    var backtestResult = this;

                    var templateType = ResolveType(backtestResult.BotConfigType);
                    if (templateType == null && RelaxedTypeResolving)
                    {
                        templateType = ResolveType(backtestResult.BotConfigType.Substring(0, backtestResult.BotConfigType.IndexOf(',')));
                    }

                    if (templateType == null)
                    {
                        Debug.WriteLine($"Bot type not supported: {backtestResult.BotConfigType}");
#if NewtonsoftJson
                        try
                        {
                            this.TBotJObject = (JObject)backtestResult.Config;
                        }
                        catch (Exception)
                        {
                            Debug.WriteLine($"Failed to assign Config to JObject.  Type: " + backtestResult.Config?.GetType()?.FullName);
                        }
#endif
                        return null;
                        //throw new NotSupportedException($"Bot type not supported: {backtestResult.BotConfigType}");
                    }

#if NewtonsoftJson
                    tBot = (TBot)((JObject)backtestResult.Config).ToObject(templateType);
#else
                    throw new NotImplementedException("TODO: Get TBot from backtestResult.Config");
#endif
                }
                return tBot;
            }
        }
        private TBot tBot;

#if NewtonsoftJson
        [JsonIgnore]
        [IgnoreDataMember]
        [NotMapped]
        public JObject TBotJObject
        {
            get; private set;
        }
#endif // #else TODO

        public Type ResolveType(string typeName)
        {
            var result = TypeResolver.Default.TryResolve(typeName);
            if (result != null) return result;

            result = TypeResolver.Default.TryResolve(typeName.Replace("LionFire.Trading.cTrader", "LionFire.Trading.Proprietary"));
            if (result != null) return result;

            result = TypeResolver.Default.TryResolve(typeName.Replace("cAlgo.Robots.", "LionFire.Trading.Proprietary.Algos."));

            return result;
        }

#region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.
                BacktestResultConfig = null;
                Config = null;
                tBot = null;
#if NewtonsoftJson
                TBotJObject = null;
#endif

                disposedValue = true;
            }
        }

        public void Dispose() => Dispose(true);

#endregion

    }


}
