using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;
using static QtBridgeStrategy.Utils.Util;
using static QtBridgeStrategy.Constants.Constants;
using QtBridgeStrategy.Models;
using QtBridgeStrategy.Data;
using QtBridgeStrategy.Enums;

namespace QtBridgeStrategy.Services.Brokers
{
    public class PreOrderMgr : QtBridgeStrategy
    {
        private string _filepath;
        private DataManager _dataManager = new DataManager();
        private double _triggerTicks;
        private double _maxSpreadTicks;
        private OrderAdditionalInfoMgr _infoMgr;

        public List<PreOrder> Orders { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="triggerTicks"></param>
        /// <param name="maxSpreadTicks"></param>
        /// <param name="infoMgr"></param>
        public PreOrderMgr(double triggerTicks, double maxSpreadTicks, OrderAdditionalInfoMgr infoMgr)
        {
            Orders = new List<PreOrder>();
            _infoMgr = infoMgr;
            _triggerTicks = triggerTicks;
            _maxSpreadTicks = maxSpreadTicks;
            _filepath = GetPreOrdersFilePath();

            CreateFileIfNotExist(_filepath); // create folder if not exist.  We'll just assume the file exists from here on out.
            Load();
        }

        // Public Methods --------------------------------------------------------

        /// <summary>
        /// Create new pre order
        /// </summary>
        /// <param name="id"></param>
        /// <param name="request"></param>
        /// <param name="enabled"></param>
        public void Add(string id, PlaceOrderRequestParameters request, bool enabled)
        {
            Orders.Add(new PreOrder(id, request, enabled));
            Log.Order("pre-order", "added", id, request.Symbol.Name);
            Save();
        }

        /// <summary>
        /// Get pre order
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public PreOrder Get(string id)
        {
            foreach (var order in Orders)
                if (order.id == id)
                    return order;
            return null;
            //return orders.Find(o => o.id == id);
        }
        
        /// <summary>
        /// Close pre order by qtSymbolName
        /// </summary>
        /// <param name="qtSymbolName"></param>
        /// <param name="ignorePersistentOrder"></param>
        /// <returns></returns>
        public bool Close(string qtSymbolName, bool ignorePersistentOrder)
        {
            bool found = false;
            var res = qtSymbolName == null ? Orders.FindAll(o => o.parameters.Symbol.Root == qtSymbolName) : Orders;
            if (res?.Count > 0)
            {
                found = true;
                try
                {
                    foreach (var order in res)
                        Close(order, ignorePersistentOrder);
                }
                catch (Exception ex)
                {
                    Log.Ex(ex);
                }
                finally
                {
                    Save();
                }
            }
            return found;
        }

        /// <summary>
        /// Close pre order
        /// </summary>
        /// <param name="order"></param>
        /// <param name="ignorePersistentOrder"></param>
        /// <returns></returns>
        public bool Close(PreOrder order, bool ignorePersistentOrder)
        {
            var info = _infoMgr.Get(order.id);
            if (IsObjDefault<OrderAdditionalInfoExt>(info) || ignorePersistentOrder || !info.persistentOrder)
            {
                Orders.Remove(order);
                Log.Order("pre-order", "closed", order.id, order.parameters.Symbol.Name);
                Save();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Update and save preorder
        /// </summary>
        /// <param name="order"></param>
        public void Update(PreOrder order)
        {
            Orders.RemoveAll(s => s.id == order.id);
            Orders.Add(order);
            Log.Order("pre-order", "updated", order.id, order.parameters.Symbol.Name);
            Save();
        }

        /// <summary>
        /// Returns order count
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            return Orders.Count;
        }

        /// <summary>
        /// Save preorders
        /// </summary>
        /// <returns></returns>
        public bool Save()
        {
            List<PlaceOrderRequestParametersJson> objs = new List<PlaceOrderRequestParametersJson>();
            foreach (var order in Orders)
                objs.Add(PreOrderToPlaceOrderRequestParametersJson(order));
            return _dataManager.SaveToFile(objs, _filepath);
        }

        /// <summary>
        /// Process pre order, check if it should be converted or closed.
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public ProcessOrderResult ProcessOrder(PreOrder order)
        {
            try
            {
                Symbol symbol = order.parameters.Symbol;
                string symbolName = Broker.GetSymbolName(symbol);
                double bid = symbol.Bid;
                double ask = symbol.Ask;
                double entryPriceOffset = GetEntryPriceOffset(symbolName, order);
                double stop = GetStopPrice(symbol, order);
                Side side = order.parameters.Side;
                double price = order.parameters.Price;

                if (!ReadyToProcessOrder(bid,ask,order, symbol))
                    return ProcessOrderResult.None;

                return GetProcessOrderResult(side, bid, ask, price, stop, entryPriceOffset);
            }
            catch { }

            return ProcessOrderResult.None;
        }

        // Private Methods -------------------------------------------------------------------------------------

        /// <summary>
        /// Calculates the result of the pre order, whether it has been stopped or triggered
        /// </summary>
        /// <param name="side"></param>
        /// <param name="bid"></param>
        /// <param name="ask"></param>
        /// <param name="price"></param>
        /// <param name="stop"></param>
        /// <param name="entryPriceOffset"></param>
        /// <returns></returns>
        private ProcessOrderResult GetProcessOrderResult(Side side, double bid, double ask, double price, double stop, double entryPriceOffset)
        {
            var res = GetStoppedAndTriggered(side, bid, ask, price, stop, entryPriceOffset);
            if (res.stopped & res.triggered)
                return ProcessOrderResult.StopAndTriggered;
            else if (res.stopped)
                return ProcessOrderResult.Stopped;
            else if (res.triggered)
                return ProcessOrderResult.Triggered;
            return ProcessOrderResult.None;
        }

        /// <summary>
        /// Calculates the stop and trigger prices
        /// </summary>
        /// <param name="side"></param>
        /// <param name="bid"></param>
        /// <param name="ask"></param>
        /// <param name="price"></param>
        /// <param name="stop"></param>
        /// <param name="entryPriceOffset"></param>
        /// <returns></returns>
        private (bool stopped, bool triggered) GetStoppedAndTriggered(Side side, double bid, double ask, double price, double stop, double entryPriceOffset)
        {
            bool stopped = false;
            bool triggered = false;

            if (side == Side.Buy)
            {
                stopped = ask < stop;
                triggered = bid <= price + entryPriceOffset;
            }
            else if (side == Side.Sell)
            {
                stopped = bid > stop;
                triggered = ask >= price - entryPriceOffset;
            }
            return (stopped, triggered);
        }

        /// <summary>
        /// Checks for errors and states to see if order is ready to be processed
        /// </summary>
        /// <param name="bid"></param>
        /// <param name="ask"></param>
        /// <param name="order"></param>
        /// <param name="symbol"></param>
        /// <returns></returns>
        private bool ReadyToProcessOrder(double bid, double ask, PreOrder order, Symbol symbol)
        {
            if (PriceInvalid(bid, ask) ||
                !order.enabled ||
                SpreadTooLarge(bid, ask, symbol))
                return false;
            return true;
        }

        // private methods -----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Get stop price
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="order"></param>
        /// <returns></returns>
        private double GetStopPrice(Symbol symbol, PreOrder order)
        {
            return CalcPrice(symbol, order.parameters.Side, order.parameters.Price, order.parameters.StopLoss.Price, "stop");
        }

        /// <summary>
        /// Get entry price offset
        /// </summary>
        /// <param name="symbolName"></param>
        /// <param name="order"></param>
        /// <returns></returns>
        private double GetEntryPriceOffset(string symbolName, PreOrder order)
        {
            double triggerTicksOffset = GetTriggerTicksOffset(symbolName);
            double targetTriggerTicks = _triggerTicks * triggerTicksOffset;
            double entryPriceOffset = order.parameters.Symbol.TickSize * targetTriggerTicks;
            return entryPriceOffset;
        }

        /// <summary>
        /// Get trigger ticks offset
        /// </summary>
        /// <param name="symbolName"></param>
        /// <returns></returns>
        private double GetTriggerTicksOffset(string symbolName)
        {
            // TODO hack, offset nq futures because higher volitity means it should triger sooner
            string[] triggerVolx = ["-NQ", "-MNQ"];
            double triggerTicksOffset = 1;
            if (triggerVolx.Contains(symbolName))
                triggerTicksOffset = 3;
            return triggerTicksOffset;
        }

        /// <summary>
        /// Check if prices are invalid
        /// </summary>
        /// <param name="bid"></param>
        /// <param name="ask"></param>
        /// <returns></returns>
        private bool PriceInvalid(double bid, double ask)
        {
            return (bid.IsNanOrDefault() || ask.IsNanOrDefault() || bid == 0 || ask == 0);
        }

        /// <summary>
        /// Check if spread is too large to be processed
        /// </summary>
        /// <param name="bid"></param>
        /// <param name="ask"></param>
        /// <param name="symbol"></param>
        /// <returns></returns>
        private bool SpreadTooLarge(double bid, double ask, Symbol symbol)
        {
            double spread = Math.Abs(ask - bid);
            double spreadTicks = spread / symbol.TickSize;
            if (spreadTicks > _maxSpreadTicks) // spread too damn big, dont process.
                return true;
            return false;
        }

        /// <summary>
        /// Convert PreOrder to Qt's Place Order Request object to place a live order.
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private PlaceOrderRequestParametersJson PreOrderToPlaceOrderRequestParametersJson(PreOrder order)
        {
            var p = order.parameters;
            double stopLossTicks = 0;
            double takeProfitTicks = 0;
            if (p.StopLoss != null)
                stopLossTicks = p.StopLoss.Price;
            if (p.TakeProfit != null)
                takeProfitTicks = p.TakeProfit.Price;
            return new PlaceOrderRequestParametersJson(order.id, p.AccountId, p.Side, p.Comment, p.Quantity, p.Price, p.OrderTypeId, p.Symbol.Name, p.Symbol.Root, stopLossTicks, takeProfitTicks, order.enabled);
        }

        /// <summary>
        /// Convert Qt's PlaceOrderRequest object to a PreOrder
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private PreOrder PlaceOrderRequestParametersToPreOrderJson(PlaceOrderRequestParametersJson order)
        {
            Symbol sym = Broker.GetSymbol(order.contract);// Symbols.First(o => o.Name == order.contract);
            Account acct = Core.Instance.Accounts.First(o => o.Id == order.accountId);

            // verify critical objects
            if (sym == null)
                throw new Exception("PlaceOrderRequestParametersToPreOrderJson() symbol not found: " + order.contract);
            if (acct == null)
                throw new Exception("PlaceOrderRequestParametersToPreOrderJson() account not found: " + order.accountId);

            // set parameters
            PlaceOrderRequestParameters parameters = new PlaceOrderRequestParameters
            {
                Account = acct,
                Side = order.side,
                Price = order.price,
                Quantity = order.qty,
                TimeInForce = TimeInForce.GTC,
                OrderTypeId = order.orderTypeId,
                Symbol = sym,
                Comment = order.comment,
            };

            // These properties can't be saved and must be re-created 
            if (order.stopLossTicks != 0)
                parameters.StopLoss = SlTpHolder.CreateSL(order.stopLossTicks, PriceMeasurement.Absolute, false, order.qty, 0, true);
            if (order.takeProfitTicks != 0)
                parameters.TakeProfit = SlTpHolder.CreateTP(order.takeProfitTicks, PriceMeasurement.Absolute, order.qty, 0, true);

            return new PreOrder(order.id, parameters, order.enabled);
        }

        /// <summary>
        /// Load Pre Orders
        /// </summary>
        private void Load()
        {
            try
            {
                // load data from file
                List<PlaceOrderRequestParametersJson> objs = _dataManager.LoadFromFile<List<PlaceOrderRequestParametersJson>>(_filepath);
                Orders.Clear(); // clear orders now (in case load fails before)
                foreach (PlaceOrderRequestParametersJson obj in objs) // Correctly transform data to class
                {
                    try{ Orders.Add(PlaceOrderRequestParametersToPreOrderJson(obj)); } 
                    catch (Exception ex) { Log.Ex(ex); }
                }
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
            }
        }
    }
}
