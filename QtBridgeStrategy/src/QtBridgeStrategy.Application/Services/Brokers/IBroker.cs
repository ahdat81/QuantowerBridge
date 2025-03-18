using QtBridgeStrategy.Enums;
using QtBridgeStrategy.Models;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;

namespace QtBridgeStrategy.Services.Brokers
{
    /// <summary>
    /// Interface for the Broker class
    /// </summary>
    public interface IBroker
    {
        Task<InitResult> InitializeAsync();
        void CheckOrders();
        void CheckStopFailsafe();
        void ConvertBridgeOrderToTick(BridgeOrder order, Symbol symbol);
        Account GetAccount();
    }
}
