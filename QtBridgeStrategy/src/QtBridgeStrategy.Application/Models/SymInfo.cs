namespace QtBridgeStrategy.Models
// This struct is named SymInfo to not conflict with the SymbolInfo class used by Quantower.
{
    /// <summary>
    /// Information about a symbol. Primarily used for Json serialization back to PAG.
    /// </summary>
    public struct SymInfo
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.

        public string symbol { get; set; }
        public string localSymbol { get; set; }
        public double tickSize { get; set; }
        public string description { get; set; }

        /// <summary> Expiration of the futures contract </summary>
        public string expirationDate { get; set; }
    }
}
