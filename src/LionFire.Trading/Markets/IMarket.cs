﻿using LionFire.Templating;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LionFire.Trading
{
    // REVIEW: Move these to account

    public interface IMarket : IHierarchicalTemplateInstance
    {
#if !cAlgo
        IAccount Account { get;  }
        //List<IAccount> Accounts { get; }
        Symbol GetSymbol(string symbolCode);
        IMarketSeries GetMarketSeries(string symbol, TimeFrame tf);
        MarketData MarketData { get; set; }
        MarketDataProvider Data { get; }
        Server Server { get; }

        void Add(IMarketParticipant indicator);

        event Action Ticked;

#endif

        DateTime SimulationTime { get; }
        TimeZoneInfo TimeZone { get; }

        bool IsBacktesting { get; }
        
        bool IsSimulation { get; }
        bool IsRealMoney { get; }


        //MarketSeries GetSeries(Symbol symbol, TimeFrame timeFrame);

        
        //IEnumerable<string> SymbolsAvailable { get; }
        //IEnumerable<string> GetSymbolTimeFramesAvailable(string symbol);

        IObservable<bool> Started { get; }


    
        
    }
}
