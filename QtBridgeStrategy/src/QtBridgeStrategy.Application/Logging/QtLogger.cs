using System;
using System.Diagnostics;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace QtBridgeStrategy.Logging
{
    /// <summary>
    /// Helper class to logs directly to the Quantower log window
    /// </summary>
    public class Logger : Strategy
    {
        QtBridgeStrategy BaseStrategy;

        /// <summary>
        /// Initializes an instance of Logger.  Logging functions can only be called from the Strategy object, otherwise the application will crash.
        /// </summary>
        /// <param name="baseStrategy">Quantower base strategy object</param>
        public Logger(QtBridgeStrategy baseStrategy)
        {
            BaseStrategy = baseStrategy;
        }

        /// <summary>
        /// Logs exception
        /// </summary>
        /// <param name="ex">Exception</param>
        public void Ex(Exception ex)
        {
            Ex(ex, "");

        }

        /// <summary>
        /// Logs exception with additional information 
        /// </summary>
        /// <param name="ex">Exception</param>
        public void Ex(Exception ex, string extendedInfo, int levels = 6)
        {
            string message = ex.Message;
            string innerMessage = "";
            string trace = "";
            for (int i = 0; i < levels; i++)
                trace += new StackTrace().GetFrame(i).GetMethod().Name + ".";
            trace = trace.Remove(trace.Length - 1);
            var innerEx = ex.InnerException;
            if (innerEx != null && innerEx.Message != null)
                innerMessage = "  >>>  " + innerEx.Message;
            if (extendedInfo.Count() > 0)
                BaseStrategy.LogError($"{trace}() {extendedInfo}: {message}{innerMessage}");
            else
                BaseStrategy.LogError($"{trace}() {message}{innerMessage}");
        }

        /// <summary>
        /// Log an order action with standard formatting
        /// </summary>
        /// <param name="preMsg">Header</param>
        /// <param name="action">Order action</param>
        /// <param name="id"></param>
        /// <param name="symbolName"></param>
        /// <param name="postMsg">Additional information at the end of message</param>
        public void Order(string preMsg, string action, string id, string symbolName, string postMsg = "")
        {
            string comma = preMsg.Length > 0 ? ", " : "";
            BaseStrategy.LogTrading($"{preMsg}{comma}{action} ({id}) {symbolName}: {postMsg}");
        }

        /// <summary>
        /// Log an order error. This is logged as an Error in Qt, so it stands out in the log view.
        /// </summary>
        /// <param name="preMsg">header</param>
        /// <param name="action">order action</param>
        /// <param name="id"></param>
        /// <param name="symbolName"></param>
        /// <param name="postMsg">additional info</param>
        public void OrderError(string preMsg, string action, string id, string symbolName, string postMsg = "")
        {
            string comma = preMsg.Length > 0 ? "," : "";
            BaseStrategy.LogError($"{preMsg}{comma}{action} ({id}) {symbolName}: {postMsg}");
        }

        /// <summary>
        /// Logs message as a Qt Error type
        /// </summary>
        /// <param name="message"></param>
        public void Error(string message)
        {
            BaseStrategy.LogError(message);
        }

        /// <summary>
        /// Logs message as a Qt Trade type
        /// </summary>
        /// <param name="message"></param>
        public void Trade(string message)
        {
            BaseStrategy.LogTrading(message);
        }

        /// <summary>
        /// Logs message as a Qt Info type
        /// </summary>
        /// <param name="message"></param>
        public void Info(string message)
        {
            BaseStrategy.LogInfo(message);
        }
    }
}