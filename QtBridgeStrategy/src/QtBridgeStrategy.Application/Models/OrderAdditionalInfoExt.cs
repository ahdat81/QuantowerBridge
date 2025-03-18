namespace QtBridgeStrategy.Models
{
    /// <summary>
    /// Additional Order information that we cannot save back to a Quantower order.  
    /// In Quantower, an order's comment is locked once a trade is live.  
    /// So we still need to track these properties internally throughout the life of the order.
    /// </summary>
    public struct OrderAdditionalInfoExt
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.

        public string ogId { get; set; }
        public double moveStopTriggerPrice { get; set; }
        public double moveStopPrice { get; set; }
        public bool persistentOrder { get; set; }
        public bool stph { get; set; }
        public double tgPrice { get; set; }
        public double stopPrice { get; set; }

        // pre-order, inactive, active (+pos) order
        // user flatten, pre order processing, auto eod close
        // ignore auto remove for pre, inactive and active pos. ignores all processing
        //public bool ltt;
        // ignore user flatten for pre, inactive, active (+pos) orders
        //public double entryPrice;
        //public string groupId;
        //public double lastPrice;
        // auto remove needs smarts for persistentorder
        // user flatten, close all preorders, active, inactive, pos
        // eod, dont flatten preorder, inactive, active
        // if we do need to flatten pos, selectively find pos, matching orders.  close orders, flatten pos
        // failsafe can get triggered here
    }
}
