namespace QtBridgeStrategy.Models
{
    /// <summary>
    /// Informaiton about an Order.  Primarily used for Json serialization.
    /// </summary>
    public struct OrderInfo
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.

        public string id { get; set; }
        public string symbol { get; set; }
        public double entry { get; set; }
        public double stop { get; set; }
        public double target { get; set; }
        public string side { get; set; }
        public double qty { get; set; }
        public string date { get; set; }
        public string status { get; set; }
        public string orderType { get; set; }
        public double ogStop { get; set; }
        public double ogEntry { get; set; }
        public double ogTarget { get; set; }
        public string ogId { get; set; }
        public double cancel { get; set; }
        public double moveStopTriggerPrice { get; set; }
        public double moveStopPrice { get; set; }
        public bool persistentOrder { get; set; }
        public int isPreOrder { get; set; }
    }
}
