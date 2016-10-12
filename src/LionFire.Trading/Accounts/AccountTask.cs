﻿using LionFire.Applications;
using LionFire.Extensions.Logging;
using LionFire.Trading;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LionFire.Applications.Trading
{
    public class AccountTask : AppTask, IMarketTask
    {
        #region Relationships

        IMarket IMarketTask.Market { get { return this.Market; } }
        public LiveMarket Market { get; private set; }

        #endregion

        #region Parameters

        public TMarket Config {
            get { return Market?.Config; }
            set {
                // REVIEW - automatic instantiation
                Market = LionFire.Templating.ITemplateExtensions.Create(value);
            }
        }

        #endregion

        #region Construction

        public AccountTask(TMarket config = null)
        {
            if (config != null)
            {
                this.Config = config;
            }
        }

        #endregion

        #region Init

        bool isInitialized = false;
        public override bool TryInitialize()
        {
            if (isInitialized) return true;
            isInitialized = true;
            logger = this.GetLogger();

            Market.Initialize();

            return base.TryInitialize();
        }

        #endregion

        protected override void Run()
        {
            logger.LogInformation($"Starting account");

            Market.Start();

        }

        ILogger logger;
    }

}
