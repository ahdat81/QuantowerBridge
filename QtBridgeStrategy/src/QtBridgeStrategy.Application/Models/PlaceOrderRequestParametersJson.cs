using TradingPlatform.BusinessLayer;

namespace QtBridgeStrategy.Models
{
    /// <summary>
    /// Quantower object to be passed to place a trade.  This is primarily used for Json serialization. 
    /// </summary>
    public struct PlaceOrderRequestParametersJson
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.

        public string id { get; set; }
        public string accountId { get; set; }
        public Side side { get; set; }
        public string comment { get; set; }
        public double stopLossTicks { get; set; }
        public double takeProfitTicks { get; set; }
        public double qty { get; set; }
        public string rootSymbol { get; set; }
        public string contract { get; set; }
        public double price { get; set; }
        public string orderTypeId { get; set; }
        public bool enabled { get; set; }

        public PlaceOrderRequestParametersJson(string id, string accountId, Side side, string comment, double qty, double price, string orderTypeId, string contract, string rootSymbol, double stopLossTicks, double takeProfitTicks, bool enabled)
        {
            this.id = id;
            this.accountId = accountId;
            this.side = side;
            this.comment = comment;
            this.qty = qty;
            this.price = price;
            this.orderTypeId = orderTypeId;
            this.contract = contract;
            this.rootSymbol = rootSymbol;
            this.stopLossTicks = stopLossTicks;
            this.takeProfitTicks = takeProfitTicks;
            this.enabled = enabled;
        }
    }
}
