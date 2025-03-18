using QtBridgeStrategy.Enums;
using QtBridgeStrategy.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;
using System.Text.Json;
using static QtBridgeStrategy.Utils.Util;
using QtBridgeStrategy.Services.Brokers;
using QtBridgeStrategy.Logging;
using System.Security.Principal;

namespace QtBridgeStrategy.Services
{
    /// <summary>
    /// Class that contains the functions to get Data Stream information to be sent back to the client.
    /// </summary>
    public class WebSocketServerClientDataStreamMessages
    {
        private List<string> _symbolMapUnavail = new List<string>();
        private List<SymbolPrice> symbolPrices = new List<SymbolPrice>();
        private Broker _broker;
        private QtBridgeStrategy _qtStrategy;
        private Core _core;
        private Logger _log;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="qtStrategy"></param>
        public WebSocketServerClientDataStreamMessages(QtBridgeStrategy qtStrategy)
        {
            _qtStrategy = qtStrategy;
            _core = qtStrategy.CoreRef;
            _log = qtStrategy.Log;
        }

        /// <summary>
        /// Creates the Cccount information message in the expected format
        /// </summary>
        /// <param name="callId"></param>
        /// <returns></returns>
        public string AccountInfoMsg(string callId = "0")
        {
            try
            {
                Account account = _broker.GetAccount(); //this.account;//account.AdditionalInfo // TODO what if multiple accounts?
                AccountInfo accountInfo = CreateAccountInfo(account);

                string jsonStr = JsonSerializer.Serialize(accountInfo);
                return string.Format("<@|{0}|{1}|{2}|@>", (int)Command.GetAccount, callId, jsonStr);//equity //free margin
            }
            catch (Exception ex) { _log.Ex(ex); }
            return "";
        }

        /// <summary>
        /// Creates the Positions information message in the expected format
        /// </summary>
        /// <param name="callId"></param>
        /// <returns></returns>
        public string PositionsInfoMsg(string callId = "0")
        {
            try
            {
                if (_core?.Positions is not null)
                {
                    List<PositionsInfo> positions = new List<PositionsInfo>();
                    foreach (var position in _core.Positions)
                        positions.Add(CreatePositionsInfo(position));

                    string jsonStr = JsonSerializer.Serialize(positions);
                    return string.Format("<@|{0}|{1}|{2}|ok|@>", (int)Command.GetPositions, callId, jsonStr);
                }
                else
                {
                    _log.Error("PositionInfoMsg() error: Core or Core.Positions is null.  Disconnected?");
                }
            }
            catch (Exception ex) { _log.Ex(ex); }
            return "";
        }

        /// <summary>
        /// Creates the orders and preorder information message in the expected format
        /// </summary>
        /// <param name="callId"></param>
        /// <returns></returns>
        public string OrdersMsg(string callId = "0")
        {
            try
            {
                List<OrderInfo> orders = new List<OrderInfo>();
                orders.AddRange(CreateOrderInfos(_core.Orders));
                orders.AddRange(CreatePreOrderInfos(_broker.OrderMgr.PreOrderMgr.Orders));

                string jsonStr = JsonSerializer.Serialize(orders);
                return string.Format("<@|{0}|{1}|{2}|ok|@>", (int)Command.GetOrders, callId, jsonStr);
            }
            catch (Exception ex) { _log.Ex(ex); }
            return "";
        }

        /// <summary>
        /// Creates the symbol prices information message in the expected format.  It only sends information of the symbol price has changed from the previous message.
        /// </summary>
        /// <param name="forceSend"></param>
        /// <returns></returns>
        public string SubscribedSymbolPricesMsg(bool forceSend = false)
        {
            string response = string.Format("<@|{0}|0", (int)Command.GetSymbolPrice);
            //foreach (var symbol in subscribedSymbols)
            if (_broker.GetSymbols().Count == 0)
                _log.Error("SubscribedSymbolPriceMsg failed, no Symbols available.  Probably a connection problem.");
            else
            {
                string processingSymbol = "";
                try
                {
                    //foreach (var smap in SymbolMap)
                    foreach(var sym in _broker.GetSymbols())
                    {

                        //var smap = symbolMap.First(o => o.Key == symbol);
                        // string symbol = smap.Key;
                        string symbol = _broker.GetSymbolName(sym);
                        processingSymbol = symbol;

                        // dont continue if symbol was invalid
                        if (_symbolMapUnavail.FirstOrDefault(o => o == symbol) != null)
                            continue;
                        /*
                        Symbol sym = Broker.GetSymbols().FirstOrDefault(o => o.Root == smap.Value);
                        if (sym == null)
                        {
                            Log.Error("SubscribedSymbolPricesMsg()' " + "symbol not found.  Symbol not found in watchlist, or Account may not have access to relevant market data package for: " + symbol + ". This error will now be ignored.");
                            //SymbolMap.Remove(symbol);
                            _symbolMapUnavail.Add(symbol);
                            continue;
                        }*/
                        
                        double bid = sym.Bid;
                        double ask = sym.Ask;
                        double last = sym.Last;

                        // check if values are valid
                        if (bid.IsNanOrDefault() || ask.IsNanOrDefault())
                        {
                            if (last.IsNanOrDefault())
                                continue;
                            bid = ask = last;
                        }

                        // get symbolprice to check for previous prices
                        SymbolPrice symPrice = GetSymbolPrice(symbolPrices, symbol);

                        // dont send if bid/ask price is save as previous
                        if (!forceSend && symPrice.bid == bid && symPrice.ask == ask)
                            continue;
                        else // set new prices
                        {
                            symPrice.bid = bid;
                            symPrice.ask = ask;
                        }

                        response += string.Format("|{0}#{1}#{2}", symbol, bid, ask);
                        //response += string.Format("|{0}#{1}#{2}", smap.Key, bid, ask);
                    }
                }
                catch (Exception ex) { _log.Ex(ex, processingSymbol); }
            }
            response += "|@>";
            return response;
        }

