﻿using LionFire.Collections;
using LionFire.ExtensionMethods;
using LionFire.DependencyInjection;
using LionFire.Execution;
using LionFire.Instantiating;
using LionFire.Trading.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using LionFire.Extensions.Logging;
using System.IO;
using LionFire.Applications.Hosting;
using LionFire.Applications;

namespace LionFire.Trading.Data
{

    public class DataCacheService : ITemplate<SDataCacheService>
    {

        public List<TimeFrame> TimeFrames = new List<TimeFrame> { TimeFrame.m1, TimeFrame.h1 };

    }

    // TODO: Job limiter for Spotware HTTP API: rank based on currency and timeframe priority

    public class SDataCacheService : ITemplateInstance<DataCacheService>, IStartable, IStoppable, IExecutable, IHasRunTask
    //, IRequiresInjection
    {
        public int CompletionCount { get; set; }
        public DataCacheService Template { get; set; }

        #region Dependencies

        [Dependency]
        public TradingOptions TradingOptions { get; set; }


        #endregion

        #region Lifecycle State

        //public ExecutionState State { get; }

        #region State

        public ExecutionState State
        {
            get { return state; }
            set
            {
                if (state == value) return;
                state = value; StateChangedToFor?.Invoke(state, this);
            }
        }
        private ExecutionState state;

        #endregion


        public event Action<ExecutionState, IExecutable> StateChangedToFor;

        #endregion

        #region Lifecycle

        public SDataCacheService()
        {
            Logger = this.GetLogger();
        }

        public async Task Start()
        {
            State = ExecutionState.Starting;
            foreach (var workspace in App.GetComponents<TradingWorkspace>())
            {
                await StartWorkspace(workspace);
            }

            RunTask = runTask();

            if (!RunTask.IsCompleted)
            {
                State = ExecutionState.Started;
            }
        }

        private async Task StartWorkspace(TradingWorkspace workspace)
        {
            if (!workspace.Template.IsEnabled) return;
            if (startedWorkspaces.ContainsKey(workspace.Key)) return;

            var accounts = workspace.GetAccounts(TradingOptions.AccountModes);

            Logger.LogInformation($"[start] {this.GetType().Name} starting for Workspace {workspace.Template.Name}");

            foreach (var account in accounts)
            {
                var symbols = account.SymbolsAvailable.Except(TradingOptions.SymbolsBlackList ?? Enumerable.Empty<string>());
                if (TradingOptions.SymbolsWhiteList != null)
                {
                    symbols = symbols.Intersect(TradingOptions.SymbolsWhiteList);
                }
                Logger.LogInformation($" - Account: {account.Template.AccountId} {account.Template.BrokerName} {account.Template.AccountType}");
                var sb = new StringBuilder();
                foreach (var symbol in symbols)
                {
                    if (sb.Length != 0) sb.Append(" ");
                    sb.Append(symbol);

                    foreach (var tf in Template.TimeFrames.Intersect(TradingOptions.HistoricalDataTimeFrames))
                    {
                        var serviceConfig = new SeriesCacheService
                        {
                            Symbol = symbol,
                            TimeFrame = tf,
                            Account = account,
                            Auto = true,
                        };
                        var service = serviceConfig.Create();
                        services.Add(service.Key, service);
                        await service.Start();
                    }
                }
                Logger.LogInformation($" - Symbols: [{sb.ToString()}]");
            }
            startedWorkspaces.Add(workspace.Key, workspace);
        }

        public Task RunTask { get; set; }
        private async Task runTask()
        {
            foreach (var seriesCacheService in services.Values.ToArray())
            {
                await seriesCacheService.RunTask;
            }
            await OnCompleted();
        }

        Dictionary<string, SSeriesCacheService> services = new Dictionary<string, SSeriesCacheService>();

        protected async Task OnCompleted()
        {
            CompletionCount++;
            await Stop();
        }

        public Task Stop()
        {
            State = ExecutionState.Stopping;
            foreach (var workspace in startedWorkspaces.Keys.ToArray())
            {
                StopWorkspace(workspace);
            }

            State = ExecutionState.Stopped;
            return Task.CompletedTask;
        }
        private void StopWorkspace(string key)
        {
            if (startedWorkspaces.TryGetValue(key, out var workspace))
            {
                startedWorkspaces.Remove(key);
            }
        }

        #endregion

        #region State

        #region StartedWorkspaces

        public ObservableDictionary<string, TradingWorkspace> StartedWorkspaces
        {
            get { return startedWorkspaces; }
        }
        private ObservableDictionary<string, TradingWorkspace> startedWorkspaces = new ObservableDictionary<string, TradingWorkspace>();

        public IServiceProvider ServiceProvider { get; set; }

        #endregion

        #endregion



        [Dependency]
        public ILogger Logger { get; set; }
    }



}