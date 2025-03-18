namespace QtBridgeStrategy.Models
{
    /// <summary>
    /// Information about the account's positions.  Primarily used for Json serialization.
    /// </summary>
    public struct PositionsInfo
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.

        public string symbol { get; set; }
        public double qty { get; set; }
        public double pnl { get; set; }
        public double grossPnL { get; set; }
        public double grossPnLTicks { get; set; }
        public double netPnl { get; set; }
    }
}
