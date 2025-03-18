namespace QtBridgeStrategy.Models
{
    /// <summary>
    /// Represents the properties to modify a Bridge Order.  Primarily used for Json serialization.
    /// </summary>
    public struct BridgeOrderModification
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.

        public string id { get; set; }
        public string symbol { get; set; }
        public double price { get; set; }
        public string orderType { get; set; }
        public double p1 { get; set; }
        public double p2 { get; set; }
    }
}
