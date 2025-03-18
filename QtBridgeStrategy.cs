// Copyright QUANTOWER LLC. © 2017-2021. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Text;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Modules;
using System.Drawing;
using System.Threading.Tasks;
using System.Threading;
using System.Timers;
using System.Linq;
using System.Data.SqlClient;
using System.Net.NetworkInformation;
using System.Security.Principal;
using TradingPlatform.BusinessLayer.TimeSync;
using System.Runtime.InteropServices.ComTypes;
using System.Diagnostics.Contracts;
using System.Security.Permissions;
using System.Security.Policy;
using System.Xml.Linq;

namespace QtBridgeStrategy
{
    /// <summary>
    /// An example of blank strategy. Add your code, compile it and run via Strategy Runner panel in the assigned trading terminal.
    /// Information about API you can find here: http://api.quantower.com
    /// Code samples: https://github.com/Quantower/Examples 
    /// </summary>
    public class QtBridgeStrategy : Strategy
    {
        /// <summary>
        /// Strategy's constructor. Contains general information: name, description etc. 
        /// </summary>
        ///         
        [InputParameter("Port", 10)]
        public int Port { get; set; }
        //public Account account;
        private int BrokerId = 4;
        private string prevOrderMsg = "";
        private string prevAcctMsg = "";
        private string prevPositionsMsg = "";
        bool connectedToPlatform = false;
        bool running = false;
        Socket listener;
        System.Timers.Timer socketTimer;
        Socket handler;
        List<Symbol> Symbols = new List<Symbol>();
        List<SymbolPrice> symbolPrices = new List<SymbolPrice>();
        List<string> subscribedSymbols = new List<string>(); // currently not used,just send everything in the symbolsmap
        Dictionary<string, string> symbolMap = new Dictionary<string, string>(){
            // FOREX
            {"-6A", "DA6"},
            {"-6B", "BP6"},
            {"-6C", "CA6"},
            {"-6E", "EU6"},
            {"-6J", "JY6"},
            {"-6N", "NE6"},
            {"-6S", "SF6"},

            // INDEXES
            {"-YM", "YM"}, 
            //{"E7", "EEU"},
            {"-NQ", "ENQ"},
            {"-ES", "EP"},
            {"-RTY", "RTY"},
            {"-NKD", "NKD"},

            // COMMODITIES
            {"-HG", "CPE"},
            {"-GC", "GCE"},
            {"-SI", "SIE"},
            {"-NG", "NGE"},
            {"-CL", "CLE"},
            //{"-HO", "HOE"},//

            {"-ZC", "ZCE"},
            {"-ZL", "ZLE"}, // SOYBEAN OIL
            {"-ZM", "ZME"}, // SOYBEAN MEAL
            {"-ZS", "ZSE"}, // SOYBEAN
            {"-ZW", "ZWA"}, // WHEAT
            /*{"GF", "GF"}, // CATTLE FEEDER
            {"LE", "GLE"}, // LIVE CATTLE
            */

            {"-ZN", "TYA"},
            {"-ZT", "TUA"},
            {"-ZF", "FVA"},

            {"-M6B", "M6B"},
            {"-M6E", "M6E"},
            {"-M2K", "M2K"}, // RUSSELL
            {"-MES", "MES"}, // SPX
            {"-MNQ", "MNQ"}, // NASDAQ
            {"-MGC", "MGC"}, // GOLD
            {"-MCL", "MCLE"} // MICRO CL
            /*
            {"QG", "NQG"}, // EMINI NATURAL GAS
            {"QM", "NQM"}, // EMINI QM
            {"M6A", "M6A"},
            {"M6C", "GMCD"}, // CAD MICRO
            {"MYM", "MYM"}, // DOW MICRO
            */
        };

        class SymbolPrice
        {
            public string symbol;
            public double bid;
            public double ask;
        }

        class BridgeOrder
        {
            public string symbol;
            public int orderType;
            public double qty;
            public double entry;
            public double stop;
            public double target;
            public string ogId;
            public string contractName;
        }

        class BridgeOrderResponse
        {
            public string symbol;
            public string account;
            public int broker = 4; // default
            public string ogId;
            public string[] orderIds = new string[2];
            public double[] targets = new double[2];
        }

