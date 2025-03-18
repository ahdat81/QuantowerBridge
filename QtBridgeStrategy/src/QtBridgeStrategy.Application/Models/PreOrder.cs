using TradingPlatform.BusinessLayer;

namespace QtBridgeStrategy.Models
{
    /// <summary>
    /// Represents an internal Pre-Order.  Primarily used for easy Json serialization.
    /// </summary>
    public class PreOrder
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.

        public string id;
        public PlaceOrderRequestParameters parameters;
        public bool enabled;

        public PreOrder(string id, PlaceOrderRequestParameters parameters, bool enabled)
        {
            this.id = id;
            this.enabled = enabled;
            this.parameters = parameters;
        }
    }
}
