namespace oomtm450PuckMod_CurvedStick {
    internal static class Constants {
        /// <summary>
        /// Const string, added current Puck Application.version to check for mod compatibility.
        /// </summary>
        internal const string CURRENT_APPLICATION_VERSION = "1153";

        internal const string WORKSHOP_MOD_NAME = "Curved Stick";

        /// <summary>
        /// Const string, prefix of all the mod's names.
        /// </summary>
        private const string MODS_PREFIX = "oomtm450_";

        /// <summary>
        /// Const string, name of the mod.
        /// </summary>
        internal const string MOD_NAME = MODS_PREFIX + "curvedstick";


        internal const string FROM_SERVER_TO_CLIENT = MOD_NAME + "_server";
        internal const string FROM_CLIENT_TO_SERVER = MOD_NAME + "_client";
        internal const string NEW_CURVED_STICK_VALUES = MOD_NAME + "_ncsv";
    }
}
