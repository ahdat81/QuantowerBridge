using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;
using static QtBridgeStrategy.Utils.Util;
using System.Text.Json;
using static QtBridgeStrategy.Constants.Constants;
using QtBridgeStrategy.Models;
using QtBridgeStrategy.Enums;
using System.Security.Cryptography;

namespace QtBridgeStrategy.Services.Brokers
{
    /// <summary>
    /// Class that manages and processes our internal PreOrders and Qt's Orders
    /// </summary>
    public class OrderMgr : QtBridgeStrategy
    {
        private struct SymFailsafeCount
        {
            public Symbol symbol { get; set; }
            public int count { get; set; }
            public SymFailsafeCount(Symbol symbol, int count)
            {
                this.symbol = symbol;
                this.count = count;
            }
        }

        private bool _enableStopFailsafe;
        private int _stopTriggerPriceTicksOffset = 5;
        private bool _closeAllOrdersEOD;
        private bool _closeAllPreOrdersEOD;
        private bool _autoManageTargetOrders;
        private int _inactiveTradingBeginHour;
        private int _inactiveTradingBeginMin;
        private int _inactiveTradingEndHour;
        private int _inactiveTradingEndMin;
        private int _inactiveNewTradingBeginHour;
        private int _inactiveNewTradingBeginMin;
        private double _preOrderTriggerTicks;
        private double _preOrderMaxSpreadTicks;
        private bool _initialized = false;
        private bool _enableTrading = false; // enable any trading
        private bool _enableNewTrading = false; // enable new orders
        private List<Order> _stopTriggerPriceHackApplied;
        private List<CheckStopFailsafeSymbol> _checkStopFailsafeSymbols;

        public PreOrderMgr PreOrderMgr;
        public OrderAdditionalInfoMgr InfoMgr;

        /// <summary>
        /// Constructor
        /// </summary>
        public OrderMgr()
        {
            // Set user QtStrategy settings
            _stopTriggerPriceTicksOffset = StopTriggerPriceTicksOffset;
            _enableStopFailsafe = EnableStopFailsafe;
            _closeAllOrdersEOD = CloseAllOrdersEOD;
            _closeAllPreOrdersEOD = CloseAllPreOrdersEOD;
            _autoManageTargetOrders = AutoManageTargetOrders;
            _inactiveTradingBeginHour = InactiveTradingBeginHour;
            _inactiveTradingBeginMin = InactiveTradingBeginMin;
            _inactiveTradingEndHour = InactiveTradingEndHour;
            _inactiveTradingEndMin = InactiveTradingEndMin;
            _inactiveNewTradingBeginHour = InactiveNewTradingBeginHour;
            _inactiveNewTradingBeginMin = InactiveNewTradingBeginMin;
            _preOrderTriggerTicks = PreOrderTriggerTicks;
            _preOrderMaxSpreadTicks = PreOrderMaxSpreadTicks;


            _checkStopFailsafeSymbols = new List<CheckStopFailsafeSymbol>();
            _stopTriggerPriceHackApplied = new List<Order>();
            InfoMgr = new OrderAdditionalInfoMgr();
            PreOrderMgr = new PreOrderMgr(_preOrderTriggerTicks, _preOrderMaxSpreadTicks, InfoMgr);
            if (Core.Orders.Length <= 0 && Core.Positions.Length <= 0 && PreOrderMgr.Count() <= 0)
                InfoMgr.Clear();

            Core.OrderAdded += OrderAddedEvent;
            Core.OrderRemoved += OrderRemovedEvent;
            _initialized = true;
        }


        /// <summary>
        /// Get live Qt stop orders
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        public IEnumerable<Order> GetStopOrders(Symbol symbol)
        {
            return Core.Orders.Where(o => o.Symbol == symbol && o.OrderTypeId == OrderType.Stop);
        }

        /// <summary>
        /// Get live Qt target orders
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        public IEnumerable<Order> GetTgOrders(Symbol symbol)
        {
            return Core.Orders.Where(o => o.Symbol == symbol && o.OrderTypeId == OrderType.Limit && o.StopLoss == null);
        }

        /// <summary>
        /// Get Qt positions
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        public IEnumerable<Position> GetPositions(Symbol symbol)
        {
            return Core.Positions.Where(o => o.Symbol == symbol && o.Quantity != 0);
        }

        /// <summary>
        /// Get Qt position by symbol
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        public Position GetPosition(Symbol symbol)
        {
            var positions = GetPositions(symbol);
            return positions.Count() > 0 ? positions.ElementAt(0) : null;
        }

        /// <summary>
        /// Check if symbol has stop orders
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        public bool HasStopOrders(Symbol symbol)
        {
            return GetStopOrders(symbol).Count() > 0;
        }

        /// <summary>
        /// Check if symbol has target ordres
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        public bool HasTgOrders(Symbol symbol)
        {
            return GetTgOrders(symbol).Count() > 0;
        }

        /// <summary>
        /// Check if symbol has positions
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        public bool HasPosition(Symbol symbol)
        {
            return GetPosition(symbol) != null;
        }

        /// <summary>
        /// Checks the total qty of all stop orders
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        public double GetStopQtyCount(Symbol symbol)
        {
            var stopOrders = GetStopOrders(symbol);
            double shortCount = stopOrders.Where(o => o.Side == Side.Buy).Sum(o => o.RemainingQuantity);
            double longCount = stopOrders.Where(o => o.Side == Side.Sell).Sum(o => o.RemainingQuantity);
            double stopQtyCount = shortCount + longCount;
            return stopQtyCount;
        }

        /// <summary>
        /// Check orders for order managment rules and failsafes
        /// </summary>
        public async void OrderChecks()
        {
            if (!_initialized)
                return;

            SetEnableTrading();
            //StopTriggerPriceHack();
            CheckCancelPrice();
            CheckMoveStopTriggerPrice();
            //CheckStopFailsafe();

            if (_enableTrading & _enableNewTrading) // all clear for trading
            {
                await ProcessPreOrders(true, true);
            }
            else if (_enableNewTrading && !_enableTrading) // only new trading but all other trading is active, this shouldnt happen.  
            {
                Log.Error("Active times are incorrrect.  NewTrade time should be before ActiveTrade times.");
            }
            else // new trades is disabled, but we're not closing all trades yet
            {
                // handle preorders
                if (_closeAllPreOrdersEOD) // close all pre orders
                    await CloseOrders(null, false, false, 0, false);
                else // dont trigger new pre orders, but check if pre orders have been stopped out
                    await ProcessPreOrders(false, true);

                // handle live orders and positions
                if (!_enableTrading) // no more trading allowed.  close all live orders and associated positions that are not persistent
                {
                    // we just close all non-persistent orders, let failsafe handle the rest
                    await CloseOrders(null, false, true, 0, false); // close live orders appropriately
                }
                else if (!_enableNewTrading) // no more new orders allowed, close live orders that havent been triggered
                {
                    await CloseOrders(null, true, true, 0, false);
                }
            }
        }

        /// <summary>
        /// Create pre-order no preMsg
        /// </summary>
        /// <param name="id"></param>
        /// <param name="parameters"></param>
        /// <param name="isPreOrder"></param>
        /// <param name="infoExt"></param>
        /// <returns></returns>
        public string CreateOrder(string id, PlaceOrderRequestParameters parameters, int isPreOrder, OrderAdditionalInfoExt infoExt)
        {
            return CreateOrder(id, parameters, isPreOrder, "", infoExt);
        }