        class SymbolInfo {
            public string symbol;
            public string localSymbol;
            public double tickSize;
            //public double tickAmount;
            public string desc;
            public string exp;
        }
        class OrderInfo
        {
            public string id;
            public string symbol;
            public double entry;
            public double stop;
            public double target;
            public string side;
            public double qty;
            public string date;
            public string status;
            public string orderType;
            public double ogStop;
            public double ogEntry;
            public string ogId;
            public double cancel;
        }

        class OrderInfoAdditional
        {
            public string ogId;
            public double ogStop;
            public double ogEntry;
            public double cancel;
            public bool cancelTgOffsetHack;
        }

        class PositionsInfo
        {
            public string symbol;
            public double qty;
            public double pnl;
        }

        public QtBridgeStrategy(): 
        base()
        {
            // Defines strategy's name and description.
            this.Name = "QtBridge";
            this.Description = "Qt Bridge";
        }

        private static byte[] ArraySegmentToArray(ArraySegment<byte> segment) =>
            segment.ToArray();

        async void InitSocketServer()
        {
            try
            {
                IPAddress ipAddress = IPAddress.Parse("127.0.0.1");
                IPEndPoint ipEndPoint = new(ipAddress, Port); //3300

                listener = new(
                    ipEndPoint.AddressFamily,
                    SocketType.Stream,
                    ProtocolType.Tcp);
                listener.Bind(ipEndPoint);
                listener.Listen(Port);

                Log("waiting for bridge client...");
                handler = await listener.AcceptAsync();
                Log("bridge client connected");

                //InitializeSymbolsList();
                SocketServerTick();

                if (socketTimer == null)
                {
                    socketTimer = new System.Timers.Timer();
                    socketTimer.Interval = 500; // in miliseconds
                    socketTimer.Elapsed += OnTimedEvent;
                    socketTimer.Start();
                }
                else
                    socketTimer.Start();
                SendMessageToClient(SymbolInfosMsg());
            }
            catch (Exception err)
            {
                Log(err.Message, StrategyLoggingLevel.Error);
            }
        }

        private string SubscribedSymbolPricesMsg()
        {
            string response = String.Format("<@|{0}|0", (int)Command.getSymbolPrice);
            foreach (var symbol in subscribedSymbols)
            //foreach (var smap in symbolMap)
            {
                var smap = symbolMap.First(o => o.Key == symbol);
                Symbol sym = Symbols.First(o => o.Root == smap.Value);
                double bid = sym.Bid;
                double ask = sym.Ask;
                if (bid.IsNanOrDefault() || ask.IsNanOrDefault())
                {
                    if (sym.Last.IsNanOrDefault())
                        continue;
                    bid = sym.Last;
                    ask = sym.Last;
                }
                var symPrice = symbolPrices.FirstOrDefault(o => o.symbol == symbol);
                //if (symPrice.bid == bid && symPrice.ask == ask)
                //    continue;
                //else
                //{
                    symPrice.bid = bid;
                    symPrice.ask = ask;
                //}
                response += String.Format("|{0}#{1}#{2}", smap.Key, bid, ask);
            }
            response += "|@>/eom";
            return response;
        }
        private string AccountInfoMsg(string callId = "0")
        {
            Account account = Core.Accounts[0]; //this.account;//account.AdditionalInfo // TODO what if multiple accounts?
            return String.Format("<@|{0}|{1}|{2}|{3}|{4}|{5}|@>", (int)Command.getAccount, callId, account.Id, account.Balance, account.Balance, account.Balance);//equity //free margin
        }

        private string SymbolInfosMsg(string callId = "0")
        {
            List<SymbolInfo> infos = new List<SymbolInfo>();
            foreach (var sym in Symbols.Where(o => o != null && symbolMap.ContainsValue(o.Root)))
            {
                infos.Add(new SymbolInfo
                {
                    symbol = symbolMap.First(o => o.Value == sym.Root).Key,
                    localSymbol = sym.Name,
                    desc = sym.Description,
                    exp = sym.ExpirationDate.ToShortDateString(),
                    tickSize = sym.TickSize
                    //tickAmount = sym.GetTickCost()
                });
            }
            string jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(infos);
            return String.Format("<@|{0}|{1}|{2}|ok|@>", (int)Command.getSymbolInfo, callId, jsonStr);
        }

