using System;
using System.Text.Json;
using System.IO;
using TradingPlatform.BusinessLayer;

namespace QtBridgeStrategy.Data
{
    public class DataManager : QtBridgeStrategy
    {
        /// <summary>
        /// Save Objects to File in Json Format
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="filePath"></param>
        /// <returns>Returns operation success</returns>
        public bool SaveToFile<T>(T data, string filePath)
        {
            try
            {
                string jsonData = JsonSerializer.Serialize(data);
                File.WriteAllText(filePath, jsonData);
                return true;
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
            }
            return false;
        }

        /// <summary>
        ///  Load Objects from File in Json Format
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filePath"></param>
        /// <returns>Deserialized objects from file</returns>
        public T LoadFromFile<T>(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string jsonData = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<T>(jsonData);
                }
                else
                {
                    Log.Error("Loading file not found: " + filePath);
                    throw new FileNotFoundException("File not found", filePath);
                }
            }
            catch (Exception ex)
            {
                Log.Ex(ex);
                return default;
            }
        }
    }
}
