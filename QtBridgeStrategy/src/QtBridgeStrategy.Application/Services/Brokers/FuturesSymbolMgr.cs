using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;
using System.Text.Json;
using QtBridgeStrategy.Enums;
using static QtBridgeStrategy.Constants.Constants;

namespace QtBridgeStrategy.Services.Brokers
{
    public class FuturesSymbolManager : QtBridgeStrategy
    {
        /// <summary>
        /// Manages futures contracts and names, to sync Quantower with the chart data on PAG
        /// </summary>
        private int _contractExpirationRolloverDaysOffset = 2;
        private string _scContractsUrl;
        private string _watchlistName;
        private Dictionary<string, string> _supportedSymbolMap = new Dictionary<string, string>(){
            // FOREX
            {"-6A", "DA6"},
            {"-6B", "BP6"},
            {"-6E", "EU6"},
            {"-6N", "NE6"},
            //{"-6C", "CA6"},
            //{"-6J", "JY6"},
            //{"-6S", "SF6"},

            // INDEXES
            {"-YM", "YM"},
            {"-NQ", "ENQ"},
            {"-ES", "EP"},
            {"-RTY", "RTY"},
            //{"-NKD", "NKD"},

            // COMMODITIES
            {"-HG", "CPE"},
            {"-GC", "GCE"},
            {"-SI", "SIE"},
            {"-CL", "CLE"},
             //{"-NG", "NGE"},
            //{"-ZT", "TUA"},
            //{"-ZF", "FVA"},
            //{"-HO", "HOE"},//

            //{"-ZC", "ZCE"},
            //{"-ZL", "ZLE"}, // SOYBEAN OIL
            //{"-ZM", "ZME"}, // SOYBEAN MEAL
            //{"-ZS", "ZSE"}, // SOYBEAN
            //{"-ZW", "ZWA"}, // WHEAT
            //{"-GF", "GF"}, // CATTLE FEEDER
            //{"-LE", "GLE"}, // LIVE CATTLE
            
            //{"-ZN", "TYA"},
            //{"-ZT", "TUA"},
            //{"-ZF", "FVA"},
            
            {"-M6A", "M6A"}, // AU
            {"-M6B", "M6B"}, // GU
            {"-M6E", "M6E"}, // EU
            {"-M2K", "M2K"}, // RUSSELL
            {"-MES", "MES"}, // SPX
            {"-MNQ", "MNQ"}, // NASDAQ
            {"-MYM", "MYM"}, // DOW MICRO
            {"-MGC", "MGC"}, // GOLD
            {"-MCL", "MCL"}, // Micro Cl
            //{"-MSI", "SIL"} // MICRO CL

            /*
            {"-M6A", "M6A"},
            {"-M6B", "M6B"},
            {"-M6E", "M6E"},
            {"-M2K", "M2K"}, // RUSSELL
            {"-MES", "MES"}, // SPX
            {"-MNQ", "MNQ"}, // NASDAQ
            {"-MYM", "MYM"}, // DOW MICRO
            {"-MGC", "MGC"}, // GOLD
            {"-MCL", "MCLE"} // MICRO CL
            */

            /*
            {"QG", "NQG"}, // EMINI NATURAL GAS
            {"QM", "NQM"}, // EMINI QM
            {"M6A", "M6A"},
            {"M6C", "GMCD"}, // CAD MICRO
 
            */
        };

        public List<Symbol> Symbols { get; private set; }
        public readonly Dictionary<string, string> SymbolMap = new Dictionary<string, string>() { };
        public readonly Dictionary<string, double> SymbolMapVolx = new Dictionary<string, double>()
        {
            {"ENQ",3},
            {"MNQ",3},
        };

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="loadSymbols"></param>
        /// <param name="scContractsUrl"></param>
        /// <param name="contractExpirationRolloverDaysOffset"></param>
        public FuturesSymbolManager(List<string> loadSymbols, string scContractsUrl, int contractExpirationRolloverDaysOffset)
        {
            Symbols = new List<Symbol>();
            _scContractsUrl = scContractsUrl;
            _contractExpirationRolloverDaysOffset = contractExpirationRolloverDaysOffset;
            _watchlistName = DefaultQtWatchlistName;

            SetSupportedSymbols(loadSymbols);
        }

        // Public Methods ---------------------------------------------------
        /// <summary>
        /// Initializes the futures symbols to be used.  Retrieves the current contracts from PAG and syncs it with the symbols in Qt
        /// </summary>
        /// <returns></returns>
        public async Task<InitResult> InitializeAsync()
        {
            InitResult result = InitResult.None;
            if (string.IsNullOrEmpty(_scContractsUrl)) // If SC Contract URL is provided, we fetch the contracts and log handle accordingly
            {
                result = await InitializeScContractNames(_scContractsUrl);
            }
            else
            {
                Log.Info("No ScContract url, retrieving default qt future symbols... ");
                CreateQtWatchList(new List<string>()); // intialize with empty list, load system defaults
                result = InitResult.Success;
            }

            if (result == InitResult.Success)
            {
                Log.Info("");
                Log.Trade($"*** Important!!!  replace or add the '{_watchlistName}' watchlist at the start of every day ***");
                Log.Info("");
            }

            return result;
        }