        /// <summary>
        /// Create an order
        /// </summary>
        /// <param name="id"></param>
        /// <param name="parameters"></param>
        /// <param name="isPreOrder"></param>
        /// <param name="preMsg"></param>
        /// <param name="infoExt"></param>
        /// <returns></returns>
        public string CreateOrder(string id, PlaceOrderRequestParameters parameters, int isPreOrder, string preMsg, OrderAdditionalInfoExt infoExt)
        {
            try
            {
                if (!IsObjDefault<OrderAdditionalInfoExt>(infoExt))
                    InfoMgr.Update(infoExt);

                if (isPreOrder == 0) // live order
                {
                    string symbolName = parameters.Symbol.Name;
                    Log.Order(preMsg, "placing order", id, symbolName, "...");
                    var res = Core.PlaceOrder(parameters);
                    var msg = res?.Message ?? "no response. success?";
                    if (res.Message != null && res.Message.Length > 0)
                        Log.OrderError("error", "placing order", id, symbolName, msg);
                    return res.OrderId;
                }
                else // preorder
                {
                    PreOrderMgr.Add(id, parameters, isPreOrder == 1);
                    return id;
                }
            }
            catch (Exception ex) { Log.Ex(ex); }
            return null;
        }

        /// <summary>
        /// Modify bridge order
        /// </summary>
        /// <param name="o"></param>
        /// <param name="sym"></param>
        public void ModifyBridgeOrder(BridgeOrderModification o, Symbol sym)
        {
            string id = o.id;
            double price = ConvertPriceTicked(sym.TickSize, o.price);
            string orderType = o.orderType;
            bool isPreOrder = IsGuid(id);
            //Log.Info("attempting to modify order... " + symbol + ": " + id);

            if (isPreOrder)
                ModifyBridgePreOrder(id, orderType, price, o);
            else
                ModifyBridgeOrder(id, orderType, price, o);
        }

        /// <summary>
        /// Modify Bridge PreOrder
        /// </summary>
        /// <param name="id"></param>
        /// <param name="orderType"></param>
        /// <param name="price"></param>
        /// <param name="o"></param>
        public void ModifyBridgePreOrder(string id, string orderType, double price, BridgeOrderModification o)
        {
            //var parameters = preOrders.Find(o => o.id == id).parameters;
            // verify this is reference updated
            var preOrder = PreOrderMgr.Get(id);
            var order = preOrder.parameters;
            OrderInfoAdditional additionalInfo = GetAdditionalInfo(order.Comment);
            var moreInfos = GetAdditionalInfoExt(additionalInfo.ogId);

            if (orderType == "entry")
            {
                if (order.StopLoss != null)
                {
                    double stopPrice = CalcPrice(order.Symbol, order.Side, order.Price, order.StopLoss.Price, "stop");
                    if (order.Side == Side.Buy && stopPrice <= price || order.Side == Side.Sell && stopPrice >= price)
                    {
                        order.StopLoss = SlTpHolder.CreateSL(CalcTicks(order.Symbol, order.Side, price, stopPrice, "stop"), PriceMeasurement.Absolute, false, order.StopLoss.Quantity, double.NaN, true);
                        //order.TriggerPrice = stopPrice;
                    }
                    moreInfos.stopPrice = stopPrice;
                }
                order.Price = price;
                additionalInfo.ogEntry = price;
                order.Comment = JsonSerializer.Serialize(additionalInfo);

                if (o.p1 != 0)
                    moreInfos.moveStopTriggerPrice = o.p1;
                if (o.p2 != 0)
                    moreInfos.moveStopPrice = o.p2;
            }
            else if (orderType == "stop")
            {
                if (order.StopLoss != null)
                {
                    //additionalInfo.ogStop = price;
                    order.StopLoss = SlTpHolder.CreateSL(CalcTicks(order.Symbol, order.Side, order.Price, price, "stop"), PriceMeasurement.Absolute, false, order.StopLoss.Quantity, double.NaN, true);
                    order.Comment = JsonSerializer.Serialize(additionalInfo);
                }
                else
                    order.TriggerPrice = price;
                moreInfos.stopPrice = price;
            }
            else if (orderType == "target")
            {
                if (order.TakeProfit != null)
                    order.TakeProfit = SlTpHolder.CreateTP(CalcTicks(order.Symbol, order.Side, order.Price, price, "target"), PriceMeasurement.Absolute, order.TakeProfit.Quantity, double.NaN, true);
                else
                    order.Price = price;
                moreInfos.tgPrice = price;
            }
            else if (orderType == "cancel")
            {
                // seems like the order must be recreated to change the comment.  so we're going to offset the tg a bit back and forth to force the order recreation
                int offset = additionalInfo.ctoh ? -1 : 1;
                additionalInfo.ctoh = !additionalInfo.ctoh;
                additionalInfo.cancel = price;
                order.Comment = JsonSerializer.Serialize(additionalInfo);
                order.TakeProfit = SlTpHolder.CreateTP(order.TakeProfit.Price + offset, order.TakeProfit.PriceMeasurement, order.TakeProfit.Quantity, order.TakeProfit.QuantityPercentage, order.TakeProfit.Active);
            }
            else if (orderType == "enablepreorder")
                preOrder.enabled = price != 0 ? true : false;
            else if (orderType == "persistentorder")
                moreInfos.persistentOrder = price != 0 ? true : false;
            else if (orderType == "persistentorderenable")
                moreInfos.persistentOrder = price != 0 ? true : false;
            else if (orderType == "movestoptriggerprice")
                moreInfos.moveStopTriggerPrice = price;
            else if (orderType == "movestopprice")
                moreInfos.moveStopPrice = price;

            preOrder.parameters = order;
            UpdateOrder(preOrder, moreInfos);
        }

