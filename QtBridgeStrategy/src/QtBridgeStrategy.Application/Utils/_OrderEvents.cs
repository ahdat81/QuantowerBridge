using QtBridgeStrategy.Models;
using TradingPlatform.BusinessLayer;

namespace QtBridgeStrategy.Utils
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="text"></param>
    /// <param name="level"></param>
    public readonly struct LogEventArgs(string text, int level)
    {
        public string Text { get; } = text;
        public int Level { get; } = level;
    };

    public readonly struct PlacePreOrderEventArgs(string id, PlaceOrderRequestParameters parameters, BridgeOrder bridgeOrder)
    {
        public PlaceOrderRequestParameters Parameters { get; } = parameters;
        public BridgeOrder BridgeOrder { get; } = bridgeOrder;
        public string Id { get; } = id;
    };

    public readonly struct ModifyPreOrderEventArgs(string id, ModifyOrderRequestParameters parameters, BridgeOrderModification bridgeOrderModification)
    {
        public string Id { get; } = id;
        BridgeOrderModification BridgeOrderModification { get; } = bridgeOrderModification;
        public ModifyOrderRequestParameters Parameters { get; } = parameters;
    };

    public readonly struct CancelPreOrderEventArgs(string id)
    {
        public string Id { get; } = id;
    };

    public readonly struct FlattenPreOrderEventArgs(string symbol)
    {
        public string Symbol { get; } = symbol;
    };
}
