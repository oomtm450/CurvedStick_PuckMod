using Newtonsoft.Json;
using oomtm450PuckMod_CurvedStick.SystemFunc;
using System.IO;

namespace oomtm450PuckMod_CurvedStick.Configs {
    /// <summary>
    /// Class containing the configuration from oomtm450_curvedstick_serverconfig.json used for this mod.
    /// </summary>
    public class ServerConfig : IConfig {
        #region Constants
        /// <summary>
        /// Const string, name used when sending the config data to the client.
        /// </summary>
        public const string CONFIG_DATA_NAME = Constants.MOD_NAME + "_serverconfig.json";
        #endregion

        #region Properties
        /// <summary>
        /// Bool, true if the info logs must be printed.
        /// </summary>
        public bool LogInfo { get; set; } = true;
        #endregion

        #region Methods/Functions
        /// <summary>
        /// Function that serialize the config object.
        /// </summary>
        /// <returns>String, serialized config.</returns>
        public override string ToString() {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        /// <summary>
        /// Function that unserialize a ServerConfig.
        /// </summary>
        /// <param name="json">String, JSON that is the serialized ServerConfig.</param>
        /// <returns>ServerConfig, unserialized ServerConfig.</returns>
        internal static ServerConfig SetConfig(string json) {
            return JsonConvert.DeserializeObject<ServerConfig>(json);
        }

        /// <summary>
        /// Function that reads the config file for the mod and create a ServerConfig object with it.
        /// Also creates the file with the default values, if it doesn't exists.
        /// </summary>
        /// <returns>ServerConfig, parsed config.</returns>
        internal static ServerConfig ReadConfig() {
            ServerConfig config = new ServerConfig();

            string rootPath = Path.GetFullPath(".");
            string configPath = Path.Combine(rootPath, CONFIG_DATA_NAME);
            if (File.Exists(configPath)) {
                string configFileContent = File.ReadAllText(configPath);
                config = SetConfig(configFileContent);
            }

            File.WriteAllText(configPath, config.ToString());

            Logging.Log($"Writing server config : {config}", config);

            return config;
        }
        #endregion
    }
}
