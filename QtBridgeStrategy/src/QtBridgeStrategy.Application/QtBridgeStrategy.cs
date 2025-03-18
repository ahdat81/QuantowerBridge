using System;
using System.Net;
using TradingPlatform.BusinessLayer;
using System.Threading.Tasks;
using WebSocketSharp.Server;
using QtBridgeStrategy.Enums;
using QtBridgeStrategy.Logging;
using QtBridgeStrategy.Services.Brokers;
using QtBridgeStrategy.Services;

namespace QtBridgeStrategy
{
    //https://learn.microsoft.com/en-us/aspnet/core/signalr/hubcontext?view=aspnetcore-3.1
    //https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/sockets/socket-services

    /// <summary>
    /// Quantower Strategy bridge to the PriceActionGroups api, for custom order management and real-time data.
    /// Uses WebSocketServer to connect multiple clients.
    /// Live data from AmpFutures, such as AccountInfo, Orders, Positions, and Live bid/ask.
    /// Pre Orders: Allow user to place an order within the strategy, that turns into a live market order when triggered.  This allows the user to place limit orders without pre-maturely exceeding margin requirements.
    /// Order Managment: 
    ///     Place / modify / cancel orders directly from PAG, 
    ///     Unique stop adjustment strategies, 
    ///     Stop failsafe (in case stop limits are not triggered).
    /// </summary>

    public class QtBridgeStrategy : Strategy
    {
        /// <summary>
        /// Strategy's constructor. Contains general information: name, description etc. 
        /// </summary>
        ///         
        [InputParameter("Server Ip")]
        public string ServerIp { get; set; }

        [InputParameter("Port")]
        public int Port { get; set; }

        [InputParameter("Data Interval Ms")]
        public int DataIntervalMs { get; set; }

        [InputParameter("Order Check Interval Ms")]
        public int OrderCheckIntervalMs { get; set; }

        [InputParameter("Pre Order Trigger Ticks")]
        public double PreOrderTriggerTicks { get; set; }

        [InputParameter("Pre Order Max Spread Ticks")]
        public double PreOrderMaxSpreadTicks { get; set; }

        [InputParameter("Pre Order Revert Ticks")]
        public double PreOrderRevertTicks { get; set; }

        [InputParameter("Use SC Contracts")]
        public bool UseScContracts { get; set; }

        [InputParameter("SC Contracts Url")]
        public string ScContractsUrl { get; set; }

        [InputParameter("Contract Expiration Rollover Days Offset")]
        public int ContractExpirationRolloverDaysOffset { get; set; }

        [InputParameter("Inactive New Trading Begin Hour EST")]
        public int InactiveNewTradingBeginHour { get; set; }

        [InputParameter("Inactive New Trading Begin Min EST")]
        public int InactiveNewTradingBeginMin { get; set; }

        [InputParameter("Inactive Trading Begin Hour EST")]
        public int InactiveTradingBeginHour { get; set; }

        [InputParameter("Inactive Trading Begin Min EST")]
        public int InactiveTradingBeginMin { get; set; }

        [InputParameter("Inactive Trading End Hour EST")]
        public int InactiveTradingEndHour { get; set; }

        [InputParameter("Inactive Trading End Min EST")]
        public int InactiveTradingEndMin { get; set; }

        [InputParameter("Close All Live Positions And Orders EOD")]
        public bool CloseAllOrdersEOD { get; set; }

        [InputParameter("Close All PreOrders EOD")]
        public bool CloseAllPreOrdersEOD { get; set; }

        [InputParameter("StopTriggerPriceTicksOffset")]
        public int StopTriggerPriceTicksOffset { get; set; }

        [InputParameter("AutoManageTargetOrders")]
        public bool AutoManageTargetOrders { get; set; }

        [InputParameter("Enable Stop Failsafe")]
        public bool EnableStopFailsafe { get; set; }

        [InputParameter("Use Amp Futures")]
        public bool UseAmpFutures { get; set; }

        [InputParameter("Use Ameritrade")]
        public bool UseAmeritrade { get; set; }

        [InputParameter("AvgCommission (micro: 0.6, mini: 2.0)")]
        public double AvgCommission { get; set; }

        public Core CoreRef { get; set; }
        public Logger Log { get; set; }
        public Broker Broker { get; set; }

        private WebSocketServer _wssv;
        private bool _checkOrdersTaskRunning = false;