        // Private Methods ------------------------------------------------------------------------

        /// <summary>
        /// Gets the SymbolPrice (or creates if not exist) object for the specified symbol name
        /// </summary>
        /// <param name="symbolPrices"></param>
        /// <param name="symbol"></param>
        /// <returns></returns>
        private SymbolPrice GetSymbolPrice(List<SymbolPrice> symbolPrices, string symbol)
        {
            SymbolPrice symPrice = symbolPrices.FirstOrDefault(o => o.symbol == symbol);

            // create if not exist
            if (IsObjDefault<SymbolPrice>(symPrice))
            {
                symPrice = new SymbolPrice
                {
                    symbol = symbol,
                    bid = 0,
                    ask = 0
                };
                symbolPrices.Add(symPrice);
            }

            return symPrice;
        }

        /// <summary>
        /// Creates and populates AccountInfo object
        /// </summary>
        /// <param name="account"></param>
        /// <returns></returns>
        private AccountInfo CreateAccountInfo(Account account)
        {
            AccountInfo info = new AccountInfo();
            info.id = account.Id;

            double totalFilledQty = (double)account.AdditionalInfo.First(o => o.Id == "TotalFilledQty").Value;
            double commissionPerSide = _qtStrategy.AvgCommission; //2.0; // emini average commish
            double estimatedCommission = totalFilledQty * commissionPerSide;

            info.realizedPnL = (double)account.AdditionalInfo.First(o => o.Id == "RealizedPnL").Value - estimatedCommission;
            info.totalPnL = (double)account.AdditionalInfo.First(o => o.Id == "TotalPnL").Value - estimatedCommission;
            info.marginCredit = (double)account.AdditionalInfo.First(o => o.Id == "MarginCredit").Value;
            info.totalMargin = (double)account.AdditionalInfo.First(o => o.Id == "TotalMargin").Value;
            info.positionMargin = (double)account.AdditionalInfo.First(o => o.Id == "PositionMargin").Value;
            info.balance = account.Balance - estimatedCommission;
            /*
            else if (UseAmeritrade)
            {
                double totalFilledQty = 0;// (double)account.AdditionalInfo.First(o => o.Id == "TotalFilledQty").Value;
                double estimatedCommission = 0;
                info.realizedPnL = 0;//(double)account.AdditionalInfo.First(o => o.Id == "RealizedPnL").Value - estimatedCommission;
                info.totalPnL = 0;//(double)account.AdditionalInfo.First(o => o.Id == "TotalPnL").Value - estimatedCommission;
                info.marginCredit = 0;
                info.totalMargin = 0;
                info.positionMargin = 0;
                info.balance = account.Balance - estimatedCommission;
            }*/
            return info;
        }

        /// <summary>
        /// Cretaes and populates the PositionsInfo objects for each symbol
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        private PositionsInfo CreatePositionsInfo(Position position)
        {
            double grossPnL = position.GrossPnL != null ? position.GrossPnL.Value : 0;
            return (new PositionsInfo
            {
                symbol = _broker.GetSymbolName(position.Symbol),
                qty = position.Side == Side.Sell ? -position.Quantity : position.Quantity,
                grossPnL = grossPnL,
                grossPnLTicks = position.GrossPnLTicks
            });
        }

        /// <summary>
        /// Creates and populates the OrdersInfos objects for each order
        /// </summary>
        /// <param name="orders"></param>
        /// <returns></returns>
        private List<OrderInfo> CreateOrderInfos(Order[] orders)
        {
            List < OrderInfo> orderInfos = new List < OrderInfo>();
            foreach (var order in orders)
            {
                OrderInfo orderInfo = CreateOrderInfo(order);
                if (IsObjDefault<OrderInfo>(orderInfo))
                    continue;
                orderInfos.Add(orderInfo);
            }
            return orderInfos;
        }

        /// <summary>
        /// Creates and populates the OrderInfos objects from a list of PreOrders
        /// </summary>
        /// <param name="orders"></param>
        /// <returns></returns>
        private List<OrderInfo> CreatePreOrderInfos(List<PreOrder> orders)
        {
            List<OrderInfo> orderInfos = new List<OrderInfo>();
            foreach (var preOrder in orders)
            {
                OrderInfo orderInfo = CreatePreOrderInfo(preOrder);
                if (IsObjDefault<OrderInfo>(orderInfos))
                    continue;
                orderInfos.Add(orderInfo);
            }
            return orderInfos;
        }

