using System.Collections.Generic;
using static QtBridgeStrategy.Utils.Util;
using static QtBridgeStrategy.Constants.Constants;
using QtBridgeStrategy.Models;
using QtBridgeStrategy.Data;

namespace QtBridgeStrategy.Services.Brokers
{
    /// <summary>
    /// Manages additional information about an Order than must be tracked internally, and can't be saved onto a Quantower Order
    /// </summary>
    public class OrderAdditionalInfoMgr : QtBridgeStrategy
    {
        private string _filepath;
        private DataManager _dataManager = new DataManager();

        public List<OrderAdditionalInfoExt> infos { get; private set; }

        /// <summary>
        /// Constructor loads the file (or creates if not exist)
        /// </summary>
        public OrderAdditionalInfoMgr()
        {
            infos = new List<OrderAdditionalInfoExt>();
            _filepath = GetAdditionalInfoFilePath();
            CreateFileIfNotExist(_filepath); // create folder if not exist.  We'll just assume the file exists from here on out.
            Load();
        }


        // Public Methods ----------------------------------------
        /// <summary>
        /// Clear and saves all objects
        /// </summary>
        public void Clear()
        {
            infos.Clear();
            Save();
        }

        /// <summary>
        /// Remove specified object from the manager
        /// </summary>
        /// <param name="ogId"></param>
        public void Remove(string ogId)
        {
            infos.RemoveAll(s => s.ogId == ogId);
            Save();
        }


        /// <summary>
        /// Updates and saves specified object
        /// </summary>
        /// <param name="info"></param>
        public void Update(OrderAdditionalInfoExt info)
        {
            if (!IsObjDefault<OrderAdditionalInfoExt>(info)) // only update if object is valid
            {
                infos.RemoveAll(s => s.ogId == info.ogId);
                infos.Add(info);
                Save();
            }
        }

        /// <summary>
        /// Gets additional info for the specified id
        /// </summary>
        /// <param name="ogId"></param>
        /// <returns></returns>
        public OrderAdditionalInfoExt Get(string ogId)
        {
            return infos.Find(o => o.ogId == ogId);
        }

        /// <summary>
        /// Saves to file
        /// </summary>
        /// <returns></returns>
        public bool Save()
        {
            return _dataManager.SaveToFile(infos, _filepath);
        }


        // Private Methods --------------------------------------------------
        /// <summary>
        /// Loads from file
        /// </summary>
        private void Load()
        {
            infos.Clear();
            infos = _dataManager.LoadFromFile<List<OrderAdditionalInfoExt>>(_filepath);
        }
    }
}