        private string PositionsInfoMsg(string callId = "0")
        {
            List<PositionsInfo> objs = new List<PositionsInfo>();

            foreach (var obj in Core.Positions)
            {
                objs.Add(new PositionsInfo
                {
                    symbol = GetSymbolName(obj.Symbol),
                    qty = obj.Quantity
                    //pnl = obj.GrossPnL.Value
                });
            }
            string jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(objs);
            return String.Format("<@|{0}|{1}|{2}|ok|@>", (int)Command.getPositions, callId, jsonStr);
        }

        private string OrdersMsg(string callId = "0")
        {
            List<OrderInfo> orders = new List<OrderInfo>(); 
            foreach (var order in Core.Orders)
            {
                try
                {
                    OrderInfoAdditional additionalInfo = new OrderInfoAdditional();
                    if(order.Comment != null && order.Comment != "")
                        Newtonsoft.Json.JsonConvert.PopulateObject(order.Comment, additionalInfo);

                    var info = new OrderInfo
                    {
                        id = order.Id,
                        symbol = GetSymbolName(order.Symbol),
                        status = order.Status.ToString().ToLower(),
                        date = order.LastUpdateTime.ToShortDateString(),
                        qty = order.TotalQuantity,
                        side = order.Side.ToString().ToLower(),
                        orderType = order.OrderTypeId.ToString().ToLower(),
                        ogStop = additionalInfo.ogStop,
                        ogEntry = additionalInfo.ogEntry,
                        ogId = additionalInfo.ogId,
                        cancel = additionalInfo.cancel
                    };

                    if(order.StopLoss != null || order.TakeProfit != null) // initial entry
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
                    orders.Add(info);
                }
                catch (Exception err)
                {
                    Log(err.Message, StrategyLoggingLevel.Error);
                }
            }
            string jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(orders);
            return String.Format("<@|{0}|{1}|{2}|ok|@>", (int)Command.getOrders, callId, jsonStr);
        }

        private void CheckCancelPrice()
        {
            foreach (var order in Core.Orders)
            {
                try
                {
                    double price = order.Symbol.Last;
                    OrderInfoAdditional additionalInfo = new OrderInfoAdditional();
                    if (order.Comment != null && order.Comment != "")
                        Newtonsoft.Json.JsonConvert.PopulateObject(order.Comment, additionalInfo);

                    if(additionalInfo.cancel != 0 && price > 0)
                    {
                        if (order.OrderTypeId == OrderType.Limit) 
                        {
                            bool isShort = order.Side == Side.Sell;
                            double cancelPrice = additionalInfo.cancel;

                            if ((isShort && price < cancelPrice) || (!isShort && price > cancelPrice))
                            {
                                Log("Order Cancel Price Reached: " + order.Symbol.Name + " " + order.Symbol.Root);
                                order.Cancel();
   
                            }
                        }
                    }
                }
                catch (Exception err)
                {
                    Log(err.Message, StrategyLoggingLevel.Error);
                }
            }
        }
        private void OnTimedEvent(Object source, System.Timers.ElapsedEventArgs e)
        {
            if(!running && handler != null && connectedToPlatform)
            {
                running = true;
                try
                {
                    string msg = "";
                    // TODO only send changed price
                    SendMessageToClient(SubscribedSymbolPricesMsg());
                    msg = AccountInfoMsg();
                    if(msg != prevAcctMsg)
                    {
                        prevAcctMsg = msg;
                        SendMessageToClient(msg);
                    }

                    msg = OrdersMsg();
                    if (msg != prevOrderMsg)
                    {
                        prevOrderMsg = msg;
                        SendMessageToClient(msg);
                    }

                    msg = PositionsInfoMsg();
                    if(msg != prevPositionsMsg)
                    {
                        prevPositionsMsg = msg;
                        SendMessageToClient(msg);
                    }

                    CheckCancelPrice();
                }
                catch(Exception err)
                {
                    Log(err.Message, StrategyLoggingLevel.Error);
                    ProcessDisconnected(err.Message);
                }
                running = false;
            }
            //Log("tick " + symbol.Ask, StrategyLoggingLevel.Info);
        }

        private bool ProcessDisconnected(string msg)
        {
            if (msg == "An established connection was aborted by the software in your host machine"
            || msg == "An existing connection was forcibly closed by the remote host")
            {
                Disconnect();
                InitSocketServer();
                return true;
            }
            return false;
        }

