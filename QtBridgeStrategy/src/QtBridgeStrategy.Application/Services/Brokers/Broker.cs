using QtBridgeStrategy.Enums;
using QtBridgeStrategy.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace QtBridgeStrategy.Services.Brokers
{
    public class Broker : QtBridgeStrategy, IBroker
    {
        /// <summary>
        /// Base broker class, implements IBroker
        /// </summary>
        public string Name { get; set; }
        public OrderMgr OrderMgr { get; set; }

        /// <summary>
        /// Initializes the class
        /// </summary>
        /// <returns></returns>
        public async Task<InitResult> InitializeAsync()
        {
            await Task.Delay(100);
            return InitResult.Success;
        }

        /// <summary>
        /// Check orders for order management
        /// </summary>
        public void CheckOrders()
        {
            OrderMgr.CheckStopFailsafe();
        }

        /// <summary>
        /// Check orders for failsafe triggers
        /// </summary>
        public void CheckStopFailsafe()
        {

        }

        /// <summary>
        /// Get symbol name from Qt Symbol
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        public string GetSymbolName(Symbol symbol)
        {
            return symbol.Name; // otherwise just return the symbol name from qt
        }

        /// <summary>
        /// Gets Qt Symbol from name
        /// </summary>
        /// <param name="orderSymbolName"></param>
        /// <returns></returns>
        public Symbol GetSymbol(string orderSymbolName)
        {
            return Core.GetSymbol(new GetSymbolRequestParameters
            {
                SymbolId = orderSymbolName
            }, null, NonFixedListDownload.Download);

            //Symbol symbol = null;
            //return symbol;
        }


        /// <summary>
        /// Gets all validated symbols synced between PAG and Qt
        /// </summary>
        /// <returns></returns>
        public List<Symbol> GetSymbols()
        {
            return new List<Symbol>();
        }

        /// <summary>
        /// Get Qt symbol name from PAG symbol name
        /// </summary>
        /// <param name="symbolName"></param>
        /// <returns></returns>
        public string QtSymbolName(string symbolName)
        {
            return symbolName;
        }

        /// <summary>
        /// Nothing to do by default
        /// </summary>
        /// <param name="order"></param>
        /// <param name="symbol"></param>
        public void ConvertBridgeOrderToTick(BridgeOrder order, Symbol symbol)
        {
            // do nothing by default
        }

        /// <summary>
        /// Get default account
        /// </summary>
        /// <returns>Account or null if not found.</returns>
        public Account GetAccount()
        {
            return Core.Accounts.Length > 0 ? Core.Accounts[0] : null;
        }
    }
}
