using Newtonsoft.Json;
using oomtm450PuckMod_CurvedStick.SystemFunc;
using System;
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
        private const string CONFIG_DATA_NAME = Constants.MOD_NAME + "_clientconfig.json";

        /// <summary>
        /// String, old full path for the config folder.
        /// </summary>
        [JsonIgnore]
        private static readonly string OLD_CONFIG_FOLDER_PATH = Path.Combine(Path.GetFullPath("."));

        /// <summary>
        /// String, old full path for the config file.
        /// </summary>
        [JsonIgnore]
        private static readonly string OLD_CONFIG_PATH = Path.Combine(OLD_CONFIG_FOLDER_PATH, CONFIG_DATA_NAME);

        /// <summary>
        /// String, full path for the config folder.
        /// </summary>
        [JsonIgnore]
        private static readonly string CONFIG_FOLDER_PATH = Path.Combine(Path.GetFullPath("."), "config");

        /// <summary>
        /// String, full path for the config file.
        /// </summary>
        [JsonIgnore]
        private static readonly string CONFIG_PATH = Path.Combine(CONFIG_FOLDER_PATH, CONFIG_DATA_NAME);

        public const int HEEL_MAX = 100;
        public const int HEEL_MIN = HEEL_MAX * -1;

        public const int MIDDLE_MAX = 100;
        public const int MIDDLE_MIN = MIDDLE_MAX * -1;

        public const int TOE_MAX = 100;
        public const int TOE_MIN = TOE_MAX * -1;

        public const int TIP_MAX = 400;
        public const int TIP_MIN = TIP_MAX * -1;
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

        /// <summary>
        /// Bool, true if the stick has no curve.
        /// </summary>
        [JsonIgnore]
        public bool NoCurve => HeelCurve == 0 && MiddleCurve == 0 && ToeCurve == 0 && TipCurve == 0;
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
            ClientConfig config;

            try {
                if (!Directory.Exists(CONFIG_FOLDER_PATH))
                    Directory.CreateDirectory(CONFIG_FOLDER_PATH);

                if (File.Exists(CONFIG_PATH)) {
                    string configFileContent = File.ReadAllText(CONFIG_PATH);
                    config = SetConfig(configFileContent);
                    Logging.Log($"Client config read.", config, true);
                }
                else if (File.Exists(OLD_CONFIG_PATH)) {
                    string configFileContent = File.ReadAllText(OLD_CONFIG_PATH);
                    config = SetConfig(configFileContent);
                    Logging.Log($"Old client config read.", config, true);
                }
                else
                    config = new ClientConfig();

                config.Save();

                return config;
            }
            catch (Exception ex) {
                Logging.LogError($"Can't read the server config file/folder. (Permission error ?)\n{ex}");
            }

            return new ClientConfig();
        }

        internal void Save() {
            if (string.IsNullOrEmpty(CONFIG_PATH)) {
                Logging.LogError($"Can't write the client config file. ({nameof(CONFIG_PATH)} null or empty)");
                return;
            }

            try {
                if (!Directory.Exists(CONFIG_FOLDER_PATH))
                    Directory.CreateDirectory(CONFIG_FOLDER_PATH);

                File.WriteAllText(CONFIG_PATH, ToString());
            }
            catch (Exception ex) {
                Logging.LogError($"Can't write the client config file. (Permission error ?)\n{ex}");
            }

            Logging.Log($"Wrote client config : {ToString()}", this, true);
        }

        internal void CheckCurveValues() {
            if (HeelCurve > HEEL_MAX)
                HeelCurve = HEEL_MAX;
            else if (HeelCurve < HEEL_MIN)
                HeelCurve = HEEL_MIN;

            if (MiddleCurve > MIDDLE_MAX)
                MiddleCurve = MIDDLE_MAX;
            else if (MiddleCurve < MIDDLE_MIN)
                MiddleCurve = MIDDLE_MIN;

            if (ToeCurve > TOE_MAX)
                ToeCurve = TOE_MAX;
            else if (ToeCurve < TOE_MIN)
                ToeCurve = TOE_MIN;

            if (TipCurve > TIP_MAX)
                TipCurve = TIP_MAX;
            else if (TipCurve < TIP_MIN)
                TipCurve = TIP_MIN;
        }
    }
}