        /// <summary>
        /// Modify Bridge Order
        /// </summary>
        /// <param name="id"></param>
        /// <param name="orderType"></param>
        /// <param name="price"></param>
        /// <param name="o"></param>
        public void ModifyBridgeOrder(string id, string orderType, double price, BridgeOrderModification o)
        {
            Order order = Core.GetOrderById(id);
            OrderInfoAdditional additionalInfo = GetAdditionalInfo(order);
            OrderAdditionalInfoExt moreInfos = GetAdditionalInfoExt(order);

            ModifyOrderRequestParameters parameters = new ModifyOrderRequestParameters(order);
            if (orderType == "entry")
            {
                if (parameters.StopLoss != null)
                {
                    double stopPrice = CalcPrice(order.Symbol, order.Side, order.Price, order.StopLoss.Price, "stop");
                    if (order.Side == Side.Buy && stopPrice <= price || order.Side == Side.Sell && stopPrice >= price)
                    {
                        parameters.StopLoss = SlTpHolder.CreateSL(CalcTicks(order.Symbol, order.Side, price, stopPrice, "stop"), PriceMeasurement.Absolute, false, order.StopLoss.Quantity, double.NaN, true);
                        parameters.TriggerPrice = stopPrice;
                    }
                    additionalInfo.ogStop = stopPrice;
                    moreInfos.stopPrice = stopPrice;
                }
                parameters.Price = price;
                additionalInfo.ogEntry = price;
                parameters.Comment = JsonSerializer.Serialize(additionalInfo);
                if (o.p1 != 0)
                    moreInfos.moveStopTriggerPrice = o.p1;
                if (o.p2 != 0)
                    moreInfos.moveStopPrice = o.p2;
            }
            else if (orderType == "stop")
            {
                if (order.StopLoss != null)
                {
                    //additionalInfo.ogStop = price;
                    parameters.StopLoss = SlTpHolder.CreateSL(CalcTicks(order.Symbol, order.Side, order.Price, price, "stop"), PriceMeasurement.Absolute, false, order.StopLoss.Quantity, double.NaN, true);
                    parameters.Comment = JsonSerializer.Serialize(additionalInfo);
                }
                else
                    parameters.TriggerPrice = price;
                moreInfos.stopPrice = price;
            }
            else if (orderType == "target")
            {
                if (order.TakeProfit != null)
                    parameters.TakeProfit = SlTpHolder.CreateTP(CalcTicks(order.Symbol, order.Side, order.Price, price, "target"), PriceMeasurement.Absolute, order.TakeProfit.Quantity, double.NaN, true);
                else
                    parameters.Price = price;
                moreInfos.tgPrice = price;
            }
            else if (orderType == "cancel")
            {
                additionalInfo.cancel = price;

                // seems like the order must be recreated to change the comment.  so we're going to offset the tg a bit back and forth to force the order recreation
                double offset = additionalInfo.ctoh ? -order.Symbol.TickSize : order.Symbol.TickSize;
                additionalInfo.ctoh = !additionalInfo.ctoh;
                //parameters.Comment = Newtonsoft.Json.JsonConvert.SerializeObject(additionalInfo);
                parameters.Comment = JsonSerializer.Serialize(additionalInfo);
                parameters.TakeProfit = SlTpHolder.CreateTP(order.TakeProfit.Price + offset, order.TakeProfit.PriceMeasurement, order.TakeProfit.Quantity, order.TakeProfit.QuantityPercentage, order.TakeProfit.Active);

            }
            else if (orderType == "persistentorder")
            {
                parameters = null;
                moreInfos.persistentOrder = price != 0 ? true : false;
            }
            else if (orderType == "movestoptriggerprice")
            {
                parameters = null;
                moreInfos.moveStopTriggerPrice = price;
            }
            else if (orderType == "movestopprice")
            {
                parameters = null;
                moreInfos.moveStopPrice = price;
            }

            UpdateOrder(order, parameters, moreInfos);
        }

        /// <summary>
        /// Update Pre Order
        /// </summary>
        /// <param name="order"></param>
        /// <param name="infoExt"></param>
        public void UpdateOrder(PreOrder order, OrderAdditionalInfoExt infoExt)
        {
            InfoMgr.Update(infoExt);
            PreOrderMgr.Update(order);
        }

        /// <summary>
        /// Update Order
        /// </summary>
        /// <param name="order"></param>
        /// <param name="parameters"></param>
        /// <param name="infoExt"></param>
        public void UpdateOrder(Order order, ModifyOrderRequestParameters parameters, OrderAdditionalInfoExt infoExt)
        {
            InfoMgr.Update(infoExt);
            if (parameters != null)
            {
                Log.Order("", "modifying order", order.Id.ToString(), order.Symbol.Name, "...");
                var res = Core.ModifyOrder(parameters);
                var msg = res.Status + " " + res?.Message ?? "";
                if (res.Message != null && res.Message.Length > 0)
                    Log.OrderError("error", "modifying order", order.Id.ToString(), order.Symbol.Name, msg);
            }
        }

        /// <summary>
        /// Check if provided order ids are working
        /// </summary>
        /// <param name="ids"></param>
        /// <returns></returns>
        public bool AreOrdersWorking(List<string> ids)
        {
            return Core.Orders.Where(o => ids.Contains(o.Id) && o.OriginalStatus == "Working").Any();
        }

        /// <summary>
        /// Close Orders by id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="ignorePersistentOrder"></param>
        /// <returns></returns>
        public bool CloseOrderById(string id, bool ignorePersistentOrder)
        {
            bool found = false;
            try
            {
                if (IsGuid(id)) // preorder
                {
                    var order = PreOrderMgr.Get(id);
                    if (order != null)
                        found = PreOrderMgr.Close(order, ignorePersistentOrder);
                }
                else // live order
                {
                    Order order = Core.GetOrderById(id);
                    if (order != null)
                    {
                        var info = GetAdditionalInfoExt(order);
                        if (IsObjDefault<OrderAdditionalInfoExt>(info) || ignorePersistentOrder || !info.persistentOrder)
                        {
                            Log.Order("", "canceling order", order.Id.ToString(), order.Symbol.Name, "...");
                            if (order.OriginalStatus == "Working")
                            {
                                var res = order?.Cancel();
                                var msg = res.Status + " " + res?.Message ?? "";
                                if (res.Message != null && res.Message.Length > 0)
                                    Log.OrderError("error", "canceling order", order.Id.ToString(), order.Symbol.Name, msg);
                                found = true;
                            }
                        }
                    }

                    // check order state, qty, immediately after placed.  should i close pos here?
                    //order = Core.GetOrderById(id);
                    //Log.Info("huh");
                    // if stop, close pos

                }

                // TODO
                // Positions are updated by stop failsafe instead of here
                // because there might be some delays updating things.
                // Test if i should immediately process here.
                // Is orders immediately updated?  will there be a race condition?


                //if(found)
                //    InfoMgr.Remove(id);
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
            }
            return found;
        }

        /// <summary>
        /// Get AdditionalInfo for order
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public OrderInfoAdditional GetAdditionalInfo(Order order)
        {
            try
            {
                OrderInfoAdditional infos = new OrderInfoAdditional();
                if (order.Comment != null && order.Comment != "")
                {
                    //Newtonsoft.Json.JsonConvert.PopulateObject(order.Comment, infos);
                    infos = JsonSerializer.Deserialize<OrderInfoAdditional>(order.Comment);
                    return infos;
                }
                else
                {
                    //Log.Error("order has no comment: "+ order.Id);
                }
            }
            catch (Exception ex) { Log.Ex(ex); }
            return default;
        }

        /// <summary>
        /// Get AdditionalInfo object from comment
        /// </summary>
        /// <param name="comment"></param>
        /// <returns></returns>
        public OrderInfoAdditional GetAdditionalInfo(string comment)
        {
            try
            {
                OrderInfoAdditional infos = new OrderInfoAdditional();
                if (comment != "")
                {
                    //Newtonsoft.Json.JsonConvert.PopulateObject(comment, infos);
                    infos = JsonSerializer.Deserialize<OrderInfoAdditional>(comment);
                    return infos;
                }
            }
            catch (Exception ex) { Log.Ex(ex); }
            return default;
        }

        /// <summary>
        /// Get AdditionalInfo object from order
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public OrderAdditionalInfoExt GetAdditionalInfoExt(Order order)
        {
            try
            {
                var info = GetAdditionalInfo(order);
                if (!IsObjDefault<OrderInfoAdditional>(info))
                    return InfoMgr.Get(info.ogId);
            }
            catch (Exception ex) { Log.Ex(ex); }
            return default;
        }

        /// <summary>
        /// Get AdditionalInfoExt by ogId
        /// </summary>
        /// <param name="ogId"></param>
        /// <returns></returns>
        public OrderAdditionalInfoExt GetAdditionalInfoExt(string ogId)
        {
            return InfoMgr.Get(ogId);
        }

