namespace QtBridgeStrategy.Enums
{
    /// <summary>
    /// Commands from the PriceActionGroup api
    /// </summary> 
    public enum Command
    {
        /// <summary>Client pings for a response.</summary>
        Ping = 0,

        /// <summary>Is Quantower connected to its server.</summary>
        IsConnected = 1,

        /// <summary>Client requests disconnect from Quantower bridge.</summary>
        Disconnect = 2,

        /// <summary>Client subscribes to symbol's live price, as provided by Quantower.</summary>
        SubSymbolPrice = 3,

        /// <summary>Client unsubscribes to symbol's live price.</summary>
        UnsubSymbolPrice = 4,

        /// <summary>Client requests live symbol price, as provided by Quantower.</summary>
        GetSymbolPrice = 5,

        /// <summary>Client requests account information, as provided by Quantower.</summary>
        GetAccount = 8,

        /// <summary>Client requests orders information, as provided by Quantower.</summary>
        GetOrders = 11,

        /// <summary>Client requests positions information, as provided by Quantower.</summary>
        GetPositions = 14,

        /// <summary>Client requests symbol information, as provided by Quantower.</summary>
        GetSymbolInfo = 15,

        /// <summary>Client places order.</summary>
        PlaceOrder = 16,

        /// <summary>Client modifies order.</summary>
        ModifyOrder = 17,

        /// <summary>Client cancels order.</summary>
        CancelOrder = 18,

        /// <summary>Client flattens all orders and positions.</summary>
        Flatten = 19,

        /// <summary>Client initializes connection to this strategy.</summary>
        Init = 20,

        /// <summary>Not Implemented.</summary>
        ReplaceOrder = 21
    }
}
