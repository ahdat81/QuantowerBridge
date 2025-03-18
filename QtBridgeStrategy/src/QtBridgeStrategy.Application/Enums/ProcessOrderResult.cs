namespace QtBridgeStrategy.Enums
{
    /// <summary>
    /// Represents different statuses for a bridge order (order managed within this strategy).
    /// </summary> 
    public enum ProcessOrderResult
    {
        /// <summary>Internal order that is open, and not triggered.</summary>
        None,

        /// <summary>Internal order that has been triggered.  The entry price has been reached and a live order has been placed.</summary>
        Triggered,

        /// <summary>Internal order that has been stopped.  The internal order will simply be cancelled, no live order has been placed.</summary>
        Stopped,

        /// <summary>Internal order that has been stopped and triggered at the same time.  This can occur during moments of extreme volitility, such as news.</summary>
        StopAndTriggered
    }
}
