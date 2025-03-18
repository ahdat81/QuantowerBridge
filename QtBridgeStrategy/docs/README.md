Quantower Bridge that connects to the PriceActionGroup website.  
This will allow PAG to access brokers from Quantower, providing functions such as:

  * Retrieving info such as Account, Orders, Positions, and Live Prices
  * Place and modify orders directly from PAG
  * Allow placing internal (non-live) pre-orders that are turned into live orders when triggered.  This allows placing limit orders 
	without affecting account margin requirements.
  * Handle order management rules such as triggers to move stops, cancel prices, etc...
  * Auto adjust / fix orders and positions to make expected order rules (every trade must have a stop, stops must have associated 
	target orders, matching order qty between all orders)
  * Failsafe to prevent potentially catastrophic scenarios
  * Handle EOD scenarios, for trading futures where daily margin contracts can differ, and must be closed.

Currently only supports AmpFutures, Ameritrade has been depricated.

Installation
------------
	1. Add package to \Quantower\Settings\Scripts\Strategies
	2. Configure settings as necessary
	3. Run strategy
	4. Replace the watchlist with 'allsymbols'.  This must be done daily, futures contracts rollover and change, and
		Quantower needs symbols to be added to the watchlist to retreive bid/ask prices
	5. Log into PriceActionGroup.  It should automatically connect.

Remote Connection
-----------------
Although this strategy is designed to be run locally on the same machine that using PAG, it is possible to run this remotely on 
as seperate machine.  

	1. On a remote 'server', install Quantower. 
	2. Follow the installation instructions above.
	3. Set a static IP on the server.
	4. (optional) Router port forwarding to expose the port set in the Qt strategy's setting.  Default is 3300.
	5. Change the Server Ip setting in Quantower to the server's ip address.
	6. On the client machine (where you are logging onto the PAG website), set system routing rules approprietly.  
		On windows, open the command prompt with elevated admin privledges.  Then apply the necessary rules.
			a. netsh interface portproxy show all (see all rules)
			b. netsh interface portproxy add v4tov4 listenaddress=127.0.0.1 listenport=3300 connectaddress=<internal ip> connectport=3300 (connect to a local machine on the network)
			c. netsh interface portproxy add v4tov4 listenaddress=127.0.0.1 listenport=3300 connectaddress=<public ip> connectport=3300 (connect from outside the local network.  Step 4)
			d. netsh interface portproxy delete v4tov4 listenaddress=127.0.0.1 listenport=3300 (remote the rule)

For Future Reference
--------------------
Quicks discovered for future reference, for a possible future redesign
	* Quantower does not update an order without changing price.  This means the comment field (where we track internal information) changes won't apply.
	* Once a limit order is executed, the Comments field can no longer be modified.  This is the reason for the AdditionalInfosExt class.
	* To get bid/ask price from Quantower for a particular field programatically, the symbol must be in the watchlist (or chart opened).
	* To Log to the Quantower Log Window, the Log methods must be called from the original base class object (and not from extending the Strategy).  
