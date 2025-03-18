using System;
using System.Linq;
using TradingPlatform.BusinessLayer;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using QtBridgeStrategy.Models;

namespace QtBridgeStrategy.Utils
{
    /// <summary>
    /// Utility static class of useful functions
    /// </summary>
    public static class Util
    {
        /// <summary>
        /// Checks if a value is default.  Useful if an object is not nullable.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static bool IsObjDefault<T>(object obj) where T : struct
        {
            if (obj is T value)
            {
                return EqualityComparer<T>.Default.Equals(value, default);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Rounds number to fraction, useful for dealing with prices and ticks
        /// </summary>
        /// <param name="number"></param>
        /// <param name="denominator"></param>
        /// <returns></returns>
        public static double RoundToFraction(double number, double denominator = 1)
        {
            return Math.Round(number * denominator) / denominator;
        }

        /// <summary>
        /// Convert price closer to precision
        /// </summary>
        /// <param name="tickSize"></param>
        /// <param name="price"></param>
        /// <returns></returns>
        public static double ConvertPriceTicked(double tickSize, double price)
        {
            if (price % tickSize != 0)
                return RoundToFraction(price, 1.0 / tickSize);
            return price;
        }

        /// <summary>
        /// Checks to see if string is a Guid, who's format is used internally for order Ids, ogId
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static bool IsGuid(string str)
        {
            return Guid.TryParse(str, out _);
            /*
            int charCount = str.Count();
            int dashCount = str.Count(o => o == '-');
            return charCount == 36 && dashCount == 4; // hacky, if id is guid type, assume is pre order*/
        }

        /// <summary>
        /// Calculates the ticks of a symbol, based on price.
        /// </summary>
        /// <param name="sym"></param>
        /// <param name="side"></param>
        /// <param name="price"></param>
        /// <param name="offsetPrice"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static double CalcTicks(Symbol sym, Side side, double price, double offsetPrice, string type) // takeProfit or stop
        {
            double ticks = 0;
            if (side == Side.Buy)
            {
                if (type == "stop")
                    ticks = sym.CalculateTicks(offsetPrice, price);
                else if (type == "target")
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

        /// <summary>
        /// Calculates the prices, based off ticks of a symbol.
        /// </summary>
        /// <param name="sym"></param>
        /// <param name="side"></param>
        /// <param name="price"></param>
        /// <param name="ticks"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static double CalcPrice(Symbol sym, Side side, double price, double ticks, string type)
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

        /// <summary>
        /// Calculates the OgStop price.
        /// </summary>
        /// <param name="jsonString"></param>
        /// <param name="isShort"></param>
        /// <param name="stopPrice"></param>
        /// <returns></returns>
        public static double CalculateOgStop(string jsonString, bool isShort, double stopPrice)
        {
            double stop = stopPrice;
            if (jsonString != null && jsonString != "")
            {
                OrderInfoAdditional additionalInfo = JsonSerializer.Deserialize<OrderInfoAdditional>(jsonString);
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


        /// <summary>
        /// Helper class to create a file and its directories, if it doesnt exist
        /// </summary>
        /// <param name="filePath"></param>
        public static void CreateFileIfNotExist(string filePath)
        {
            string directoryPath = Path.GetDirectoryName(filePath);

            // create directory if not exist
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
                Console.WriteLine($"Directory created: {directoryPath}");
            }

            // create file if not exist
            if (!File.Exists(filePath))
            {
                using (FileStream fs = File.Create(filePath))
                {
                    Console.WriteLine($"File created: {filePath}");
                }
            }
        }
    }
}
