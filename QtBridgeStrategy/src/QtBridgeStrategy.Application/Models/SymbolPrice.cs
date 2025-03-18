namespace QtBridgeStrategy.Models
/// <summary>
/// Contains the bid/ask price for a symbol
/// </summary>
{
    public struct SymbolPrice
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.

        public string symbol { get; set; }
        public double bid { get; set; }
        public double ask { get; set; }
    }
}
