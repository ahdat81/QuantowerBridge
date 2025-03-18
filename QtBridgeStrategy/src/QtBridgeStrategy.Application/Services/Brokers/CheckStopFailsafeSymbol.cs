using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;
using static QtBridgeStrategy.Constants.Constants;
namespace QtBridgeStrategy.Services.Brokers
{
    public class CheckStopFailsafeSymbol : QtBridgeStrategy
    {
        /// <summary>
        /// Class to help check for critical errors in the orders to prevent unexpected or catastrophic failures
        /// Makes sure the orders follow our order management rules, such as:
        ///   Every order must contain a stop
        ///   Every stop must have an associated target order
        ///   Qty must even out to 0 between all entry, stop, and tg orders
        /// Errors can happen due to order execution failures, which are unlikely but possible
        /// On uncorrectable or risky fixes, it will abort and flatten all trades for the symbol.
        /// 
        /// Once an error is detected, it will let some time elapse to see if it corrects itself.
        /// Which can happen as orders takes time to execute.
        /// </summary>

        ///<summary>Current failsafe error state</summary>
        private enum ErrorState
        {
            /// <summary> no error </summary>
            None,

            /// <summary> error found, but not yet critical </summary>
            Found,

            /// <summary> critical time reached, execute failsafe </summary>
            End
        }

        private OrderMgr _orderMgr;
        private bool _processing;
        private bool _tpOrderMismatch;
        private int _maxWait;
        private int _waitDelay;
        private int _processWaitCount;

