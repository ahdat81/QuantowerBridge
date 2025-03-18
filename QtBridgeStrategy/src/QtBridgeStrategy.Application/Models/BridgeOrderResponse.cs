namespace QtBridgeStrategy.Models
{
    /// <summary>
    /// Resprents the response to be sent back to the client after placing a BridgeOrder.  Primarily used for Json serialization
    /// </summary>
    public struct BridgeOrderResponse
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.

        public string symbol { get; set; }
        public string account { get; set; }
        public int broker { get; set; }
        public string ogId { get; set; }

        /// <summary>A BridgeOrder may be made up of one or more Quantower orders</summary>
        public string[] orderIds { get; set; }

        /// <summary>A BridgeOrder may be made up of one or more Quantower target orders</summary>
        public double[] targets { get; set; }


        public BridgeOrderResponse()
        {
            orderIds = new string[2]; // currently only support 2 orders
            targets = new double[2];// currently only support 2 orders
            broker = 4; // TODO default broker from Quantower.  Make it to setting?
        }
    }
}