        public QtBridgeStrategy() :
        base()
        {
            // Defines Qt strategy's name and description.
            Name = "QtBridge";
            Description = "Qt Bridge";
        }

        /// <summary>
        /// Built-in Quantower function called after creating a strategy
        /// </summary>
        protected override async void OnCreated()
        {
            CoreRef = Core;
            Log = new Logger(this);
            SetQtUserSettingsToDefault();
            await Task.Delay(1000); // give Qt some time to settle down on initial create
            Run(); // auto-run the strategy
        }

        /// <summary>
        /// Built-in Quantower function called after running a strategy
        /// </summary>
        protected override async void OnRun()
        {
            await WaitForQtConnection(); // wait for Qt to be connected.  There's nothing we can do in Qt until then.
            Disconnect(); // disconnect all clients.  Re-initialize strategy, before accepting new clients
            InitializeBroker();
        }

        /// <summary>
        /// Built-in Quantower function called after stopping a strategy
        /// </summary>
        protected override void OnStop()
        {
            Disconnect();
        }

        /// <summary>
        /// Built-in Quantower function called after removing a strategy
        /// </summary>
        protected override void OnRemove()
        {
            Disconnect();
        }

        /// private functions ----------------------------------------------------------

        /// <summary>
        /// Sets Qt user settings to default.
        /// </summary>
        private void SetQtUserSettingsToDefault()
        {
            // Use local ip if setting up on remote computer: ex. 192.168.0.102
            if (ServerIp == null || ServerIp == "")
                ServerIp = "127.0.0.1";

            if (Port == 0)
                Port = 3300;

            if (DataIntervalMs == 0)
                DataIntervalMs = 300;

            if (OrderCheckIntervalMs == 0)
                OrderCheckIntervalMs = 100;

            if (PreOrderTriggerTicks == 0)
                PreOrderTriggerTicks = 6;

            if (PreOrderMaxSpreadTicks == 0)
                PreOrderMaxSpreadTicks = 12;

            if (ScContractsUrl == "" || ScContractsUrl == null)
                ScContractsUrl = "https://www.priceactiongroups.com:2087/sccontractnames/";

            if (!UseAmeritrade && !UseAmpFutures)
                UseAmpFutures = true;

            if (StopTriggerPriceTicksOffset <= 0)
                StopTriggerPriceTicksOffset = 10;

            if (ContractExpirationRolloverDaysOffset <= 0)
                ContractExpirationRolloverDaysOffset = 2;

            if (InactiveTradingBeginHour == 0 && InactiveTradingBeginMin == 0 && InactiveTradingEndHour == 0 && InactiveTradingEndMin == 0)
            {
                InactiveTradingBeginHour = 16; //4:40 pm
                InactiveTradingBeginMin = 40;
                InactiveTradingEndHour = 18; //6:05 pm, market opens 5. this will allow you to enter pre orders before the market.
                InactiveTradingEndMin = 5;
            }

            if (InactiveNewTradingBeginHour == 0 && InactiveNewTradingBeginMin == 0)
            {
                InactiveNewTradingBeginHour = 16; //4:00 pm
                InactiveNewTradingBeginMin = 0;
            }

            if (AvgCommission <= 0)
                AvgCommission = 0.6;
        }

        /// <summary>
        /// Initializes the Broker object, based on the user's selection.  Currently only supports AmpFutures, AmeriTrade is deprecated
        /// </summary>
        private async void InitializeBroker()
        {
            //_broker?.Dispose();
            if (UseAmpFutures)
            {
                Broker = new AmpFuturesBroker(FuturesContractNamesSource.SierraChart, UseScContracts ? ScContractsUrl : "", ContractExpirationRolloverDaysOffset);
            }
            else if (UseAmeritrade) // Ameritrade is no longer supported, but we'll leave it here as a stub for adding future brokers
            {
                Log("Initializing Ameritrade...");
                Broker = new AmeriTradeBroker();
            }

            switch (await Broker.InitializeAsync())
            {
                case InitResult.Error:
                    Log.Error("Strategy failed to initialize and will now stop.  Retry or contact the administrator. ");
                    Stop();
                    break;
                case InitResult.DelayedRetry: // Quantower may not be fully initialized.  Not sure why this happens sometimes.  Wait a few seconds, then try again...
                    Log.Error("Strategy will attempt to restart in a few seconds.  Retry or contact the administrator if issues persist.");
                    Stop();
                    Core.Initialize();
                    await Task.Delay(3000);
                    Log("Attempting to restart Strategy. ", StrategyLoggingLevel.Info);
                    Run();
                    break;
                case InitResult.Success:
                    Initialize();
                    Log("Qt Strategy successfully initialized.  Ready for client connections...");
                    break;
                default:
                    Log.Error("Unknown response for Broker.InitializeAsync()");
                    break;
            }
        }