        private void SendMessageToClient(string message)
        {
            message += "/eom";
            var echoBytes = Encoding.UTF8.GetBytes(message);
            var sendArgs = new SocketAsyncEventArgs();
            sendArgs.SetBuffer(echoBytes, 0, 0);
            handler.Send(echoBytes, SocketFlags.None);
        }

        private void InitializeSymbolsList(string[] contractNames)
        {
           
            Core.RemoveSymbolList("allsymbols");
            Symbols.Clear();
            if (contractNames.Length > 0)
            {
                foreach (string contractName in contractNames)
                {
                    if (contractName != "" && contractName != null)
                    {
                        Symbol sym = Core.GetSymbol(new GetSymbolRequestParameters
                        {
                            SymbolId = "F.US." + contractName
                        }, null, NonFixedListDownload.Download);
                        if(sym != null)
                        Symbols.Add(sym);
                    }
                }
            }
            else
            {
                Symbols = Core.Symbols.ToList();
                Core.AddSymbolList("allsymbols", Symbols.Where(o => symbolMap.ContainsValue(o.Root)));
            }
            Core.AddSymbolList("allsymbols", Symbols);
        }

        async void SocketServerTick()
        {
            try
            {
                while (true)
                {
                    var buffer = new ArraySegment<byte>(new byte[1_204]);
                    var received = await handler.ReceiveAsync(buffer, SocketFlags.None);
                    var response = Encoding.UTF8.GetString(ArraySegmentToArray(buffer));
                    //Log(response);
                    var eom = "\r\n";
                    if (response.IndexOf(eom) > -1 /* is end of message */)
                    {
                        //response = response.Replace(eom, "");
                        string[] r = response.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                        foreach (var msg in r)
                        {
                            Log($"Socket server received message: \"{msg}\"");
                            string[] args = ParseMessage(msg);
                            if (args == null)
                            {
                                if (msg != "")
                                    Log($"Invalid msg found: \"{msg}\"");
                            }
                            else
                            {
                                string respMsg = ProcessMessage(args);
                                if (respMsg == null)
                                {
                                    if (respMsg != "")
                                        Log($"Invalid msg found: \"{msg}\"");
                                }
                                else
                                {
                                    Log($"Socket server sending message: \"{msg}\"");
                                    SendMessageToClient(respMsg);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception err)
            {
                Log(err.Message, StrategyLoggingLevel.Error);
                ProcessDisconnected(err.Message);
            }
        }


        /// <summary>
        /// This function will be called after creating a strategy
        /// </summary>
        protected override void OnCreated()
        {
            // Add your code here
        }

        /// <summary>
        /// This function will be called after running a strategy
        /// </summary>
        protected override void OnRun()
        {
            // Add your code here
            Log("Init fuckers, waiting for QT Data Connection...");
            while (true)
            {
                if (Core.Connections.Connected.Length > 0)
                    break;
                Thread.Sleep(500);
            }
            Log("QT Data Connected");
            InitSocketServer();
            //InitializeSymbolsList();
        }

        /// <summary>
        /// This function will be called after stopping a strategy
        /// </summary>
        protected override void OnStop()
        {
            Disconnect();
        }

        private void Disconnect()
        {
            Symbols.Clear();
            symbolPrices.Clear();
            subscribedSymbols.Clear();
            ClearMsgs();
            connectedToPlatform = false;
            socketTimer?.Stop();
            handler?.Close();
            handler = null;
            listener?.Close();
            listener = null;
        }

        private void ClearMsgs()
        {
            prevAcctMsg = "";
            prevOrderMsg = "";
            prevPositionsMsg = "";
        }

        /// <summary>
        /// This function will be called after removing a strategy
        /// </summary>
        protected override void OnRemove()
        {
            Disconnect();
            // Add your code here
        }

        protected string GetSymbolName(Symbol sym)
        {
            var smap = symbolMap.First(o => sym.Root == o.Value);
            return smap.Key;
        }

        /// <summary>
        /// Use this method to provide run time information about your strategy. You will see it in StrategyRunner panel in trading terminal
        /// </summary>
        protected override List<StrategyMetric> OnGetMetrics()
        {
            List<StrategyMetric> result = base.OnGetMetrics();

            // An example of adding custom strategy metrics:
            // result.Add("Opened buy orders", "2");
            // result.Add("Opened sell orders", "7");

            return result;
        }

        protected string[] ParseMessage(string msg)
        {
            if(msg.StartsWith("<@") && msg.EndsWith("@>"))
            {
                msg = msg.Replace("<@", "");
                msg = msg.Replace("@>", "");
                return msg.Split('|');
            }
            return null;
        }

        private enum Command
        {
 
            ping = 0, // this should be init
            isConnected = 1,
            disconnect = 2,
            subSymbolPrice = 3,
            unsubSymbolPrice = 4,
            getSymbolPrice = 5,
            getAccount = 8,
            getOrders = 11,
            getPositions = 14,
            getSymbolInfo = 15,
            placeOrder = 16,
            modifyOrder = 17,
            cancelOrder = 18,
            flatten = 19,
            init = 20

            //subAccount = 6, 
            //unsubAccount = 7, 

            //subOrders = 9, 
            //unsubOrders = 10, 

            //subPositions = 12, 
            //unsubPositions = 13, 
        }
        /*
        protected int PriceToTicks(Symbol sym, double price1, double price2)
        {
            double tickSize = sym.TickSize;
            double diff = Math.Abs(price1 - price2);
            return (int)(diff / tickSize);
        }*/
    
        protected double CalcTicks(Symbol sym, Side side, double price, double offsetPrice, string type) // takeProfit or stop
        {
            Double ticks = 0;
            if (side == Side.Buy)
            {
                if(type == "stop")
                    ticks = sym.CalculateTicks(offsetPrice, price);
                else if(type == "target")
                    ticks = sym.CalculateTicks(price, offsetPrice);
            }
            else if (side == Side.Sell)
            {
                if (type == "target")
                    ticks = sym.CalculateTicks(offsetPrice, price);
                else if (type == "stop")
                    ticks = sym.CalculateTicks(price, offsetPrice);
            }
            return ticks;
        }

        protected double CalcPrice(Symbol sym, Side side, double price, double ticks, string type)
        {
            double p = 0;
            if (side == Side.Buy)
            {
                if (type == "stop")
                    p = sym.CalculatePrice(price, -ticks);
                else if (type == "target")
                    p = sym.CalculatePrice(price, ticks);
            }
            else if (side == Side.Sell)
            {
                if (type == "target")
                    p = sym.CalculatePrice(price, -ticks);
                else if (type == "stop")
                    p = sym.CalculatePrice(price, ticks);
            }
            return p;
        }

        protected double CalculateOgStop(string jsonString, bool isShort, double stopPrice)
        {
            double stop = stopPrice;
            OrderInfoAdditional additionalInfo = new OrderInfoAdditional();
            if (jsonString != null && jsonString != "")
            {
                Newtonsoft.Json.JsonConvert.PopulateObject(jsonString, additionalInfo);

                if (additionalInfo.ogStop > 0)
                {
                    double ogStop = additionalInfo.ogStop;
                    if (!isShort && stopPrice < ogStop)
                        stop = ogStop;
                    else if (isShort && stopPrice > ogStop)
                        stop = ogStop;
                }
            }
            return stop;
        }
        protected string ProcessMessage(string[] args)
        {
            string responseCommand = ""; 
            try
            {
                int command = int.Parse(args[1]);
                string callId = args[2];

                if (command == (int)Command.ping) // this should be init
                {
                    responseCommand = String.Format("<@|{0}|{1}|ok|@>", command, callId);
                }
                else if(command == (int)Command.init)
                {
                    connectedToPlatform = true;
                    string contractNamesString = args[3];
                    string[] list = contractNamesString.Split(',');
                    InitializeSymbolsList(list);
                    responseCommand = String.Format("<@|{0}|{1}|ok|@>", command, callId);
                }
                else if (command == (int)Command.isConnected)
                {
                    connectedToPlatform = true;
                    ClearMsgs();
                    responseCommand = String.Format("<@|{0}|{1}|ok|@>", command, callId);
                }
                else if (command == (int)Command.subSymbolPrice)
                {
                    string symbol = args[3];
                    if (!subscribedSymbols.Contains(symbol))
                    {
                        subscribedSymbols.Add(symbol);
                        symbolPrices.Add(new SymbolPrice
                        {
                            symbol = symbol,
                            bid = 0,
                            ask = 0
                        });
                    }
                    responseCommand = String.Format("<@|{0}|{1}|{2}|ok|@>", command, callId, symbol);
                }
                else if (command == (int)Command.unsubSymbolPrice)
                {
                    string symbol = args[3];
                    if (subscribedSymbols.Contains(symbol))
                    {
                        symbolPrices.Remove(symbolPrices.Find(o => o.symbol == symbol));
                        subscribedSymbols.Remove(symbol);
                    }
                    responseCommand = String.Format("<@|{0}|{1}|{2}|ok|@>", command, callId, symbol);
                }
                else if (command == (int)Command.getSymbolInfo)
                {
                    responseCommand = SymbolInfosMsg(callId);
                }
                else if (command == (int)Command.getAccount)
                {
                    responseCommand = AccountInfoMsg(callId);
                }
                else if (command == (int)Command.getOrders)
                {
                    responseCommand = OrdersMsg(callId);
                }
                else if (command == (int)Command.getPositions)
                {
                    responseCommand = PositionsInfoMsg(callId);
                }
                // https://docs.mql4.com/trading/ordersend
                else if (command == (int)Command.placeOrder)
                {
                    List<string> orderJsonStrings = new List<string>();
                    orderJsonStrings.Add(args[3]);
                    orderJsonStrings.Add(args[4]);

                    BridgeOrderResponse response = new BridgeOrderResponse();
                    response.account = Core.Accounts[0].Id;

                    int count = 0;
                    foreach (var orderJsonString in orderJsonStrings)
                    {
                        try
                        {
                            if (orderJsonString == "" || orderJsonString == null)
                                continue;

                            BridgeOrder order = new BridgeOrder();
                            Newtonsoft.Json.JsonConvert.PopulateObject(orderJsonString, order);
                            response.ogId = order.ogId;
                            response.symbol = order.symbol;
                            string qtSymbol = symbolMap[order.symbol];

                            Symbol sym = Symbols.First(o => o.Root == qtSymbol);
                            Side side = (order.orderType == 0 || order.orderType == 2 || order.orderType == 4) ? Side.Buy : Side.Sell;

                            var infos = new OrderInfoAdditional
                            {
                                ogStop = order.stop,
                                ogEntry = order.entry,
                                ogId = order.ogId
                            };

                            string comment = Newtonsoft.Json.JsonConvert.SerializeObject(infos);
                            var request = new PlaceOrderRequestParameters
                            {
                                Account = Core.Instance.Accounts[0],
                                Side = side,
                                StopLoss = SlTpHolder.CreateSL(CalcTicks(sym, side, order.entry, order.stop, "stop"), PriceMeasurement.Absolute, false, order.qty, 0, true),
                                TakeProfit = SlTpHolder.CreateTP(CalcTicks(sym, side, order.entry, order.target, "target"), PriceMeasurement.Absolute, order.qty, 0, true),
                                // request.TriggerPrice =
                                Price = order.entry,
                                Quantity = order.qty,
                                TimeInForce = TimeInForce.GTC,
                                OrderTypeId = (order.orderType == 0 || order.orderType == 1) ? OrderType.Market : ((order.orderType == 2 || order.orderType == 3) ? OrderType.Limit : OrderType.StopLimit),
                                Symbol = sym,
                                Comment = comment,
                            };

                            var res = Core.PlaceOrder(request);
                            response.orderIds[count] = res.OrderId;
                            response.targets[count] = order.target;
                        }
                        catch (Exception err)
                        {
                            Log(err.Message, StrategyLoggingLevel.Error);
                        }
                        count++;
                    }

                    //int ticket = OpenOrder(brokerSymbol, orderType, qty, entry, stop, target);
                    responseCommand = String.Format("<@|{0}|{1}|{2}|@>", command, callId, Newtonsoft.Json.JsonConvert.SerializeObject(response));
                }
                else if (command == (int)Command.modifyOrder)
                {
                    string id = args[3];
                    string symbol = args[4];
                    double price = double.Parse(args[5]);
                    string orderType = args[6];

                    Order order = Core.GetOrderById(id);

                    OrderInfoAdditional additionalInfo = new OrderInfoAdditional();
                    if (order.Comment != null && order.Comment != "")
                        Newtonsoft.Json.JsonConvert.PopulateObject(order.Comment, additionalInfo);

                    ModifyOrderRequestParameters parameters = new ModifyOrderRequestParameters(order);
                    if (orderType == "entry") 
                    { 
                        parameters.Price = price;
                        additionalInfo.ogEntry = price;

                        if (parameters.StopLoss != null)
                        {
                            double stopPrice = CalcPrice(order.Symbol, order.Side, order.Price, order.StopLoss.Price, "stop");
                            if ((order.Side == Side.Buy && stopPrice <= price) || (order.Side == Side.Sell && stopPrice >= price))
                                parameters.StopLoss = SlTpHolder.CreateSL(CalcTicks(order.Symbol, order.Side, price, stopPrice, "stop"), PriceMeasurement.Absolute, false, order.StopLoss.Quantity, double.NaN, true);
                            additionalInfo.ogStop = stopPrice;
                        }
                        parameters.Comment = Newtonsoft.Json.JsonConvert.SerializeObject(additionalInfo);
                        //if(parameters.TakeProfit != null)
                        //    parameters.TakeProfit = SlTpHolder.CreateTP(CalcTicks(order.Symbol, order.Side, order.Price, price, "target"), PriceMeasurement.Absolute, order.TakeProfit.Quantity, double.NaN, true);
                    }
                    if (orderType == "stop")
                    {
                        if (order.StopLoss != null)
                        {
                            additionalInfo.ogStop = price;
                            parameters.StopLoss = SlTpHolder.CreateSL(CalcTicks(order.Symbol, order.Side, order.Price, price, "stop"), PriceMeasurement.Absolute, false, order.StopLoss.Quantity, double.NaN, true);
                            parameters.Comment = Newtonsoft.Json.JsonConvert.SerializeObject(additionalInfo);
                        }
                        else
                            parameters.TriggerPrice = price;
                    }
                    if (orderType == "target")
                    {
                        if (order.TakeProfit != null)
                            parameters.TakeProfit = SlTpHolder.CreateTP(CalcTicks(order.Symbol, order.Side, order.Price, price, "target"), PriceMeasurement.Absolute, order.TakeProfit.Quantity, double.NaN, true);
                        else
                            parameters.Price = price;
                    }
                    if (orderType == "cancel")
                    {
                        additionalInfo.cancel = price;

                        // seems like the order must be recreated to change the comment.  so we're going to offset the tg a bit back and forth to force the order recreation
                        int offset = additionalInfo.cancelTgOffsetHack ? -1 : 1;
                        additionalInfo.cancelTgOffsetHack = !additionalInfo.cancelTgOffsetHack;
                        parameters.Comment = Newtonsoft.Json.JsonConvert.SerializeObject(additionalInfo);
                        parameters.TakeProfit = SlTpHolder.CreateTP(order.TakeProfit.Price + offset, order.TakeProfit.PriceMeasurement, order.TakeProfit.Quantity, order.TakeProfit.QuantityPercentage, order.TakeProfit.Active);
                    }

                    var result = Core.ModifyOrder(parameters);
                    //var ord = Core.GetOrderById(result.OrderId);
                    responseCommand = String.Format("<@|{0}|{1}|{2}|{3}|@>", command, callId, id, symbol);
                }
                else if (command == (int)Command.cancelOrder)
                {
                    string id = args[3];
                    string symbol = args[4];
                    Order order = Core.GetOrderById(id);
                    order?.Cancel();
                    responseCommand = String.Format("<@|{0}|{1}|{2}|{3}|@>", command, callId, id, symbol);
                }
                else if (command == (int)Command.flatten)
                {
                    string targetSymbol = args[3];
                    foreach (var pos in Core.Positions)
                    {
                        if (GetSymbolName(pos.Symbol) == targetSymbol)
                            pos.Close();
                    }
                    foreach (var order in Core.Orders)
                    {
                        if (GetSymbolName(order.Symbol) == targetSymbol)
                            order.Cancel();
                    }
                    responseCommand = String.Format("<@|{0}|{1}|{2}|@>", (int)Command.flatten, callId, targetSymbol);
                }
            }
            catch(Exception err)
            {
                Log(err.Message, StrategyLoggingLevel.Error);
            }

            if (responseCommand == "")
                return null;
            return responseCommand;
        }
    }
}

//https://learn.microsoft.com/en-us/aspnet/core/signalr/hubcontext?view=aspnetcore-3.1
//https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/sockets/socket-services
