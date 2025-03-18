using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QtBridgeStrategy.Enums
{
    public enum InitResult
    {
        /// <summary>Init not started yet.</summary>
        None,

        /// <summary>Critical error on Init.</summary>
        Error,
        
        /// <summary>Soft error on init.  May resolve itself after a period of time.</summary>
        DelayedRetry,
        
        /// <summary>Success.</summary>
        Success
    }
}
