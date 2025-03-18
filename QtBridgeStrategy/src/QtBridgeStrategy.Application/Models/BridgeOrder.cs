namespace QtBridgeStrategy.Models
{
    /// <summary>
    /// Represents our internal Order, which is not a live order.  This must be converted to a Quantower order to become live. 
    /// </summary>
    public struct BridgeOrder
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.

        /// <summary> Original Id that will follow all subsequent orders created/modified throughout the life of the order.  We also use this to track Quantower orders back to its source.</summary>
        public string ogId { get; set; }

        /// <summary> A preOrder is an order is not live, but is internally tracked.  It will convert to a live order once a trigger price has been reached.  This allows us to place many orders taking away margin from the account. </summary>
        public int preOrder { get; set; } // hacky, 0: not pre order, -1: pre order, but not enabled, 1: enabled preorder

        /// <summary> When an order is live, and this trigger price is reached, the stop will move to the moveStopPrice</summary>
        public double moveStopTriggerPrice { get; set; }

        /// <summary>Price the stop will be moved to once the moveStopTriggerPrice is reached.</summary>
        public double moveStopPrice { get; set; }

        /// <summary>A non-live order will be canceled if this price is reached.</summary>
        public double cancelPrice { get; set; }

        /// <summary>This will prevent non-live orders to be cancelled EOD, and will persist until cancelled by the user.</summary>
        public bool persistentOrder { get; set; } 

        public string symbol { get; set; }
        public int orderType { get; set; }
        public double qty { get; set; }
        public double entry { get; set; }
        public double stop { get; set; }
        public double target { get; set; }
        public string contractName { get; set; }


    }
}