        /// <summary>
        /// Get the PAG symbol name
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        public string GetSymbolName(Symbol symbol)
        {
            KeyValuePair<string, string> sMap = SymbolMap.FirstOrDefault(o => symbol.Root == o.Value);
            if (sMap.Key != null) // if map found, return the mapped value
                return sMap.Key;
            return symbol.Name; // otherwise just return the symbol name from qt
        }

        /// <summary>
        /// Gets the Quantower symbol name, from a PAG name
        /// </summary>
        /// <param name="symbolName"></param>
        /// <returns></returns>
        public string QtSymbolName(string symbolName)
        {
            return SymbolMap[symbolName];
        }

        /// <summary>
        /// Gets the Qt Symbol object from a symbol name.
        /// </summary>
        /// <param name="symbolName"></param>
        /// <returns></returns>
        public Symbol Get(string symbolName)
        {
            Symbol symbol = null;
            if (IsFutureName(symbolName))
                symbol = Symbols.Find(o => o.Root == SymbolMap[symbolName]); // app name (-ES)
            else if (symbolName.Length <= 4)
                symbol = Symbols.Find(o => o.Root == symbolName); // qt name (NQE)
            else
                symbol = Symbols.Find(o => o.Name == symbolName); // contract name
            if (symbol == null)
                Log.Error($"SymbolMgr() unable to find symbol: {symbolName}");
            return symbol;
        }

        /// <summary>
        /// Creates a Qt Watchlist from ScContracts
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public async Task<bool> CreateQtWatchlistFromScContracts(string url)
        {
            try
            {
                var names = await FetchAndTransformScContractNames(url);
                List<string> contracts = names.Select(kvp => kvp.Value).ToList();
                if(contracts.Count == 0)
                {
                    Log.Info("ScContractNames returned no results!");
                    return false;
                }
                else
                {
                    CreateQtWatchList(contracts);
                    Log.Info("ScContractNames successfully processed!");
                    return true;
                }
            }
            catch (Exception ex) 
            { 
                Log.Ex(ex); 
            }
            return false;
        }

        // Protected Methods -----------------------------------------------
        /// <summary>
        /// Create Qt watchlist from specified contractNames
        /// </summary>
        /// <param name="contractNames"></param>
        protected void CreateQtWatchList(List<string> contractNames)//(string[] contractNames)
        {
            Core.RemoveSymbolList(_watchlistName);
            Symbols.Clear();
            if (contractNames.Count > 0)
                Symbols = FuturesContractsToQtSymbols(contractNames);
            else
                Symbols = GetQtFuturesContractsUsingRollover(_contractExpirationRolloverDaysOffset);
            Core.AddSymbolList(_watchlistName, Symbols);
        }

        /// <summary>
        /// Converts Sc contracts to Qt contract names
        /// </summary>
        /// <param name="scContracts"></param>
        /// <returns></returns>
        protected Dictionary<string, string> ConvertScContracts(Dictionary<string, string> scContracts)
        {
            var contracts = new Dictionary<string, string>();
            foreach (var obj in scContracts)
            {
                try
                {
                    string symbol = obj.Key;
                    string scContractName = obj.Value;
                    string transformed = TransformContractName(symbol, scContractName);
                    if (transformed != "" && transformed != null)
                        contracts.Add(symbol, transformed);
                }
                catch (Exception ex) { Log.Info(">>> Error on Transform " + obj.Key + "/" + obj.Value + " :" + ex.Message); }
            }

            return contracts;
        }

        /// <summary>
        /// Transforms the sc contrat name
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="scContractName"></param>
        /// <returns></returns>
        protected string TransformContractName(string symbol, string scContractName)
        {
            string name = "";
            string qtName = SymbolMap[symbol];
            if (!string.IsNullOrEmpty(qtName))
            {
                string scName = symbol.Replace("-", "");
                name = scContractName.Replace(scName, qtName);
            }
            return name;
        }

        // Private Methods -----------------------------------------------


        /// <summary>
        /// Sets the symbol map to be supported in this instance of Qt.  Reducing the number of symbols reduced the overall load and bandwidth req of the system. 
        /// </summary>
        /// <param name="loadSymbols"></param>
        private void SetSupportedSymbols(List<string> loadSymbols)
        {
            bool loadAll = loadSymbols.Count == 0; // load all supported symbols if none provided
            foreach (var obj in _supportedSymbolMap)
            {
                if (loadAll || loadSymbols.Contains(obj.Key)) // load all or if in loadSymbols list
                    SymbolMap.Add(obj.Key, obj.Value);
            }
        }