        public Symbol Symbol { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="orderMgr"></param>
        public CheckStopFailsafeSymbol(Symbol symbol, OrderMgr orderMgr)
        {
            _maxWait = CheckStopFailsafeSymbolMaxWait;
            _waitDelay = CheckStopFailsafeSymbolWaitDelay;
            _processWaitCount = CheckStopFailsafeSymbolProcessWaitCount;

            Symbol = symbol;
            _orderMgr = orderMgr;
        }

        /// <summary>
        /// Stop Checking for errors
        /// </summary>
        public void StopCheck()
        {
            _processing = false;
        }

        /// <summary>
        /// Run check for errors. Should be run on an interval.
        /// </summary>
        async public void RunCheck()
        {
            try
            {
                if (_processing) // TODO: need a lock
                    return;
                _processing = true;

                for (int i = 0; i <= _maxWait; i++)
                {
                    await Task.Delay(_waitDelay); // delay check, wait for qt to update account information

                    ErrorState errorState = GetErrorState(i, _maxWait);
                    // Check and process different types of conflicts with Positions and Orders
                    if (await NoPosButHasStopOrTgOrdersConflict(_orderMgr, Symbol, errorState) || 
                        await HasPosButNoStopOrdersConflict(_orderMgr, Symbol, errorState) ||
                        await StopOrderQtyMismatchedWithPositionsConflict(_orderMgr, Symbol, errorState) ||
                        await HasMoreTgThanStopOrdersConflict(_orderMgr, Symbol, errorState) ||
                        HasLessTgThanStopOrdersConflict(_orderMgr, Symbol, errorState))
                    {
                        continue; // if conflict found then no need to check for additional conflicts in this run, just continue checking for errors within the time frame
                    }

                    if(errorState == ErrorState.Found) 
                        Log.Info($">>> order error resolved. {Symbol.Name}, {i}");

                    break; // no more errors, break out of loop to check for errors
                }
                StopCheck();
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
                StopCheck();
            }
        }

        // Private Functions ------------------------------------------------------------------

        /// <summary>
        /// Check if there are no positions but has a stop or target order.  If there are no positions, there should not be any live stop and target orders.
        /// </summary>
        /// <param name="orderMgr"></param>
        /// <param name="symbol"></param>
        /// <param name="errorState"></param>
        /// <returns></returns>
        async private Task<bool> NoPosButHasStopOrTgOrdersConflict(OrderMgr orderMgr, Symbol symbol, ErrorState errorState)
        {
            bool noPosButHasStopOrTgOrders = !orderMgr.HasPosition(symbol) && (orderMgr.HasStopOrders(symbol) || orderMgr.HasTgOrders(symbol));
            
            if (!noPosButHasStopOrTgOrders) // no conflict with orders
                return false;

            if (errorState == ErrorState.None)
            {
                Log.Info($">>> No positions, but stop and/or tg orders found. awaiting... {symbol.Name}");
            }
            else if (errorState == ErrorState.End)
            {
                Log.Info($">>> No positions, but stop and/or tg orders found. Clear all open orders. {symbol.Name}");
                await CloseOrdersAndPositions(orderMgr.GetStopOrders(symbol), orderMgr.GetTgOrders(symbol), null, false);
            }
            return true;
        }

        /// <summary>
        /// Check if there are positions but no stop orders.  All open orders should have associated stop orders.
        /// </summary>
        /// <param name="orderMgr"></param>
        /// <param name="symbol"></param>
        /// <param name="errorState"></param>
        /// <returns></returns>
        async private Task<bool> HasPosButNoStopOrdersConflict(OrderMgr orderMgr, Symbol symbol, ErrorState errorState)
        {
            bool hasPosButNoStopOrders = orderMgr.HasPosition(symbol) && !orderMgr.HasStopOrders(symbol);
            if(!hasPosButNoStopOrders) // no conflict with orders
                return false;

            if (errorState == ErrorState.None)
            {
                Log.Info($">>> Has positions, but missing stop orders. awaiting... {symbol.Name}");
            }
            else if (errorState == ErrorState.End)
            {
                Position position = orderMgr.GetPosition(symbol);   
                Log.Info($">>> Has positions, but missing stop orders. Closing positions to be safe. {position.Symbol}, pos: {position.Quantity}");
                await CloseOrdersAndPositions(orderMgr.GetStopOrders(symbol), orderMgr.GetTgOrders(symbol), position, false);

            }
            return true;
        }

        /// <summary>
        /// Check if there are more target than stop orders. These should match
        /// </summary>
        /// <param name="orderMgr"></param>
        /// <param name="symbol"></param>
        /// <param name="errorState"></param>
        /// <returns></returns>
        async private Task<bool> HasMoreTgThanStopOrdersConflict(OrderMgr orderMgr, Symbol symbol, ErrorState errorState)
        {
            var stopOrders = orderMgr.GetStopOrders(symbol);
            var tgOrders = orderMgr.GetTgOrders(symbol);
            bool moreTgThanStopOrders = tgOrders.Count() > stopOrders.Count();
            
            if (!moreTgThanStopOrders) // no conflict with orders
                return false;

            if (errorState == ErrorState.None)
                Log.Info($">>> more tpOrders than stopOrders. awaiting... {symbol.Name}");
            else if (errorState == ErrorState.End)
                await CloseTgOrdersWithoutMatchingStopOrders(symbol, stopOrders, tgOrders, false);
            return true;
        }

        /// <summary>
        /// Check if there are less target than stop orders. These should match
        /// </summary>
        /// <param name="orderMgr"></param>
        /// <param name="symbol"></param>
        /// <param name="errorState"></param>
        private bool HasLessTgThanStopOrdersConflict(OrderMgr orderMgr, Symbol symbol, ErrorState errorState)
        {
            var stopOrders = orderMgr.GetStopOrders(symbol);
            var tgOrders = orderMgr.GetTgOrders(symbol);

            bool lessTgThanStopOrders = tgOrders.Count() < stopOrders.Count();
            if(!lessTgThanStopOrders)
                return false;

            if (errorState == ErrorState.None)
                Log.Info($">>> less tpOrders than stopOrders. awaiting... {symbol.Name}");
            else if (errorState == ErrorState.End)
                Log.Error($">>> there is a qty mismatch of stop orders and tg orders.  fix as necessary.  {symbol.Name}. ");
            return true;

            // We do not try to handle this one, as creating additional tp orders can be very dangerous and blow and account
            // We need to alert the user to handle this conflict manually.

            //foreach (var order in stopOrders)
            //    await OrderMgr.CreateMissingTpOrder(order);
        }

        /// <summary>
        /// Check if there are qty mismatches between positions and stop orders
        /// </summary>
        /// <param name="orderMgr"></param>
        /// <param name="symbol"></param>
        /// <param name="errorState"></param>
        /// <returns></returns>
        async private Task<bool> StopOrderQtyMismatchedWithPositionsConflict(OrderMgr orderMgr, Symbol symbol, ErrorState errorState)
        {
            Position position = orderMgr.GetPosition(symbol);
            double stopQtyCount = orderMgr.GetStopQtyCount(symbol);
            bool stopOrderQtyMismatchedWithPos = orderMgr.HasPosition(symbol) && orderMgr.HasStopOrders(symbol) && (stopQtyCount != position.Quantity);
            
            if (!stopOrderQtyMismatchedWithPos) // no conflict
                return false;

            if (errorState == ErrorState.None)
            {
                Log.Info($">>> stop order qty mismatched with position qty. awaiting... {symbol.Name}");
            }
            else if(errorState == ErrorState.End)
            {
                var stopOrders = orderMgr.GetStopOrders(symbol);
                var tgOrders = orderMgr.GetTgOrders(symbol);
                if (await AdjustPosToMatchOrders(stopQtyCount, position, tgOrders, stopOrders)) { } // adjust mismatch
                else if (await CloseOrdersToMatchPos(stopQtyCount, position, tgOrders, stopOrders)) { } // adjust mismatch
            }
            return true;
        }

        /// <summary>
        /// Attempt to adjust positions to match order qty.
        /// </summary>
        /// <param name="stopQtyCount"></param>
        /// <param name="position"></param>
        /// <param name="tgOrders"></param>
        /// <param name="stopOrders"></param>
        /// <returns></returns>
        async private Task<bool> AdjustPosToMatchOrders(double stopQtyCount, Position position, IEnumerable<Order> tgOrders, IEnumerable<Order> stopOrders)
        {
            if(position.Quantity > stopQtyCount)
            {
                double diff = position.Quantity - stopQtyCount;
                Log.Info($">>> qty mismatch found, adjusting pos to match orders.  {Symbol.Name}.  pos: {position.Quantity}, order qty: {stopQtyCount}, diff: {diff}");
                if (await _orderMgr.ClosePositionQty(position, diff, _processWaitCount * 2, _waitDelay))
                {
                    await CloseTgOrdersWithoutMatchingStopOrders(Symbol, stopOrders, tgOrders, true);
                }
                else // failsafe  something when wrong, close all for symbol
                {
                    Log.Error($">>> Very bad, market order failed.  Nuking everything.  {Symbol.Name}.  qty: {diff}");
                    await CloseOrdersAndPositions(stopOrders, tgOrders, position, false);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Attempt to close orders to match positions.
        /// </summary>
        /// <param name="stopQtyCount"></param>
        /// <param name="position"></param>
        /// <param name="tgOrders"></param>
        /// <param name="stopOrders"></param>
        /// <returns></returns>
        async private Task<bool> CloseOrdersToMatchPos(double stopQtyCount, Position position, IEnumerable<Order> tgOrders, IEnumerable<Order> stopOrders)
        {
            if (stopQtyCount > position.Quantity) // pos qty < stop qty, close some stop orders
            {
                Log.Info($">>> position qty mismatch direction.  Closing associated orders to match qty.  {Symbol.Name}.  pos: {position.Quantity}, order qty: {stopQtyCount}");
                double diff = stopQtyCount - position.Quantity;
                var stops = stopOrders.Where(o => o.RemainingQuantity == diff);
                if (!stops.Any()) // no stops found.  to complicated to go further, just close all positions and live trades
                {
                    // TODO create matching tg order, to group id
                    Log.Error($">>> no stop orders with matching qty found.  just going to close all positions and live orders to be safe.  {Symbol.Name}.");
                    await CloseOrdersAndPositions(stopOrders, tgOrders, position, false);
                }
                else
                {
                    await CloseStopOrdersWithMatchingTargetOrders(Symbol, stopOrders, tgOrders, false);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Attempt to close stop orders with matching target orders
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="stopOrders"></param>
        /// <param name="tgOrders"></param>
        /// <param name="ignorePersisentOrders"></param>
        /// <returns></returns>
        async private Task<bool> CloseStopOrdersWithMatchingTargetOrders(Symbol symbol, IEnumerable<Order> stopOrders, IEnumerable<Order> tgOrders, bool ignorePersisentOrders)
        {
            // TODO we can optimize this
            var stopWithTargetOrders = stopOrders.FirstOrDefault(stop => tgOrders.Any(o => o.GroupId == stop.GroupId));
            if (stopWithTargetOrders is not null)
            {
                Log.Error($">>> closing stop orders with matching target orders.  {symbol.Name}.");
                var tgOrder = tgOrders.First(o => o.GroupId == stopWithTargetOrders.GroupId);
                await CloseOrders(new List<string> { stopWithTargetOrders.Id, tgOrder.Id }, ignorePersisentOrders, _processWaitCount);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Attempt to close target orders without matching stop orders.
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="stopOrders"></param>
        /// <param name="tgOrders"></param>
        /// <param name="ignorePersisentOrders"></param>
        /// <returns></returns>
        async private Task<bool> CloseTgOrdersWithoutMatchingStopOrders(Symbol symbol, IEnumerable<Order> stopOrders, IEnumerable<Order> tgOrders, bool ignorePersisentOrders)
        {
            /*
            List<string> ids = new List<string>();
            foreach (var order in tgOrders) // close unassociated tg orders
            {
                if (!stopOrders.Where(o => o.GroupId == order.GroupId).Any())
                    ids.Add(order.Id);
            }
            
            foreach (var order in tgOrders) // close unassociated tg orders
            {
                if (!stopOrders.Where(o => o.GroupId == order.GroupId).Any())
                {
                    Log.Error($">>> closing tp order without matching stop order.  {symbol.Name}. ");
                    await CloseOrders(new List<string> { order.Id }, ignorePersisentOrders, _processWaitCount);
                }
            }
            */
            var ids = tgOrders
                .Where(order => !stopOrders.Any(o => o.GroupId == order.GroupId))
                .Select(order => order.Id)
                .ToList();

            if (ids.Any())
            {
                Log.Error($">>> closing tp order without matching stop order.  {symbol.Name}. ");
                await CloseOrders(ids, ignorePersisentOrders, _processWaitCount);
            }

            return true;
        }

        /// <summary>
        /// Close orders
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="ignorePersistentOrder"></param>
        /// <param name="processWaitCount"></param>
        /// <returns></returns>
        async private Task<bool> CloseOrders(List<string> ids, bool ignorePersistentOrder, int processWaitCount)
        {
            foreach (var id in ids)
                _orderMgr.CloseOrderById(id, ignorePersistentOrder);

            return await AwaitCheckOrdersWorking(ids, ignorePersistentOrder, processWaitCount); // Wait to see if orders closed
        }

        /// <summary>
        /// Close positions
        /// </summary>
        /// <param name="position"></param>
        /// <param name="processWaitCount"></param>
        /// <returns></returns>
        async private Task<bool> ClosePosition(Position position, int processWaitCount)
        {
            if (position is null) return true; // Position object might be null if there are no positions
            var res = position.Close(); // Close positions in Qt

            return await AwaitPositionsClosed(position, processWaitCount); // Check to see if positions were closed
        }

        /// <summary>
        /// Close orders and positions
        /// </summary>
        /// <param name="stopOrders"></param>
        /// <param name="tpOrders"></param>
        /// <param name="pos"></param>
        /// <param name="ignorePersistentOrder"></param>
        /// <returns></returns>
        async private Task<bool> CloseOrdersAndPositions(IEnumerable<Order> stopOrders, IEnumerable<Order> tpOrders, Position pos, bool ignorePersistentOrder)
        {
            // TODO we can optimize this
            List<string> processIds = new List<string>();
            if (stopOrders is not null) 
                processIds.AddRange(stopOrders.Select(o => o.Id)); // get all stop order ids
            if (tpOrders is not null) 
                processIds.AddRange(tpOrders.Select(o => o.Id)); // get all target order ids

            await CloseOrders(processIds, ignorePersistentOrder, _processWaitCount); // close all orders
            await ClosePosition(pos, _processWaitCount);  // close all positions
            return true;
        }

        /// <summary>
        /// Wait for a period of time to check on working orders
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="ignorePersistentOrder"></param>
        /// <param name="processWaitCount"></param>
        /// <returns></returns>
        async private Task<bool> AwaitCheckOrdersWorking(List<string> ids, bool ignorePersistentOrder, int processWaitCount)
        {
            for (int i = 0; i <= processWaitCount; i++)
            {
                if (_orderMgr.AreOrdersWorking(ids))
                    await Task.Delay(_waitDelay);
                else
                    return true;
            }
            Log.Info($">>> Closing orders failed.  Womp womp. {Symbol.Name}");
            return false;
        }

        /// <summary>
        /// Wait for a period of time to check if positions were closed
        /// </summary>
        /// <param name="position"></param>
        /// <param name="processWaitCount"></param>
        /// <returns></returns>
        async private Task<bool> AwaitPositionsClosed(Position position, int processWaitCount)
        {
            for (int i = 0; i <= processWaitCount; i++)
            {
                if (position.Quantity != 0)
                    await Task.Delay(_waitDelay);
                else
                    return true;
            }
            Log.Info($">>> Closing position failed.  Womp womp. {Symbol.Name}");
            return false;
        }

        /// <summary>
        /// Get error state based on error count
        /// </summary>
        /// <param name="i"></param>
        /// <param name="maxCount"></param>
        /// <returns></returns>
        private ErrorState GetErrorState(int i, int maxCount)
        {
            if (i == 0)
                return ErrorState.None;
            else if (i >= maxCount)
                return ErrorState.End;
            else
                return ErrorState.Found;
        }
    }
}
