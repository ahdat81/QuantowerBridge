using System;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;
using WebSocketSharp.Server;
using WebSocketSharp;
using QtBridgeStrategy.Enums;
using QtBridgeStrategy.Services.Brokers;

namespace QtBridgeStrategy.Services
{
    /// <summary>
    /// Web Socket client, facilitating communication back to PAG.  
    /// Some of this code was ported from an older bridge using a network socket.  
    /// We may want to refactor at some point to not use the custom message formatting to something standard.
    /// </summary>
    public class WebSocketServerClient : WebSocketBehavior
    {
        private bool _initData = false;
        private bool _initialized = false;
        private string _prevOrderMsg = "";
        private string _prevAcctMsg = "";
        private string _prevPositionsMsg = "";
        private bool _sendSymbolPricesOnNextInterval = true;
        private int _intervalErrorCount = 0;

        private Logging.Logger _log;
        private QtBridgeStrategy _qtStrategy;
        private Broker _broker;
        private WebSocketServerClientCommands _wsCommands;
        private Core _core;
        private WebSocketServerClientDataStreamMessages _dataStreamMessages;


        /// <summary>
        /// Constructor
        /// </summary>
        public WebSocketServerClient()
        {
        }

        /// <summary>
        /// Starts the data stream
        /// </summary>
        /// <param name="intervalMs">Interval the data stream sends data back to client.</param>
        public void StartClient(int intervalMs = 300)
        {
            StartDataStream(intervalMs);
        }

        /// <summary>
        /// We can't pass properties the to constructor, but we need to pass the strategy, so we need to initialize this class.
        /// </summary>
        /// <param name="qtStrategy"></param>
        public void Init(QtBridgeStrategy qtStrategy)
        {
            _dataStreamMessages = new WebSocketServerClientDataStreamMessages(qtStrategy);
            _wsCommands = new WebSocketServerClientCommands(qtStrategy);
            _core = qtStrategy.CoreRef;
            _log = qtStrategy.Log;
            _log.Info("new client connected");
        }

        /* Rather than fire events, we'll just pass the Quantower Strategy and directly access its functions
        public delegate void LogEventHandler(object sender, LogEventArgs e);
        public event LogEventHandler LogEvent;

        public delegate void PlacePreOrderEventHandler(object sender, PlacePreOrderEventArgs e);
        public event PlacePreOrderEventHandler PlacePreOrderEvent;

        public delegate void ModifyPreOrderEventHandler(object sender, ModifyPreOrderEventArgs e);
        public event ModifyPreOrderEventHandler ModifyPreOrderEvent;

        public delegate void CancelPreOrderEventHandler(object sender, CancelPreOrderEventArgs e);
        public event CancelPreOrderEventHandler CancelPreOrderEvent;

        public delegate void FlattenPreOrderEventHandler(object sender, FlattenPreOrderEventArgs e);
        public event FlattenPreOrderEventHandler FlattenPreOrderEvent;
        */

        /// <summary>
        /// On socket opened to client
        /// </summary>
        protected override void OnOpen()
        {

        }

        /// <summary>
        /// On message received from client
        /// </summary>
        /// <param name="e"></param>
        protected override async void OnMessage(MessageEventArgs e)
        {
            try
            {
                string msgsStr = e.Data;
                if (msgsStr.Length > 5)
                {
                    var msgs = msgsStr.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                    foreach (var msg in msgs)
                    {
                        if (msg.Length <= 1)
                            continue;

                        await ProcessRawMessage(msg);
                    }
                }
            }
            catch (Exception ex) { _log.Ex(ex); }
        }

        /// <summary>
        /// Processes the raw string to get the individual messages
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        private async Task<bool> ProcessRawMessage(string msg)
        {
            string[] args = ParseMessage(msg);
            if (args == null)
            {
                if (msg != "")
                    Console.Write($"Invalid msg found: \"{msg}\"");
            }
            else
            {
                string respMsg = await ProcessMessage(args);
                if (respMsg == "")
                    Console.Write($"Invalid msg found: \"{msg}\"");
                else if (respMsg != null)
                {
                    Send(respMsg + "\r\n");
                    //Log($"Socket server sending message: \"{msg}\"");
                    //await SendMessageToClient(respMsg, stream);
                }
            }
            return true;
        }

        /// <summary>
        /// Parse individual message
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        protected string[] ParseMessage(string msg)
        {
            if (msg.StartsWith("<@") && msg.EndsWith("@>"))
            {
                msg = msg.Replace("<@", "");
                msg = msg.Replace("@>", "");
                return msg.Split('|');
            }
            return null;
        }

        /// <summary>
        /// Process the message based on the command request.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public async Task<string> ProcessMessage(string[] args)
        {
            string responseCommand = "";
            try
            {
                int commandId = int.Parse(args[1]);
                string callId = args[2];
                Command command = (Command)Enum.ToObject(typeof(Command), commandId);
                await _wsCommands.CallCommand(command, commandId, callId, args);
            }
            catch (Exception ex) {
                _log.Ex(ex); 
            }

            //if (responseCommand == "")
            //    return null;
            return responseCommand;
        }