        /// <summary>
        /// Starts the process of fetching and transforming the contracts used from PAG to Quantower
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private async Task<InitResult> InitializeScContractNames(string url)
        {
            InitResult result;
            var sCsuccess = await CreateQtWatchlistFromScContracts(url);
            if (sCsuccess) // we are not receiving any data from the api, we can't match up the chart data from PAG to Qt
            {
                Log.Error("Failed to retrieve ScContracts");
                result = InitResult.Error;
            }
            else if (Symbols.Count == 0) // there are no default symbols in Qt, usually just means Qt is still initializing
            {
                Log.Error("Failed to set Symbols in ScContractsNames");
                result = InitResult.DelayedRetry;
            }
            else // success
            {
                Log.Info("All available symbols by default");
                foreach (var symbol in Symbols)
                    Log.Info(GetSymbolName(symbol) + "," + symbol.Root + "   " + symbol.Name + ".  Expiration: " + symbol.ExpirationDate.ToShortDateString());
                result = InitResult.Success;
            }
            return result;
        }

        /// <summary>
        /// Is PAG futures symbol by name
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private bool IsFutureName(string name)
        {
            return name.StartsWith("-");
        }

        /// <summary>
        /// Gets the Qt Symbol object from futures contracts names
        /// </summary>
        /// <param name="contractNames"></param>
        /// <returns></returns>
        private List<Symbol> FuturesContractsToQtSymbols(List<string> contractNames)
        {
            List<Symbol> symbols = new List<Symbol>();
            foreach (string contractName in contractNames)
            {
                try
                {
                    if (contractName != "" && contractName != null)
                    {
                        Symbol sym = Core.GetSymbol(new GetSymbolRequestParameters
                        {
                            SymbolId = "F.US." + contractName
                        }, 
                        null, NonFixedListDownload.Download);

                        if (sym != null)
                            symbols.Add(sym);
                    }
                }
                catch (Exception ex) 
                { 
                    Log.Ex(ex); 
                }
            }
            return symbols;
        }

        /// <summary>
        /// Gets Qt Symbols based on contract expiration rollover rules
        /// </summary>
        /// <param name="contractExpirationRolloverDaysOffset"></param>
        /// <returns></returns>
        private List<Symbol> GetQtFuturesContractsUsingRollover(int contractExpirationRolloverDaysOffset)
        {
            List<Symbol> symbols = new List<Symbol>();
            foreach (var s in SymbolMap)
            {
                string symbol = s.Value;
                Symbol matchedSymbol = FindClosestMatchToRollover(symbol, contractExpirationRolloverDaysOffset);
                if (matchedSymbol != null)
                    symbols.Add(matchedSymbol);
            }
            return symbols;
        }

        /// <summary>
        /// Find the Qt Symbol basd on specified contract rules
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="contractExpirationRolloverDaysOffset"></param>
        /// <returns></returns>
        private Symbol FindClosestMatchToRollover(string symbol, int contractExpirationRolloverDaysOffset)
        {
            const int checkMonthsOffset = 5;
            string[] futureMonthCodes = ["F", "G", "H", "J", "K", "M", "N", "Q", "U", "V", "X", "Z"];
            int expirationDateOffset = contractExpirationRolloverDaysOffset;
            var now = DateTime.Today;
            var date = new DateTime(now.Year, now.Month, 1).AddMonths(-checkMonthsOffset);
            var endDate = new DateTime(now.Year, now.Month, 1).AddMonths(checkMonthsOffset);

            while (date < endDate)
            {
                string monthCode = futureMonthCodes[date.Month - 1];
                string contractName = $"{symbol}{monthCode}{date.Year - 2000}";
                Symbol sym = Core.GetSymbol(new GetSymbolRequestParameters
                {
                    SymbolId = "F.US." + contractName
                }, 
                null, NonFixedListDownload.Download);

                if (sym != null)
                {
                    var expiration = sym.ExpirationDate;
                    expiration = expiration.AddDays(-expirationDateOffset);
                    if (now <= expiration)
                        return sym;
                }
                date = date.AddMonths(1);
            }
            return null;
        }

        /// <summary>
        /// Retrieves ScContract names used by PAG and converts to Qt contract names
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        async private Task<Dictionary<string, string>> FetchAndTransformScContractNames(string url)
        {
            var names = new Dictionary<string, string>();
            Log.Info("Retrieving ScContractNames... " + url);
            using (var client = new HttpClient())
            {
                using (HttpResponseMessage res = await client.GetAsync(url)) // fetch
                {
                    using (HttpContent content = res.Content) // read content
                    {
                        var data = await content.ReadAsStringAsync();
                        if (data == null) // no content received
                            Log.Info("ScContractNames() received no data.");
                        else // deserialize and convert scContract to Qt names
                        {
                            Log.Info("ScContractNames() data received.  Processing...");
                            Dictionary<string, string> obj = JsonSerializer.Deserialize<Dictionary<string, string>>(data);
                            names = ConvertScContracts(obj);
                        }
                    }
                }
            }
            return names;
        }
    }
}
