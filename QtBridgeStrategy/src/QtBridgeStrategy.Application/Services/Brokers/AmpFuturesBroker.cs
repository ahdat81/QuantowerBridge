using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;
using System.Collections.Generic;
using System.Linq;
using QtBridgeStrategy.Enums;
using QtBridgeStrategy.Models;
using QtBridgeStrategy.Utils;

namespace QtBridgeStrategy.Services.Brokers
{
    /// <summary>
    /// Amp Futures Broker class specifically for Amp Futures and futures contracts
    /// </summary>
    public class AmpFuturesBroker : Broker
    {
        private FuturesContractNamesSource _futuresContractNamesSource;
        private string _futuresContractNamesSourceUrl;
        private int _contractExpirationRolloverDaysOffset;
        private FuturesSymbolManager _futuresSymbolMgr;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="futuresContractNamesSource"></param>
        /// <param name="futuresContractNamesSourceUrl"></param>
        /// <param name="contractExpirationRolloverDaysOffset"></param>
        public AmpFuturesBroker(FuturesContractNamesSource futuresContractNamesSource, string futuresContractNamesSourceUrl, int contractExpirationRolloverDaysOffset)
        {
            _futuresContractNamesSource = futuresContractNamesSource;
            _futuresContractNamesSourceUrl = futuresContractNamesSourceUrl;
            _contractExpirationRolloverDaysOffset = contractExpirationRolloverDaysOffset;
        }

        /// <summary>
        /// Initializes and syncs the futures contracts between PAG and Quantower
        /// </summary>
        /// <returns></returns>
        public new async Task<InitResult> InitializeAsync()
        {
            _futuresSymbolMgr = new FuturesSymbolManager(new List<string>(), _futuresContractNamesSourceUrl, _contractExpirationRolloverDaysOffset);
            var initResult = await _futuresSymbolMgr.InitializeAsync();
            if (initResult == InitResult.Success)
            {
                OrderMgr = new OrderMgr();
            }
            return initResult;
        }

        /// <summary>
        /// Check orders for order management.  Should be called on an interval.
        /// </summary>
        public new void CheckOrders()
        {
            OrderMgr.OrderChecks();
        }

        /// <summary>
        /// Get PAG symbol name from Qt Symbol
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        public new string GetSymbolName(Symbol symbol)
        {
            KeyValuePair<string, string> sMap = _futuresSymbolMgr.SymbolMap.FirstOrDefault(o => symbol.Root == o.Value);
            if (sMap.Key != null) // if map found, return the mapped value
                return sMap.Key;
            return symbol.Name; // otherwise just return the symbol name from qt
        }

        /// <summary>
        /// Get Qt Symbol from symbol name
        /// </summary>
        /// <param name="symbolName"></param>
        /// <returns></returns>
        public new Symbol GetSymbol(string symbolName)
        {
            return _futuresSymbolMgr.Get(symbolName);
        }

        /// <summary>
        /// Get all symbols that are currently synced between PAG and Qt
        /// </summary>
        /// <returns></returns>
        public new List<Symbol> GetSymbols()
        {
            return _futuresSymbolMgr.Symbols;
        }

        /// <summary>
        /// Get Qt symbol name from PAG name
        /// </summary>
        /// <param name="symbolName"></param>
        /// <returns></returns>
        public new string QtSymbolName(string symbolName)
        {
            return _futuresSymbolMgr.QtSymbolName(symbolName);
        }

        /// <summary>
        /// Converts order prices to their ticked value for tighter precision.
        /// </summary>
        /// <param name="order"></param>
        /// <param name="symbol"></param>
        public new void ConvertBridgeOrderToTick(BridgeOrder order, Symbol symbol)
        {
            if (order.stop > 0)
                order.stop = Util.ConvertPriceTicked(symbol.TickSize, order.stop);
            if (order.entry > 0)
                order.entry = Util.ConvertPriceTicked(symbol.TickSize, order.entry);
            if (order.target > 0)
                order.target = Util.ConvertPriceTicked(symbol.TickSize, order.target);
        }
    }
}
