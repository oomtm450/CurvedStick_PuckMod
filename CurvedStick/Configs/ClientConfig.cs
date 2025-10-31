using Newtonsoft.Json;
using oomtm450PuckMod_CurvedStick.SystemFunc;
using System.IO;

namespace oomtm450PuckMod_CurvedStick.Configs {
    /// <summary>
    /// Class containing the configuration from oomtm450_curvedstick_clientconfig.json used for this mod.
    /// </summary>
    public class ClientConfig : IConfig {
        #region Constants
        /// <summary>
        /// Const string, name used when sending the config data to the client.
        /// </summary>
        public const string CONFIG_DATA_NAME = Constants.MOD_NAME + "_clientconfig.json";

        public const int HEEL_MAX = 100;
        public const int MIDDLE_MAX = 100;
        public const int TOE_MAX = 100;
        public const int TIP_MAX = 500;
        #endregion

        #region Properties
        /// <summary>
        /// Bool, true if the info logs must be printed.
        /// </summary>
        public bool LogInfo { get; set; } = true;

        /// <summary>
        /// Int, curve of the heel.
        /// </summary>
        public int HeelCurve { get; set; } = 0;

        /// <summary>
        /// Float, curve of the heel for the rotation.
        /// </summary>
        [JsonIgnore]
        public float HeelCurveF => ((float)HeelCurve) / 1000f;

        /// <summary>
        /// Int, curve of the middle.
        /// </summary>
        public int MiddleCurve { get; set; } = 0;

        /// <summary>
        /// Float, curve of the middle for the rotation.
        /// </summary>
        [JsonIgnore]
        public float MiddleCurveF => ((float)MiddleCurve) / 1000f;

        /// <summary>
        /// Int, curve of the toe.
        /// </summary>
        public int ToeCurve { get; set; } = 0;

        /// <summary>
        /// Float, curve of the toe for the rotation.
        /// </summary>
        [JsonIgnore]
        public float ToeCurveF => ((float)ToeCurve) / 1000f;

        /// <summary>
        /// Int, curve of the tip.
        /// </summary>
        public int TipCurve { get; set; } = 0;

        /// <summary>
        /// Float, curve of the tip for the rotation.
        /// </summary>
        [JsonIgnore]
        public float TipCurveF => ((float)TipCurve) / 1000f;
        #endregion

        /// <summary>
        /// Function that serialize the ClientConfig object.
        /// </summary>
        /// <returns>String, serialized ClientConfig.</returns>
        public override string ToString() {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        /// <summary>
        /// Function that unserialize a ClientConfig.
        /// </summary>
        /// <param name="json">String, JSON that is the serialized ClientConfig.</param>
        /// <returns>ClientConfig, unserialized ClientConfig.</returns>
        internal static ClientConfig SetConfig(string json) {
            return JsonConvert.DeserializeObject<ClientConfig>(json);
        }

        /// <summary>
        /// Function that reads the config file for the mod and create a ClientConfig object with it.
        /// Also creates the file with the default values, if it doesn't exists.
        /// </summary>
        /// <returns>ClientConfig, parsed config.</returns>
        internal static ClientConfig ReadConfig() {
            ClientConfig config = new ClientConfig();

            string rootPath = Path.GetFullPath(".");
            string configPath = Path.Combine(rootPath, CONFIG_DATA_NAME);
            if (File.Exists(configPath)) {
                string configFileContent = File.ReadAllText(configPath);
                config = SetConfig(configFileContent);
            }

            config.CheckCurveValues();

            File.WriteAllText(configPath, config.ToString());

            Logging.Log($"Writing client config : {config}", config);

            return config;
        }

        internal void SaveConfig() {
            string rootPath = Path.GetFullPath(".");
            string configPath = Path.Combine(rootPath, CONFIG_DATA_NAME);
            File.WriteAllText(configPath, this.ToString());
        }

        internal void CheckCurveValues() {
            if (HeelCurve > HEEL_MAX)
                HeelCurve = HEEL_MAX;
            else if (HeelCurve < 0)
                HeelCurve = 0;

            if (MiddleCurve > MIDDLE_MAX)
                MiddleCurve = MIDDLE_MAX;
            else if (MiddleCurve < 0)
                MiddleCurve = 0;

            if (ToeCurve > TOE_MAX)
                ToeCurve = TOE_MAX;
            else if (ToeCurve < 0)
                ToeCurve = 0;

            if (TipCurve > TIP_MAX)
                TipCurve = TIP_MAX;
            else if (TipCurve < 0)
                TipCurve = 0;
        }
    }
}
