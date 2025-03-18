namespace QtBridgeStrategy.Models
{
    /// <summary>
    /// Represents Information from a Quantower Account
    /// </summary>
    public struct AccountInfo
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.
        public string id { get; set; }
        public double realizedPnL { get; set; }
        public double totalPnL { get; set; }
        public double marginCredit { get; set; }
        public double totalMargin { get; set; }
        public double positionMargin { get; set; }
        public double balance { get; set; }
    }
}
