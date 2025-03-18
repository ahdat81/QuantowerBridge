using System;
using System.IO;

namespace QtBridgeStrategy.Constants
{
    public static class Constants
    {
        public static TimeZoneInfo EstTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); 
        public const int CheckStopFailsafeSymbolMaxWait = 100;
        public const int CheckStopFailsafeSymbolWaitDelay = 50;
        public const int CheckStopFailsafeSymbolProcessWaitCount = 100;
        public const string DefaultQtWatchlistName = "allsymbols";
        public const string BaseFilePath = "C://qt//";
        public const string OrderAdditionInfoFileName = "additionalinfo.txt";
        public const string PreOrdersFileName = "preorders.txt";
        public static string GetAdditionalInfoFilePath() => Path.Combine(BaseFilePath, OrderAdditionInfoFileName);
        public static string GetPreOrdersFilePath() => Path.Combine(BaseFilePath, PreOrdersFileName);
    }
}