        /// <summary>
        /// Send message to the client
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="log"></param>
        public void SendMessageToClient(string msg, bool log = true)
        {
            Send(msg + "\r\n");
            if (log)
                Log.Info(msg);
        }

        /// <summary>
        /// Start data stream thread, which will continuously send updated information back to the client, on an interval
        /// </summary>
        /// <param name="interval"></param>
        public async void StartDataStream(int interval = 300)
        {
            if (_initialized) // only allow this to be started once.
                return;
            _initialized = true;

            while (true)
            {
                GenerateStreamMessage();
                await Task.Delay(interval);
            }
        }

        /// <summary>
        /// Creates the stream message to be sent back to the client
        /// Symbol price, account, orders and position information.
        /// </summary>
        public void GenerateStreamMessage()
        {
            string step = "start"; // track the step so we know where an error occurs, for logging purposes
            try
            {
                if (_core != null && _core.Connections != null)
                {
                    if (_qtStrategy.IsQtConnected())
                    {
                        bool force = !_initData;

                        // check for errors
                        if (_broker.GetSymbols().Count == 0)
                            throw new Exception("OnInterval: No Symbols found.  Something funky must have happened. Make sure you have symbols in the watchlist.  Try to restart amp and then the website");

                        // send data stream messages
                        SendSymbolPricesMsg(ref step, force);
                        SendAccountInfoMsg(ref step, force);
                        SendOrdersMsg(ref step, force);
                        SendPositionsMsg(ref step, force);

                        // reset private properties
                        step = "finished";
                        _sendSymbolPricesOnNextInterval = false;
                        _initData = true;
                        _intervalErrorCount = 0;
                    }
                }

                if(IsWebSocketClosed())
                    _intervalErrorCount = 0;
            }
            catch (Exception err)
            {
                _intervalErrorCount++;
                Log.Info(">>> OnInterval() step: " + step + ". " + err.Message);
                CloseSession();
            }
        }

        // Private functions ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Close client connection
        /// </summary>
        /// <returns></returns>
        private bool IsWebSocketClosed()
        {
            if (State == WebSocketState.Closed)
            {
                //Log.Error("WebSocketState Closed. Closing Session.  Make sure QTBridge strategy is running.  If problems persist, close and reopen Quantower.");
                _log.Info(">>> Quantower Bridge has disconnected from the website.");
                CloseSession();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Close session
        /// </summary>
        private void CloseSession()
        {
            try
            {
                Log.Info("Closing session...");
                Sessions.CloseSession(ID);
            }
            catch (Exception ex)
            {
                //Log.Ex(ex, "Closing");
            }
        }

        /// <summary>
        /// Send symbol price information to client
        /// </summary>
        /// <param name="step"></param>
        /// <param name="force"></param>
        private void SendSymbolPricesMsg(ref string step, bool force)
        {
            //if (UseAmpFutures)
            //{
                step = "SubscribedSymbolPricesMsg";
                string msg = _dataStreamMessages.SubscribedSymbolPricesMsg(force || _sendSymbolPricesOnNextInterval);
                if (msg.Length > 14) // hacky, only send if has new data
                    SendMessageToClient(msg, false);
            //}
        }

        /// <summary>
        /// Send account information to client
        /// </summary>
        /// <param name="step"></param>
        /// <param name="force"></param>
        private void SendAccountInfoMsg(ref string step, bool force)
        {
            step = "AccountInfoMsg";
            string msg = _dataStreamMessages.AccountInfoMsg();
            if (force || (!string.IsNullOrEmpty(msg) && msg != _prevAcctMsg)) // dont send if the information is the same as the previously sent message
            {
                _prevAcctMsg = msg;
                SendMessageToClient(msg, false);
            }
        }

        /// <summary>
        /// Send orders information to client
        /// </summary>
        /// <param name="step"></param>
        /// <param name="force"></param>
        private void SendOrdersMsg(ref string step, bool force)
        {
            step = "OrdersMsg";
            string msg = _dataStreamMessages.OrdersMsg();
            if (force || (!string.IsNullOrEmpty(msg) && msg != _prevOrderMsg)) // dont send if the information is the same as the previously sent message
            {
                _prevOrderMsg = msg;
                SendMessageToClient(msg, false);
            }
        }

        /// <summary>
        /// Send positions information to client.
        /// </summary>
        /// <param name="step"></param>
        /// <param name="force"></param>
        private void SendPositionsMsg(ref string step, bool force)
        {
            step = "PositionsInfoMsg";
            string msg = _dataStreamMessages.PositionsInfoMsg();
            if (force || (!string.IsNullOrEmpty(msg) && msg != _prevPositionsMsg)) // dont send if the information is the same as the previously sent message
            {
                _prevPositionsMsg = msg;
                SendMessageToClient(msg, false);
            }
        }
    }
}