        /// <summary>
        /// Close orders
        /// </summary>
        /// <param name="qtSymbolName"></param>
        /// <param name="onlyCloseEntries"></param>
        /// <param name="closeLiveOrders"></param>
        /// <param name="retries"></param>
        /// <param name="ignorePersistentOrders"></param>
        /// <returns></returns>
        public async Task<bool> CloseOrders(string qtSymbolName, bool onlyCloseEntries, bool closeLiveOrders, int retries, bool ignorePersistentOrders = false)
        {
            bool found = false;
            if (closeLiveOrders)
            {
                for (int i = 0; i <= retries; i++) // may want to retry to make sure flatten occurs, especially if not watching trade
                {
                    foreach (Order order in Core.Orders) // iterate through all orders
                    {
                        try
                        {
                            if (order.OriginalStatus == "Working")
                            {
                                if (!onlyCloseEntries || onlyCloseEntries && IsEntryOrder(order))
                                {
                                    if (order.Symbol.Root == qtSymbolName)
                                    {
                                        found = CloseOrderById(order.Id, ignorePersistentOrders);
                                        //await Task.Delay(10);
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { Log.Ex(ex); }
                    }
                    if (retries > 0)
                        await Task.Delay(500);
                }
            }
            else
                found = PreOrderMgr.Close(qtSymbolName, ignorePersistentOrders);
            return found;
        }

        /// <summary>
        /// Close all positions
        /// </summary>
        /// <param name="ignorePersistentOrders"></param>
        /// <returns></returns>
        public async Task<bool> CloseAllPositions(bool ignorePersistentOrders) // we really want to make sure close positions not accidentally closed due to invalid symbol name
        {
            // not really sure how to handle ignorePersistentOrders, maybe just not flatten if symbol has any ignorepersistentorders for a live trade
            return await ClosePositions("", true, 0);
        }

        /// <summary>
        /// Close positions
        /// </summary>
        /// <param name="targetSymbol"></param>
        /// <returns></returns>
        public async Task<bool> ClosePositions(string targetSymbol)
        {
            return await ClosePositions(targetSymbol, false, 0);
        }

        /// <summary>
        /// Check StopFailSafes for all symbols with positions or orders.  This should be run on an interval
        /// </summary>
        public void CheckStopFailsafe()
        {
            if (!_initialized) // !this.enableTrading
                return;
            List<Symbol> symbols = new List<Symbol>();

            // get all symbols with positions
            foreach (var position in Core.Positions)
                symbols.Add(position.Symbol);

            // get all symbols with orders
            foreach (var order in Core.Orders)
                symbols.Add(order.Symbol);

            // filter 
            symbols = symbols.Distinct().ToList();

            // check failsafe for symbols
            foreach (var symbol in symbols)
                CheckFailsafeForSymbol(symbol);
        }

        /// <summary>
        /// Create OrderInfoAdditional object
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public OrderInfoAdditional SetOrderInfoAdditional(BridgeOrder order)
        {
            return new OrderInfoAdditional
            {
                ogStop = order.stop,
                ogEntry = order.entry,
                ogTarget = order.target,
                ogId = order.ogId,
                cancel = order.cancelPrice
            };
        }

        /// <summary>
        /// Create OrderAdditionalInfoExt object
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public OrderAdditionalInfoExt SetOrderAdditionalInfoExt(BridgeOrder order)
        {
            double moveStopTriggerPrice = order.moveStopTriggerPrice == order.moveStopPrice ? 0 : order.moveStopTriggerPrice;
            double moveStopPrice = order.moveStopTriggerPrice == order.moveStopPrice ? 0 : order.moveStopPrice;

            return new OrderAdditionalInfoExt
            {
                ogId = order.ogId,
                persistentOrder = order.persistentOrder,
                moveStopTriggerPrice = moveStopTriggerPrice,
                moveStopPrice = moveStopPrice,
                stopPrice = order.stop,
                tgPrice = order.target
            };
        }

        /// <summary>
        /// Create PlaceOrderRequestParameters object
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public PlaceOrderRequestParameters SetPlaceOrderRequestParameters(BridgeOrder order, Symbol symbol, Side side, Account account, string orderTypeId, string comment)
        {
            double stoplossTicks = CalcTicks(symbol, side, order.entry, order.stop, "stop");
            double targetTicks = CalcTicks(symbol, side, order.entry, order.target, "target");

            return new PlaceOrderRequestParameters
            {
                Account = account,//Core.Instance.Accounts[0],
                Side = side,
                StopLoss = SlTpHolder.CreateSL(stoplossTicks, PriceMeasurement.Absolute),
                TakeProfit = SlTpHolder.CreateTP(targetTicks, PriceMeasurement.Absolute),
                Price = order.entry,
                Quantity = order.qty,
                TimeInForce = TimeInForce.GTC,
                OrderTypeId = orderTypeId,
                Symbol = symbol,
                Comment = comment,
            };
        }

        /// <summary>
        /// Create PlaceOrderRequestParameters object
        /// </summary> Convert bridge order into live order
        /// <param name="order"></param>
        /// <returns></returns>
        /// 
        // TODO its not right to pass broker here, very odd
        public void ExecuteBridgeOrder(BridgeOrder order, int count, Broker broker, BridgeOrderResponse response)
        {
            try
            {
                response.ogId = order.ogId;
                response.symbol = order.symbol;
                string orderSymbol = response.symbol;
                Side side = order.orderType == 0 || order.orderType == 2 || order.orderType == 4 ? Side.Buy : Side.Sell;
                GetSymbolRequestParameters symParams = new GetSymbolRequestParameters();

                Symbol sym = broker.GetSymbol(order.symbol); // TODO if futures, use order.symbol.  If not, use orderStymbol.  Not sure if this is needed.
                broker.ConvertBridgeOrderToTick(order, sym);

                var infos = SetOrderInfoAdditional(order);
                var infoExt = SetOrderAdditionalInfoExt(order);
                string comment = JsonSerializer.Serialize(infos);
                string orderTypeId = GetOrderTypeFromOrder(order, side, sym);//OrderType.Limit;

                // check if market order
                if (!string.IsNullOrEmpty(orderTypeId))
                {
                    var request = SetPlaceOrderRequestParameters(order, sym, side, Core.Instance.Accounts[0], orderTypeId, comment);

                    //_log.Trade($"placing order: entry, stop, target: {order.entry}, {order.stop}, {order.target}. {stoplossTicks}/{targetTicks}");
                    Log.Trade($"placing order: entry, stop, target: {order.entry}, {order.stop}, {order.target}.");
                    string id = Guid.NewGuid().ToString();
                    if (order.preOrder == 0)
                    {
                        var resId = CreateOrder(id, request, order.preOrder, infoExt);
                        if (resId != null)
                            response.orderIds[count] = resId;
                    }
                    else
                    {
                        response.orderIds[count] = id; // create dummy ids
                        CreateOrder(id, request, order.preOrder, infoExt);
                    }

                    response.targets[count] = order.target;
                }
            }
            catch (Exception ex) { Log.Ex(ex, "PlaceOrder"); }
        }

        /// <summary>
        /// Create missing tg orders
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public bool CreateMissingTpOrder(Order order)
        {
            if (order.OrderTypeId != OrderType.Stop)
                return false;

            try
            {
                var tpOrder = Core.Orders.FirstOrDefault(o => o.GroupId == order.GroupId && o.OrderTypeId == OrderType.Limit && o.StopLoss == null);
                if (tpOrder != null) // already as associated to porder
                    return false;
                //CreateOrder(string id, PlaceOrderRequestParameters parameters, bool isLiveOrder, string preMsg, OrderAdditionalInfoExt infoExt)
                var info = GetAdditionalInfo(order);
                var infoExt = GetAdditionalInfoExt(order);
                if (IsObjDefault<OrderInfoAdditional>(info) || IsObjDefault<OrderAdditionalInfoExt>(infoExt))
                    return false;

                var sym = order.Symbol;
                var price = info.ogTarget;
                OrderInfoAdditional additionalInfo = new OrderInfoAdditional();
                additionalInfo = JsonSerializer.Deserialize<OrderInfoAdditional>(order.Comment);
                additionalInfo.groupId = order.GroupId;
                additionalInfo.isAddedOrder = true;
                //string comment = "";//Newtonsoft.Json.JsonConvert.SerializeObject(additionalInfo);
                string comment = JsonSerializer.Serialize(additionalInfo);

                var request = new PlaceOrderRequestParameters
                {
                    Account = Core.Instance.Accounts[0],
                    Side = order.Side,
                    Price = price,
                    Quantity = order.RemainingQuantity,
                    TimeInForce = TimeInForce.GTC,
                    OrderTypeId = OrderType.Limit,
                    Symbol = sym,
                    GroupId = order.GroupId,
                    Comment = comment,
                };

                string id = Guid.NewGuid().ToString();
                string orderId = CreateOrder(id, request, 0, "re-create tp order", infoExt);
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
                return false;
            }
            return false;
        }

        public async Task<bool> FlattenOrdersAndPositions(string targetSymbol) // nuke everytihng
        {
            string qtSymbolName = Broker.QtSymbolName(targetSymbol);
            var symbol = Broker.GetSymbol(qtSymbolName);
            Log.Trade("attempting to flatten: " + targetSymbol);
            await CloseOrders(qtSymbolName, false, false, 0, true); // close pre-orders
            await CloseOrders(qtSymbolName, false, true, 1, true); // close live orders
            await Task.Delay(100); // wait a bit before closing positions, to avoid wash sale
            await ClosePositions(targetSymbol, false, 1); // we retry a few times to really make sure the flatten occurs
            return true;
        }

        async public Task<bool> ClosePositionQty(Position position, double targetQty, int processWaitCount, int waitDelay)
        {
            double diff = position.Quantity - targetQty;
            Side side = position.Side == Side.Sell ? Side.Buy : Side.Sell;
            Symbol symbol = position.Symbol;
            PlaceOrderRequestParameters parameters = new PlaceOrderRequestParameters
            {
                Account = Core.Accounts.First(),
                Side = side,
                Quantity = diff,
                TimeInForce = TimeInForce.Day,
                OrderTypeId = OrderType.Market,
                Symbol = symbol
                // Expieratino time
            };

            Log.Info($">>> placing market order.  {symbol.Name}.  qty: {diff}");
            return await PlaceMarketOrder(parameters, position, processWaitCount * 2, waitDelay);
        }

        /// <summary>
        /// Check if stop order has matching target orders
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        protected bool StopOrderHasMatchingTgOrder(Order order)
        {
            if (order.OrderTypeId == OrderType.Stop)
            {
                try
                {
                    var tpOrders = Core.Orders.FirstOrDefault(o => o.GroupId == order.GroupId && o.OrderTypeId == OrderType.Limit && o.StopLoss == null);
                    return tpOrders == null;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
            return true;
        }

        // Private Methods ---------------------------------------------------------------------------------------

        /// <summary>
        /// Event handler sent by Qt after an order has been added
        /// Does some checks to orders as needed
        /// </summary>
        /// <param name="order"></param>
        private async void OrderAddedEvent(Order order)
        {
            try
            {
                // if order is Working (not canceled) and not a market order.  system doesn't support market orders for now.  but market order might be created by qt itself, so we dont process
                if (order.OriginalStatus == "Working" && order.OrderTypeId != OrderType.Market)
                {
                    await InitStopPriceHack(order);
                    if (_autoManageTargetOrders)
                    {
                        //await Task.Delay(10000); // for testing to cancel limit order
                        await Task.Delay(700); // wait and let order process
                        if (IsValidOrder(order) && IsStopOrder(order))
                            ValidateAndProcessTargetOrders(null, order); // check target orders
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
            }
        }

        /// <summary>
        /// Event handler sent by Qt after order is removed
        /// Does some checks to orders as necessary
        /// </summary>
        /// <param name="order"></param>
        private void OrderRemovedEvent(Order order)
        {
            try
            {
                if (_autoManageTargetOrders)
                    HandleTargetOrdersOnClosedOrder(order);
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
            }
        }

        /// <summary>
        /// Check if order is valid
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private bool IsValidOrder(Order order)
        {
            var foundOrder = Core.Orders.FirstOrDefault(o => o.Id == order.Id);
            if (foundOrder is null || foundOrder.OriginalStatus != "Working")
                return false;
            return true;
        }

        /// <summary>
        /// Check if order is an entry order
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private bool IsEntryOrder(Order order)
        {   // TODO there must be a more accurate way to do this.  check for a type
            if (order.StopLoss != null && order.TakeProfitItems != null)
                return true;
            return false;
        }

        /// <summary>
        /// Check if order is a stop order
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private bool IsStopOrder(Order order)
        {
            return !IsEntryOrder(order) && !order.TriggerPrice.IsNanOrDefault() && order.OrderTypeId == OrderType.Stop;
        }

        /// <summary>
        /// Check if order is a target order
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private bool IsTargetOrder(Order order)
        {
            return !IsEntryOrder(order) && order.TriggerPrice.IsNanOrDefault() && order.OrderTypeId == OrderType.Limit;
        }

        /// <summary>
        /// Check if order is an added target order
        /// </summary>
        /// <param name="order"></param>
        /// <param name="isAddedOrder"></param>
        /// <returns></returns>
        private bool IsAddedTargetOrder(Order order, bool isAddedOrder)
        {
            return !IsEntryOrder(order) && order.TriggerPrice.IsNanOrDefault() && order.OrderTypeId == OrderType.Limit && GetAdditionalInfo(order).isAddedOrder == isAddedOrder;
        }

        /// <summary>
        /// Check if is Active Trading Hourse, as defined by parameters
        /// </summary>
        /// <param name="beginHour"></param>
        /// <param name="beginMin"></param>
        /// <param name="endHour"></param>
        /// <param name="endMin"></param>
        /// <returns></returns>
        private bool IsActiveTradingTime(int beginHour, int beginMin, int endHour, int endMin)
        {
            DateTime est = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EstTz);
            int hour = est.Hour;
            int min = est.Minute;
            int currMins = hour * 60 + min;
            int beginMins = beginHour * 60 + beginMin;
            int endMins = endHour * 60 + endMin;
            return !(currMins >= beginMins && currMins <= endMins); // within inactive trading time
        }

        /// <summary>
        /// Sets flag to Enable trading, based on a few parameters
        /// </summary>
        private void SetEnableTrading()
        {
            bool enableTradingStart = _enableTrading;
            bool enableNewTradingStart = _enableNewTrading;

            if (_closeAllOrdersEOD) // TODO: need to make sure new trading time is less than enable trading time.  otherwise inactive
            {
                _enableTrading = IsActiveTradingTime(_inactiveTradingBeginHour, _inactiveTradingBeginMin, _inactiveTradingEndHour, _inactiveTradingEndMin);
                _enableNewTrading = IsActiveTradingTime(_inactiveNewTradingBeginHour, _inactiveNewTradingBeginMin, _inactiveTradingEndHour, _inactiveTradingEndMin);
            }
            else // trade mgmt not enabled, allow all trading at any time
            {
                _enableNewTrading = true;
                _enableTrading = true;
            }

            // log on change
            if (enableTradingStart != _enableTrading)
                Log.Trade("New trades is enabled: " + _enableTrading);
            if (enableNewTradingStart != _enableNewTrading)
                Log.Trade("Any trades is enabled: " + _enableNewTrading);
        }

        /// <summary>
        /// Process all Pre Orders
        /// </summary>
        /// <param name="activateLiveTrades"></param>
        /// <param name="removeStopped"></param>
        /// <returns></returns>
        private async Task<bool> ProcessPreOrders(bool activateLiveTrades, bool removeStopped)
        {
            try
            {
                var orders = PreOrderMgr.Orders;
                for (int i = orders.Count() - 1; i >= 0; i--)
                    await ProcessPreOrder(orders[i], activateLiveTrades, removeStopped);
            }
            catch (Exception ex) { Log.Ex(ex); }
            return true;
        }

        /// <summary>
        /// Process PreOrder
        /// </summary>
        /// <param name="order"></param>
        /// <param name="activateLiveTrades"></param>
        /// <param name="removeStopped"></param>
        /// <returns></returns>
        private async Task<bool> ProcessPreOrder(PreOrder order, bool activateLiveTrades, bool removeStopped)
        {
            try
            {
                ProcessOrderResult res = PreOrderMgr.ProcessOrder(order);
                bool stopped = res == ProcessOrderResult.Stopped || res == ProcessOrderResult.StopAndTriggered;
                bool triggered = res == ProcessOrderResult.Triggered || res == ProcessOrderResult.StopAndTriggered;

                if (stopped && removeStopped)
                {
                    Log.Order("pre-order", "stopped", order.id, order.parameters.Symbol.Name, "");
                    PreOrderMgr.Close(order, true);
                }
                else if (triggered && activateLiveTrades)
                {
                    ConvertPreOrderToLiveTrade(order);
                    await Task.Delay(50);
                }
            }
            catch (Exception ex) { Log.Ex(ex); }
            return true;
        }

        /// <summary>
        /// Convert PreOrder to live Qt trade
        /// </summary>
        /// <param name="order"></param>
        private void ConvertPreOrderToLiveTrade(PreOrder order)
        {
            OrderAdditionalInfoExt infoExt = default;
            OrderInfoAdditional info = GetAdditionalInfo(order.parameters.Comment);
            if (!IsObjDefault<OrderInfoAdditional>(info))
                infoExt = GetAdditionalInfoExt(info.ogId);
            PreOrderMgr.Close(order, true); // close before creating order, to avoid duplicates.  Possibility of missing orders
            CreateOrder(order.id, order.parameters, 0, "pre-order triggered, converting to live order", infoExt);
        }

        /// <summary>
        /// Check MoveStopTriggerPrice to see if it's been triggered
        /// </summary>
        /// <returns></returns>
        private bool CheckMoveStopTriggerPrice()
        {
            var triggeredOrders = new List<OrderAdditionalInfoExt>();
            foreach (var order in Core.Orders) // iteralte through all stop orders
            {
                try
                {
                    if (order.OrderTypeId == OrderType.Stop)
                    {
                        OrderInfoAdditional additionalInfo = GetAdditionalInfo(order);
                        var info = GetAdditionalInfoExt(order);
                        if (!IsObjDefault<OrderAdditionalInfoExt>(info)) // if found, check trigger rpcie
                        {
                            // check if triggered
                            bool triggered = false;
                            double bid = order.Symbol.Bid;
                            double ask = order.Symbol.Ask;
                            double trigger = info.moveStopTriggerPrice;
                            double move = info.moveStopPrice;
                            bool pricesAreValid = bid != 0 && ask != 0 & trigger != 0 && move != 0;

                            if (pricesAreValid)
                            {
                                triggered = IsPriceTriggered(bid, ask, trigger, order.Side);
                            }
                            if (triggered) // triggered, move stop
                            {
                                TriggeredMoveStop(order, move, info);
                                triggeredOrders.Add(info);
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Ex(ex); }
            }

            
            ResetTriggeredOrders(triggeredOrders);
            return triggeredOrders.Count > 0;
        }

        /// <summary>
        /// reset all triggered orders.  we do this here so that it iterates through all orders first, since some orders are connected
        /// </summary>
        /// <param name="triggeredOrders"></param>
        private void ResetTriggeredOrders(List<OrderAdditionalInfoExt> triggeredOrders)
        {
            bool updated = false;
            for (int i = 0; i < triggeredOrders.Count; i++)
            {
                var order = triggeredOrders[i];
                order.moveStopPrice = 0;
                order.moveStopTriggerPrice = 0;
                updated = true;
            }

            if(updated)
                InfoMgr.Save();
        }

        /// <summary>
        /// Check if moveStop price has been triggered
        /// </summary>
        /// <param name="order"></param>
        /// <param name="move"></param>
        /// <param name="info"></param>
        private void TriggeredMoveStop(Order order, double move, OrderAdditionalInfoExt info)
        {
            ModifyOrderRequestParameters parameters = new ModifyOrderRequestParameters(order);
            double price = ConvertPriceTicked(order.Symbol.TickSize, move);
            parameters.TriggerPrice = price;
            info.stopPrice = price;
            var res = Core.ModifyOrder(parameters);
            Log.Order("Check move stop triggered", "moving stop", order.Id.ToString(), order.Symbol.Name, "");
            var msg = res.Status + " " + res?.Message ?? "";
            if (res.Message != null && res.Message.Length > 0)
                Log.OrderError("error", "moving stop", order.Id.ToString(), order.Symbol.Name, msg);
        }

        /// <summary>
        /// Check if price has reached the trigger price
        /// </summary>
        /// <param name="bid"></param>
        /// <param name="ask"></param>
        /// <param name="trigger"></param>
        /// <param name="side"></param>
        /// <returns></returns>
        private bool IsPriceTriggered(double bid, double ask, double trigger, Side side)
        {
            double currentPrice = Math.Abs(bid - ask) + bid;
            bool isShort = side == Side.Buy;
            if (isShort && currentPrice <= trigger || !isShort && currentPrice >= trigger)
                return true;
            return false;
        }

        /// <summary>
        /// Check if cancel price has been reached
        /// </summary>
        private void CheckCancelPrice()
        {
            try
            {
                foreach (var order in Core.Orders)
                {
                    try
                    {
                        double price = order.Symbol.Last;
                        OrderInfoAdditional additionalInfo = GetAdditionalInfo(order);
                        if (!IsObjDefault<OrderInfoAdditional>(additionalInfo) && additionalInfo.cancel != 0 && price > 0 && IsEntryOrder(order))
                        {
                            bool isShort = order.Side == Side.Sell;
                            double cancelPrice = additionalInfo.cancel;

                            if (isShort && price < cancelPrice || !isShort && price > cancelPrice)
                            {
                                Log.Trade("Order Cancel Price Reached: " + order.Symbol.Name + " " + order.Symbol.Root);
                                order.Cancel();

                            }
                        }
                    }
                    catch (Exception ex) { Log.Ex(ex); }
                }

                foreach (var preOrder in PreOrderMgr.Orders)
                {
                    try
                    {
                        var order = preOrder.parameters;
                        string comment = order.Comment;
                        OrderInfoAdditional additionalInfo = new OrderInfoAdditional();
                        if (comment != null && comment != "")
                            additionalInfo = JsonSerializer.Deserialize<OrderInfoAdditional>(comment);

                        double price = order.Symbol.Last;
                        if (additionalInfo.cancel != 0 && price > 0 && order.OrderTypeId == OrderType.Limit)
                        {
                            bool isShort = order.Side == Side.Sell;
                            double cancelPrice = additionalInfo.cancel;

                            if (isShort && price < cancelPrice || !isShort && price > cancelPrice)
                            {
                                Log.Trade("Pre Order Cancel Price Reached: " + order.Symbol.Name + " " + order.Symbol.Root);
                                PreOrderMgr.Orders.Remove(preOrder);
                                PreOrderMgr.Save();
                            }
                        }
                    }
                    catch (Exception ex) { Log.Ex(ex); }
                }
            }
            catch (Exception ex) { Log.Ex(ex); }
        }

        /// <summary>
        /// CheckFailsafe for symbol
        /// </summary>
        /// <param name="symbol"></param>
        private void CheckFailsafeForSymbol(Symbol symbol)
        {
            var failsafe = _checkStopFailsafeSymbols.FirstOrDefault(o => o.Symbol == symbol);
            if (failsafe == null)
            {
                failsafe = new CheckStopFailsafeSymbol(symbol, this);
                _checkStopFailsafeSymbols.Add(failsafe);
            }
            failsafe.RunCheck();
        }

        /// <summary>
        /// Get order type from BridgeOrder
        /// </summary>
        /// <param name="order"></param>
        /// <param name="side"></param>
        /// <param name="symbol"></param>
        /// <returns></returns>
        private string GetOrderTypeFromOrder(BridgeOrder order, Side side, Symbol symbol)
        {
            string orderTypeId = "";
            var oType = order.orderType;
            bool isMarketType = oType == 0 || oType == 1;
            bool isLimitType = oType == 2 || oType == 3;
            bool isStopLimitType = oType == 4 || oType == 5;

            if(isMarketType || order.entry == 0)
            {
                orderTypeId = OrderType.Market;
            }
            else if(isLimitType && side == Side.Buy)
            {
                orderTypeId = OrderType.Limit;
                if (symbol.Ask <= order.stop) // abort
                    Log.Trade("limit order already reached stop price");
                else if (symbol.Ask <= order.entry)
                {
                    //orderTypeId = OrderType.Market;
                    //Log.Trade("limit order switch to market");
                    orderTypeId = OrderType.Limit;
                }
                else
                    orderTypeId = OrderType.Limit;
            }
            else if(isLimitType && side == Side.Sell)
            {
                orderTypeId = OrderType.Limit;
                if (symbol.Bid >= order.stop) // abort
                    Log.Trade("limit order already reached stop price");
                else if (symbol.Bid >= order.entry)
                {
                    //orderTypeId = OrderType.Market;
                    //Log.Trade("limit order switch to market");
                    orderTypeId = OrderType.Limit;
                }
                else
                    orderTypeId = OrderType.Limit;
            }
            else if (isStopLimitType)
            {
                orderTypeId = OrderType.StopLimit;

            }

            return orderTypeId;
        }

       

        // Sometimes stop trigger is erroneously set
        /// <summary>
        /// Initialize stop price hack
        /// Sometimes we need to make changes to the comments of an Order, but the order is not modified by Qt unless price has changed.  So make a slight adjustment to price to force this change.
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private async Task<bool> InitStopPriceHack(Order order)
        {
            bool applied = false;
            try
            {
                //foreach (var order in Core.Orders)
                //{
                //if (order.OrderTypeId == OrderType.Stop && order.TriggerPrice != 0 && order.TriggerPrice < order.Symbol.Bid * .3 && _stopTriggerPriceHackApplied.Find(o => o == order) == null)

                // only apply to stop orders
                if (IsStopOrder(order)) //&& _stopTriggerPriceHackApplied.Find(o => o == order) == null)
                {
                    var additionalInfo = GetAdditionalInfo(order);
                    var additionalInfoExt = GetAdditionalInfoExt(order);
                    if (!IsObjDefault<OrderInfoAdditional>(additionalInfo) && !IsObjDefault<OrderAdditionalInfoExt>(additionalInfoExt) && !additionalInfoExt.stph) //&& additionalInfo.ogStop != order.TriggerPrice)
                    {
                        //additionalInfoExt.stph = true;
                        //InfoMgr.Update(additionalInfoExt);
                        bool isShort = additionalInfo.ogStop > additionalInfo.ogEntry;
                        double stopPrice = additionalInfoExt.stopPrice;
                        //parameters.Comment = Newtonsoft.Json.JsonConvert.SerializeObject(additionalInfo);
                        for (int i = 0; i < 15; i++)
                        {
                            double allowedOffset = order.Symbol.TickSize * _stopTriggerPriceTicksOffset;
                            double allowedEntryOffset = order.Symbol.TickSize * 1;
                            if (stopPrice > 0 && (order.TriggerPrice > stopPrice || order.TriggerPrice < stopPrice - allowedOffset))
                            {
                                await Task.Delay(800);
                                //await Task.Delay(500); // delay a bit because order might not be recognizd in the system yet
                                if (Core.Orders.FirstOrDefault(o => o.Id == order.Id) != null) // make sure order still exists
                                {
                                    if (!isShort && stopPrice > 0 && (order.TriggerPrice > stopPrice + allowedEntryOffset || order.TriggerPrice < stopPrice - allowedOffset)
                                    || isShort && stopPrice > 0 && (order.TriggerPrice < stopPrice - allowedEntryOffset || order.TriggerPrice > stopPrice + allowedOffset))
                                    {
                                        ModifyOrderRequestParameters parameters = new ModifyOrderRequestParameters(order);
                                        parameters.TriggerPrice = stopPrice;
                                        Log.Trade($"StopTriggerPriceHack triggered..., {order.TriggerPrice}, ${stopPrice}, {order.Symbol.Name}");
                                        UpdateOrder(order, parameters, default);
                                        applied = true;
                                    }
                                }
                                additionalInfoExt.stph = true;
                                break;
                            }
                            await Task.Delay(100);
                        }
                        //_stopTriggerPriceHackApplied.Add(order);
                    }
                }
                //}
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
            }
            return applied;
        }

        /// <summary>
        /// Remove associated target orders by groupId
        /// </summary>
        /// <param name="groupId"></param>
        /// <param name="excludeId"></param>
        private void RemoveAssociatedTargetOrders(string groupId, string excludeId = "")
        {
            var addedTargetOrders = Core.Orders.Where(o => GetAdditionalInfo(o).isAddedOrder && groupId == GetAdditionalInfo(o).groupId && o.Id != excludeId);
            foreach (var order in addedTargetOrders)
            {
                Log.Info("RemoveAssociatedTargetOrders() Closing associated added target order: " + order.Id);
                CloseOrderById(order.Id, true);
            }
        }

        // for now, have this executed by the user
        /// <summary>
        /// Validate and process target orders
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="targetStopOrder"></param>
        private void ValidateAndProcessTargetOrders(Symbol symbol, Order targetStopOrder)
        {
            // look for hanging, added target orders with no stop.  close out
            var orders = symbol == null ? Core.Orders.ToList() : Core.Orders.Where(o => o.Symbol == symbol && IsValidOrder(o)).ToList();
            // look for stop orders without a target order

            var stopOrders = targetStopOrder != null ? new List<Order> { targetStopOrder } : orders.Where(o => IsStopOrder(o) && IsValidOrder(o)).ToList();
            foreach (var stopOrder in stopOrders)
            {
                // if there is a normal target order found, ok!
                if (orders.FirstOrDefault(o => o.GroupId == stopOrder.GroupId && IsTargetOrder(o) && !GetAdditionalInfo(o).isAddedOrder) != null)
                    RemoveAssociatedTargetOrders(stopOrder.GroupId);
                else
                {
                    var addedTargetOrders = orders.Where(o => stopOrder.GroupId == GetAdditionalInfo(o).groupId && GetAdditionalInfo(o).isAddedOrder).ToList();
                    int count = addedTargetOrders.Count();
                    if (count == 1) { }// single added target found, ok!
                    else if (count == 0) // no target orders, add one.  Add groupId and isAddedOrder to additional info
                        CreateMissingTpOrder(stopOrder);
                    else if (count > 1) // there are dupes, remove all except one
                        RemoveAssociatedTargetOrders(stopOrder.GroupId, addedTargetOrders.ElementAt(0).Id);
                }
            }
        }

        /// <summary>
        /// Handle targetorder on closed orders
        /// </summary>
        /// <param name="order"></param>
        private void HandleTargetOrdersOnClosedOrder(Order order)
        {
            // if stop closed, close all added target orders
            if (IsAddedTargetOrder(order, true))
            {
                var associatedStopOrders = Core.Orders.Where(o => IsValidOrder(o) && IsStopOrder(o) && o.GroupId == GetAdditionalInfo(order).groupId).ToList();
                if (associatedStopOrders.Count() > 0)
                {
                    Log.Trade("HandleTargetOrdersOnClosedOrder() added tg order triggered, remove associated stop order");
                    foreach (var stopOrder in associatedStopOrders)
                        CloseOrderById(stopOrder.Id, false);
                }
            }
            else if (IsStopOrder(order))
            {
                var addedTargetOrders = Core.Orders.Where(o => IsValidOrder(o) && order.GroupId == GetAdditionalInfo(o).groupId && GetAdditionalInfo(o).isAddedOrder).ToList();
                if (addedTargetOrders.Count() > 0)
                {
                    Log.Trade("HandleTargetOrdersOnClosedOrder() stop order triggered, remove associated added target orders");
                    RemoveAssociatedTargetOrders(order.GroupId);
                }
            }

            // if added target order closed, close all related stop orders
        }

        /// <summary>
        /// Close positions
        /// </summary>
        /// <param name="targetSymbol"></param>
        /// <param name="closeAll"></param>
        /// <param name="retries"></param>
        /// <returns></returns>
        private async Task<bool> ClosePositions(string targetSymbol, bool closeAll, int retries)
        {
            bool found = false;
            if (targetSymbol.Length == 0)
                Log.Error("Attempting to close position with invaild symbol");
            else
            {
                for (int i = 0; i <= retries; i++) // may want to retry to make sure flatten occurs, especially if not watching trade
                {
                    foreach (var pos in Core.Positions)
                    {
                        try
                        {
                            if (closeAll || Broker.GetSymbolName(pos.Symbol) == targetSymbol)
                            {
                                if (i == 0)
                                    Log.Order("", "closing pos", pos.Id, targetSymbol, $"qty: {pos.Quantity}");
                                if (pos.Quantity != 0)
                                {
                                    found = true;
                                    var res = pos.Close();
                                    var msg = res?.Message ?? "no response. success?";
                                    if (res.Message != null && res.Message.Length > 0)
                                        Log.OrderError("error", "closing pos", pos.Id, targetSymbol, msg);
                                }
                            }
                        }
                        catch (Exception ex) { Log.Ex(ex); }
                    }
                    if (retries > 0)
                        await Task.Delay(300);
                }
            }

            return found;
        }


        async private Task<bool> PlaceMarketOrder(PlaceOrderRequestParameters parameters, Position position, int processWaitCount, int waitDelay)
        {
            try
            {
                double startQty = position.Quantity;
                var res = Core.PlaceOrder(parameters);
                if (res.Status == TradingOperationResultStatus.Success)
                {
                    for (int i = 0; i <= processWaitCount; i++)
                    {
                        if (position.Quantity == startQty)
                            await Task.Delay(waitDelay);
                        else
                            return true;
                    }
                    Log.Info($">>> Closing position failed.  Attempt to cancel market order.  Womp womp. {position.Symbol.Name}");
                    Core.CancelOrder(Core.GetOrderById(res.OrderId));
                }
                else
                    Log.Info($">>> Closing position failed with Failure response. {res.Message} {position.Symbol.Name}");
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
            }

            return false;
        }

        /*
        private async Task<bool> InitStopPriceHack(Order order)
        {
            bool applied = false;
            try
            {
                if (IsStopOrder(order)) //&& _stopTriggerPriceHackApplied.Find(o => o == order) == null)
                {
                    var additionalInfo = GetAdditionalInfo(order);
                    var additionalInfoExt = GetAdditionalInfoExt(order);
                    if (additionalInfo != null && additionalInfoExt != null) //&& additionalInfo.ogStop != order.TriggerPrice)
                    {
                        bool isShort = additionalInfo.ogStop > additionalInfo.ogEntry;
                        double stopPrice = additionalInfo.ogStop; //additionalInfoExt.stopPrice;
                        if (!isShort)
                        {
                            for (int i = 0; i < 15; i++)
                            {
                                await Task.Delay(700);
                                if (Core.Orders.FirstOrDefault(o => o.Id == order.Id) != null) // make sure order still exists
                                {
                                    if (stopPrice > 0 && (order.TriggerPrice < stopPrice * .9))
                                    {
                                        ModifyOrderRequestParameters parameters = new ModifyOrderRequestParameters(order);
                                        parameters.TriggerPrice = stopPrice;
                                        Log.Trade($"StopTriggerPriceHack triggered..., {order.TriggerPrice}, ${stopPrice}, {order.Symbol.Name}");
                                        UpdateOrder(order, parameters, null);
                                        applied = true;
                                    }
                                }
                                break;
                            }
                            await Task.Delay(100);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
            }
            return applied;
        }
        */
        /*
        private List<Order> GetMatchingOrdersByGroupId(string groupId, string excludeId = "")
        {
            return Core.Orders.Where(o => o.GroupId == groupId && o.Id != excludeId).ToList();
        }
        private void RemoveHangingTargetOrders(Symbol symbol) // set null to check all
        {
            var orders = symbol == null ? Core.Orders.ToList() : Core.Orders.Where(o => o.Symbol == symbol).ToList();
            foreach (var order in orders)
            {
                if (IsTargetOrder(order))
                {
                    var info = GetAdditionalInfo(order);
                    if (!IsObjDefault<OrderInfoAdditional>(info) && info.isAddedOrder && IsTargetOrder(order))
                    {
                        var matchingOrders = GetMatchingOrdersByGroupId(info.groupId, order.Id);
                        // if no stop order found, this is a hanging order.  Close
                        if (matchingOrders.Find(o => IsStopOrder(o)) != null)
                        {
                            Log.LogInfo($"ValidateAndProcessTargetOrders() hanging target order found w/o stop.  Closing... {order.Id}");
                            if (!CloseOrderById(order.Id, true))
                                Log.LogError($"ValidateAndProcessTargetOrders() close hanging target order failed... {order.Id}");
                        }
                    }
                }
            }
        }*/
    }
}
