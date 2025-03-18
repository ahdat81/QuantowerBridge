namespace QtBridgeStrategy.Models
{
    /// <summary>
    /// Additional order information for an order. We save this in the comment of a Quantower order for tracking.
    /// </summary>
    public struct OrderInfoAdditional
    {
        // TODO properties are currently lowercase to match the name coming from PAG to make JSON serialization easier.

        public string ogId { get; set; }
        public double ogStop { get; set; }
        public double ogEntry { get; set; }
        public double ogTarget { get; set; }
        public double cancel { get; set; }
        public bool ctoh { get; set; } //cancelTgOffsetHack
        public bool stph { get; set; } //StoplossTriggerPriceHack
        public bool persistentOrder { get; set; }
        public string groupId { get; set; }
        public bool isAddedOrder { get; set; }
    }
}