        /// <summary>
        /// Initializes the web socket server and order manager worker thread
        /// </summary>
        async void Initialize()
        {
            try
            {
                StartWebSocketServer();
                StartOrderManagementWorker();
                await Task.Delay(100); // add a small delay to let things settle down a bit
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
                Disconnect();
            }
        }

        /// <summary>
        /// Wait for a connection to be Qt established.  Quantower isn't usable until a connection is established.
        /// </summary>
        private async Task<bool> WaitForQtConnection()
        {
            Log("Waiting for Quantower data connection...");
            while (true)
            {
                //if (core.Connections.Connected.Length > 0)
                if (IsQtConnected())
                {
                    Log("Quantower data connected");
                    return true;
                }
                await Task.Delay(100);
            }
        }

        /// <summary>
        /// Check if Qt is connected to its server.
        /// We only support a single qt connection.  Not even sure how qt connects to multiple connections.
        /// </summary>
        public bool IsQtConnected()
        {
            var connections = Core.Connections.Connected;
            return
                connections != null && // is the connections list valid.
                connections.Length > 0 && // is there at least one connection.  
                connections[0].State == ConnectionState.Connected && // is the first (and only) connection is connected.
                State == StrategyState.Working; // is the connection in the correct state.
        }

        /// <summary>
        /// Stop, re-initialize, and start WebSocketServer
        /// </summary>
        private void StartWebSocketServer()
        {
            Log("Starting WebSocketServer...");
            StopWssv();
            _wssv = new WebSocketServer(IPAddress.Parse(ServerIp), Port, false);
            _wssv.AddWebSocketService<WebSocketServerClient>("/Client", (session) =>
            {
                session.StartClient(DataIntervalMs);
                session.Init(Broker);
            });
            _wssv.Start();
        }

        /// <summary>
        /// On a polling interval, checks the connection status for Qt. Then checks orders and manages accordingly.
        /// We do this at the root of the Strategy since we need to control turning the strategy on/off on disconnect.
        /// </summary>
        private async void StartOrderManagementWorker(int interval = 500)
        {
            Log("Starting Orders Manager...");
            _checkOrdersTaskRunning = true;
            bool initialized = false;
            try
            {
                while (_checkOrdersTaskRunning)
                {
                    var connections = Core.Connections.Connected;
                    if (IsQtConnected())
                    {
                        initialized = true;
                        Broker.CheckOrders();
                        if (EnableStopFailsafe && Core.TradingStatus == TradingStatus.Allowed)
                            Broker.CheckStopFailsafe();
                    }
                    else if (initialized) // was connected, but qt connection is lost, disconnect all now and auto re-start the initialization process
                    {
                        Log("Disconnected from Broker, restarting strategy to re-iniitalize connections...");
                        initialized = false;
                        Stop();
                        await Task.Delay(500);
                        Run();
                        break;
                    }
                    await Task.Delay(interval);
                }
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
                Disconnect(true);
            }
        }

        /// <summary>
        /// Disconnects the web socket server and stops the check order worker.  Option to restart.
        /// </summary>
        protected async void Disconnect(bool restartAfterDisconnect = false)
        {
            await StopCheckOrdersWorker();
            StopWssv();
            if (restartAfterDisconnect)
                Initialize();
        }

        /// <summary>
        /// Disconnects the web socket server and stops the check order worker
        /// </summary>
        private async Task<bool> StopCheckOrdersWorker()
        {
            Log("Stopping Orders Manager...");
            _checkOrdersTaskRunning = false;
            await Task.Delay(OrderCheckIntervalMs * 2); // delay long enough for the worker to be checked and end
            return true;
        }

        /// <summary>
        /// Stops the web socket server
        /// </summary>
        private void StopWssv()
        {
            Log("Stopping Web Socket Server...");

            try
            {
                _wssv?.Stop();
            }
            catch (Exception ex)
            {
                // logging not necessary, might be more confusing than helpful
            }
            finally
            {
                _wssv = null;
            }
        }
    }
}