        /// <summary>
        /// Creates and populates the OrderInfo object from an Order
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private OrderInfo CreateOrderInfo(Order order)
        {
            OrderInfoAdditional additionalInfo = _broker.OrderMgr.GetAdditionalInfo(order);
            if (IsObjDefault<OrderInfoAdditional>(additionalInfo))
            {
                //Log.Error("warning, OrdersMsg order additionalInfo is null (order has no comment?)");
                return default;
            }

            var moreInfos = _broker.OrderMgr.GetAdditionalInfoExt(order);

            var info = new OrderInfo
            {
                id = order.Id,
                symbol = _broker.GetSymbolName(order.Symbol),
                status = order.Status.ToString().ToLower(),
                date = order.LastUpdateTime.ToShortDateString(),
                qty = order.TotalQuantity,
                side = order.Side.ToString().ToLower(),
                orderType = order.OrderTypeId.ToString().ToLower(),
                ogStop = additionalInfo.ogStop,
                ogEntry = additionalInfo.ogEntry,
                ogTarget = additionalInfo.ogTarget,
                ogId = additionalInfo.ogId,
                cancel = additionalInfo.cancel,
                persistentOrder = !IsObjDefault<OrderAdditionalInfoExt>(moreInfos) ? moreInfos.persistentOrder : false,
                moveStopTriggerPrice = !IsObjDefault<OrderAdditionalInfoExt>(moreInfos) ? moreInfos.moveStopTriggerPrice : 0,
                moveStopPrice = !IsObjDefault<OrderAdditionalInfoExt>(moreInfos) ? moreInfos.moveStopPrice : 0,
                isPreOrder = 0
            };

            // todo: ameritrade doesnt handle comments, and therefor our entry system
            /*
            if (UseAmeritrade || order.StopLoss != null || order.TakeProfit != null) // initial entry
            {
                info.target = 0;
                info.stop = 0;
                info.entry = order.Price;
                if (order.StopLoss != null)
                    info.stop = CalcPrice(order.Symbol, order.Side, order.Price, order.StopLoss.Price, "stop");
                if (order.TakeProfit != null)
                    info.target = CalcPrice(order.Symbol, order.Side, order.Price, order.TakeProfit.Price, "target");
            }
            else
            {*/
            if (info.orderType == "limit")
                info.target = order.Price;
            else if (info.orderType == "stop")
                info.stop = order.TriggerPrice;

            return info;
        }

        /// <summary>
        /// Creates and populates the OrderInfo object from a PreOrder
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private OrderInfo CreatePreOrderInfo(PreOrder preOrder)
        {
            var order = preOrder.parameters;
            OrderInfoAdditional additionalInfo = _broker.OrderMgr.GetAdditionalInfo(order.Comment);
            if (IsObjDefault<OrderInfoAdditional>(additionalInfo))
            {
                //Log.Error("warning, OrdersMsg preorder additionalInfo is null (order has no comment?)");
                return default;
            }
            var moreInfos = _broker.OrderMgr.GetAdditionalInfoExt(additionalInfo.ogId);

            var info = new OrderInfo
            {
                id = preOrder.id,
                symbol = _broker.GetSymbolName(order.Symbol),
                status = "preorder",//order.Status.ToString().ToLower(),
                date = "",
                qty = order.Quantity,//''.TotalQuantity,
                side = order.Side.ToString().ToLower(),
                orderType = order.OrderTypeId.ToString().ToLower(),
                ogStop = additionalInfo.ogStop,
                ogEntry = additionalInfo.ogEntry,
                ogTarget = additionalInfo.ogTarget,
                ogId = additionalInfo.ogId,
                cancel = additionalInfo.cancel,
                persistentOrder = !IsObjDefault<OrderAdditionalInfoExt>(moreInfos) ? moreInfos.persistentOrder : false,
                moveStopTriggerPrice = !IsObjDefault<OrderAdditionalInfoExt>(moreInfos) ? moreInfos.moveStopTriggerPrice : 0,
                moveStopPrice = !IsObjDefault<OrderAdditionalInfoExt>(moreInfos) ? moreInfos.moveStopPrice : 0,
                isPreOrder = preOrder.enabled ? 1 : -1
            };

            if (order.StopLoss != null || order.TakeProfit != null) // initial entry
            {
                info.entry = order.Price;
                if (order.StopLoss != null)
                    info.stop = CalcPrice(order.Symbol, order.Side, order.Price, order.StopLoss.Price, "stop");
                if (order.TakeProfit != null)
                    info.target = CalcPrice(order.Symbol, order.Side, order.Price, order.TakeProfit.Price, "target");
            }
            else
            {
                if (info.orderType == "limit")
                    info.target = order.Price;
                else if (info.orderType == "stop")
                    info.stop = order.TriggerPrice;
            }
            return info;
        }
    }
}
