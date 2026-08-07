using HarmonyLib;
using oomtm450PuckMod_CurvedStick.Configs;
using oomtm450PuckMod_CurvedStick.SystemFunc;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Unity.Netcode;
using UnityEngine;

namespace oomtm450PuckMod_CurvedStick {
    /// <summary>
    /// Class containing the main code for the CurvedStick patch.
    /// </summary>
    public class CurvedStick : IPuckPlugin {
        #region Constants
        /// <summary>
        /// Const string, version of the mod.
        /// </summary>
        private const string MOD_VERSION = "0.3.6a";

        /// <summary>
        /// List of string, last released versions of the mod.
        /// </summary>
        private static readonly ReadOnlyCollection<string> OLD_MOD_VERSIONS = new ReadOnlyCollection<string>(new List<string> {
            "0.1.0",
            "0.2.0",
            "0.2.1",
            "0.3.0",
            "0.3.1",
            "0.3.2",
            "0.3.3",
            "0.3.4",
            "0.3.4a",
            "0.3.5",
            "0.3.6",
        });

        private const string ASK_SERVER_FOR_DATA = Constants.MOD_NAME + "ASKDATA";

        private static string HELP_MESSAGE { get; } = $"Curve stick commands:\n* <b>/curve</b> - Adjust all curve values heel, middle, toe and tip ({Configs.ClientConfig.HEEL_MIN}-{Configs.ClientConfig.HEEL_MAX} {Configs.ClientConfig.MIDDLE_MIN}-{Configs.ClientConfig.MIDDLE_MAX} {Configs.ClientConfig.TOE_MIN}-{Configs.ClientConfig.TOE_MAX} {Configs.ClientConfig.TIP_MIN}-{Configs.ClientConfig.TIP_MAX})\n* <b>/resetcurve</b> - Reset all curve values\n* <b>/heelcurve</b> - Adjust the curve on the heel ({Configs.ClientConfig.HEEL_MIN}-{Configs.ClientConfig.HEEL_MAX})\n* <b>/middlecurve</b> - Adjust the curve on the middle ({Configs.ClientConfig.MIDDLE_MIN}-{Configs.ClientConfig.MIDDLE_MAX})\n* <b>/toecurve</b> - Adjust the curve on the toe ({Configs.ClientConfig.TOE_MIN}-{Configs.ClientConfig.TOE_MAX})\n* <b>/tipcurve</b> - Adjust the curve on the tip ({Configs.ClientConfig.TIP_MIN}-{Configs.ClientConfig.TIP_MAX})\n";

        private const ulong REPLAY_PLAYER_OFFSET = 1337UL;
        #endregion

        #region Fields/Properties
        /// <summary>
        /// Harmony, harmony instance to patch the Puck's code.
        /// </summary>
        private static readonly Harmony _harmony = new Harmony(Constants.MOD_NAME);

        /// <summary>
        /// Configs.ServerConfig, config set and sent by the server.
        /// </summary>
        private static Configs.ServerConfig ServerConfig { get; set; } = new Configs.ServerConfig();

        /// <summary>
        /// Configs.ServerConfig, config set by the client.
        /// </summary>
        private static Configs.ClientConfig ClientConfig { get; set; } = new Configs.ClientConfig();

        private static CurvedStickAsset _curvedStickAsset = null;

        private static readonly LockDictionary<ulong, Configs.ClientConfig> _playersCurve = new LockDictionary<ulong, Configs.ClientConfig>();

        #region Client-Side
        private static DateTime _lastDateTimeAskStartupData = DateTime.MinValue;

        private static bool _hasRegisteredWithNamedMessageHandler = false;

        private static bool _serverHasResponded = false;

        /// <summary>
        /// Int, number of time client asked the server for startup data.
        /// </summary>
        private static int _askServerForStartupDataCount = 0;

        private static Mesh _originalTapeMesh = null;

        /// <summary>
        /// Bool, true if the client asked to be warned because of versionning problems.
        /// </summary>
        private static bool _askForModOutOfDateWarning = false;

        /// <summary>
        /// Bool, true if the client needs to notify the user that the server is running an out of date version of the mod.
        /// </summary>
        private static bool _addServerModVersionOutOfDateMessage = false;
        #endregion

        #region Server-Side
        private static bool _updateAllSticksForReplay = false;
        private static readonly LockList<ulong> _sticksToUpdate = new LockList<ulong>();

        private static int _frameCounter = 0;

        /// <summary>
        /// LockDictionary of ulong and DateTime, last time a mod out of date message was sent to a client (ulong clientId).
        /// </summary>
        private static readonly LockDictionary<ulong, DateTime> _sentOutOfDateMessage = new LockDictionary<ulong, DateTime>();
        #endregion
        #endregion

        /// <summary>
        /// Class that patches the Update event from PhysicsManager.
        /// </summary>
        [HarmonyPatch(typeof(PhysicsManager), "Update")]
        public class PhysicsManager_Update_Patch {
            [HarmonyPrefix]
            public static bool Prefix() {
                if (!ServerFunc.IsDedicatedServer())
                    return true;

                try {
                    if (_updateAllSticksForReplay) {
                        _updateAllSticksForReplay = false;

                        foreach (Player player in PlayerManager.Instance.GetPlayers(true).Where(x => x.IsReplay.Value).ToList()) {
                            Configs.ClientConfig playerCurve;
                            PlayerHandedness handedness;
                            try {
                                playerCurve = _playersCurve[player.OwnerClientId - REPLAY_PLAYER_OFFSET];
                                Player realPlayer = PlayerManager.Instance.GetPlayerByClientId(player.OwnerClientId - REPLAY_PLAYER_OFFSET);
                                if (!realPlayer)
                                    continue;

                                handedness = realPlayer.Handedness.Value;
                            }
                            catch (KeyNotFoundException) {
                                continue;
                            }
                            catch (NullReferenceException) {
                                continue;
                            }

                            SetCurvedStick(player, playerCurve, handedness);
                        }

                        NetworkCommunication.SendDataToAll(nameof(SetCurvedStick) + "ALLREPLAY", "1", Constants.FROM_SERVER_TO_CLIENT, ServerConfig);
                    }
                    else {
                        if (++_frameCounter % 20 == 0) { // Check and send sticks update every x frames.
                            _frameCounter = 0;
                            List<ulong> sticksToUpdate = new List<ulong>(_sticksToUpdate);
                            _sticksToUpdate.Clear();

                            StringBuilder dataToSend = new StringBuilder();
                            foreach (ulong clientId in sticksToUpdate) {
                                Player player = PlayerManager.Instance.GetPlayerByClientId(clientId);
                                if (player == null || !player)
                                    continue;

                                Configs.ClientConfig playerCurve;
                                try {
                                    playerCurve = _playersCurve[clientId];
                                }
                                catch (KeyNotFoundException) {
                                    continue;
                                }

                                SetCurvedStick(player, playerCurve);
                                dataToSend.Append($"{clientId};{FormatCurveStickForCommunication(playerCurve)}!");
                            }

                            string dataToSendStr = dataToSend.ToString();
                            if (!string.IsNullOrEmpty(dataToSendStr))
                                NetworkCommunication.SendDataToAll(nameof(SetCurvedStick) + "ALL", dataToSendStr.Substring(0, dataToSendStr.Length - 1), Constants.FROM_SERVER_TO_CLIENT, ServerConfig);
                        }
                    }
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(PhysicsManager_Update_Patch)} Prefix().\n{ex}");
                }

                return true;
            }
        }

        /// <summary>
        /// Class that patches the Server_SpawnStick event from Player.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.Server_SpawnStick))]
        public static class Player_Server_SpawnStick_Patch {
            [HarmonyPostfix]
            public static void Postfix(Player __instance, Vector3 position, Quaternion rotation, PlayerRole role) {
                if (!ServerFunc.IsDedicatedServer())
                    return;

                try {
                    new Timer(UpdateStickTimerCallback, __instance.OwnerClientId, 150, Timeout.Infinite);
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(Player_Server_SpawnStick_Patch)} Postfix().\n{ex}");
                }
            }
        }

        /// <summary>
        /// Class that patches the Update event from PhysicsManager.
        /// </summary>
        [HarmonyPatch(typeof(PhysicsManager), "Update")]
        public class PhysicsManager_Update_ClientPatch { // TODO : Check for better function for this.
            [HarmonyPostfix]
            public static void Postfix() {
                // If this is the server, do not use the patch.
                if (ServerFunc.IsDedicatedServer() || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient)
                    return;

                try {
                    if (!_hasRegisteredWithNamedMessageHandler || !_serverHasResponded) {
                        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Constants.FROM_SERVER_TO_CLIENT, ReceiveData);
                        _hasRegisteredWithNamedMessageHandler = true;

                        DateTime now = DateTime.UtcNow;
                        if (_lastDateTimeAskStartupData + TimeSpan.FromSeconds(5) < now && _askServerForStartupDataCount++ < 12) {
                            _lastDateTimeAskStartupData = now;
                            NetworkCommunication.SendData(ASK_SERVER_FOR_DATA, "1", NetworkManager.ServerClientId, Constants.FROM_CLIENT_TO_SERVER, ClientConfig);
                            SendNewCurvedStickValues();
                        }
                    }
                    else if (_askForModOutOfDateWarning) {
                        _askForModOutOfDateWarning = false;
                        NetworkCommunication.SendData(Constants.MOD_NAME + "_modoutofdate", "1", NetworkManager.ServerClientId, Constants.FROM_CLIENT_TO_SERVER, ClientConfig);
                    }
                    else if (_addServerModVersionOutOfDateMessage) {
                        _addServerModVersionOutOfDateMessage = false;
                        AddClientChatMessage($"Server's {Constants.WORKSHOP_MOD_NAME} mod is out of date. Some functionalities might not work properly.");
                    }
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(PhysicsManager_Update_ClientPatch)} Postfix().\n{ex}");
                }
            }
        }

        /// <summary>
        /// Class that patches the Client_SendChatMessage event from ChatManager.
        /// </summary>
        [HarmonyPatch(typeof(ChatManager), nameof(ChatManager.Client_SendChatMessage))]
        public class ChatManager_Client_SendChatMessage_Patch {
            [HarmonyPrefix]
            public static bool Prefix(string content, bool isQuickChat, bool isTeamChat) {
                try {
                    // If this is the server, do not use the patch.
                    if (ServerFunc.IsDedicatedServer())
                        return true;

                    if (content.StartsWith(@"/")) {
                        content = content.ToLowerInvariant();

                        bool changeCurve = false;
                        if (content.StartsWith(@"/curve") || content.StartsWith(@"/resetcurve")) {
                            if (content.StartsWith(@"/resetcurve"))
                                content += "0 0 0 0";

                            content = content.Replace("/resetcurve", "").Replace(@"/curve", "").Trim();

                            if (string.IsNullOrEmpty(content)) {
                                AddClientChatMessage($"The curve is {FormatCurveStickForCommunication(ClientConfig).Replace(';', ' ')}");
                                return false;
                            }
                            else {
                                string[] splittedMessageCurve = content.Split(' ');
                                for (int i = 0; i < splittedMessageCurve.Length; i++) {
                                    if (int.TryParse(splittedMessageCurve[i], out int curveValue)) {
                                        switch (i) {
                                            case 0:
                                                if (curveValue > Configs.ClientConfig.HEEL_MAX)
                                                    curveValue = Configs.ClientConfig.HEEL_MAX;
                                                else if (curveValue < Configs.ClientConfig.HEEL_MIN)
                                                    curveValue = Configs.ClientConfig.HEEL_MIN;

                                                ClientConfig.HeelCurve = curveValue;
                                                break;

                                            case 1:
                                                if (curveValue > Configs.ClientConfig.MIDDLE_MAX)
                                                    curveValue = Configs.ClientConfig.MIDDLE_MAX;
                                                else if (curveValue < Configs.ClientConfig.MIDDLE_MIN)
                                                    curveValue = Configs.ClientConfig.MIDDLE_MIN;

                                                ClientConfig.MiddleCurve = curveValue;
                                                break;

                                            case 2:
                                                if (curveValue > Configs.ClientConfig.TOE_MAX)
                                                    curveValue = Configs.ClientConfig.TOE_MAX;
                                                else if (curveValue < Configs.ClientConfig.TOE_MIN)
                                                    curveValue = Configs.ClientConfig.TOE_MIN;

                                                ClientConfig.ToeCurve = curveValue;
                                                break;

                                            case 3:
                                                if (curveValue > Configs.ClientConfig.TIP_MAX)
                                                    curveValue = Configs.ClientConfig.TIP_MAX;
                                                else if (curveValue < Configs.ClientConfig.TIP_MIN)
                                                    curveValue = Configs.ClientConfig.TIP_MIN;

                                                ClientConfig.TipCurve = curveValue;
                                                break;
                                        }

                                        changeCurve = true;
                                    }
                                }
                            }
                        }
                        else if (content.StartsWith(@"/heelcurve")) {
                            content = content.Replace(@"/heelcurve", "").Trim();

                            if (string.IsNullOrEmpty(content)) {
                                AddClientChatMessage($"The heel curve is {ClientConfig.HeelCurve}");
                                return false;
                            }
                            else {
                                if (int.TryParse(content, out int heelCurveValue)) {
                                    if (heelCurveValue > Configs.ClientConfig.HEEL_MAX)
                                        heelCurveValue = Configs.ClientConfig.HEEL_MAX;
                                    else if (heelCurveValue < Configs.ClientConfig.HEEL_MIN)
                                        heelCurveValue = Configs.ClientConfig.HEEL_MIN;

                                    ClientConfig.HeelCurve = heelCurveValue;
                                    changeCurve = true;
                                }
                            }
                        }
                        else if (content.StartsWith(@"/middlecurve")) {
                            content = content.Replace(@"/middlecurve", "").Trim();

                            if (string.IsNullOrEmpty(content)) {
                                AddClientChatMessage($"The middle curve is {ClientConfig.MiddleCurve}");
                                return false;
                            }
                            else {
                                if (int.TryParse(content, out int middleCurveValue)) {
                                    if (middleCurveValue > Configs.ClientConfig.MIDDLE_MAX)
                                        middleCurveValue = Configs.ClientConfig.MIDDLE_MAX;
                                    else if (middleCurveValue < Configs.ClientConfig.MIDDLE_MIN)
                                        middleCurveValue = Configs.ClientConfig.MIDDLE_MIN;

                                    ClientConfig.MiddleCurve = middleCurveValue;
                                    changeCurve = true;
                                }
                            }
                        }
                        else if (content.StartsWith(@"/toecurve")) {
                            content = content.Replace(@"/toecurve", "").Trim();

                            if (string.IsNullOrEmpty(content)) {
                                AddClientChatMessage($"The toe curve is {ClientConfig.ToeCurve}");
                                return false;
                            }
                            else {
                                if (int.TryParse(content, out int toeCurveValue)) {
                                    if (toeCurveValue > Configs.ClientConfig.TOE_MAX)
                                        toeCurveValue = Configs.ClientConfig.TOE_MAX;
                                    else if (toeCurveValue < Configs.ClientConfig.TOE_MIN)
                                        toeCurveValue = Configs.ClientConfig.TOE_MIN;

                                    ClientConfig.ToeCurve = toeCurveValue;
                                    changeCurve = true;
                                }
                            }
                        }
                        else if (content.StartsWith(@"/tipcurve")) {
                            content = content.Replace(@"/tipcurve", "").Trim();

                            if (string.IsNullOrEmpty(content)) {
                                AddClientChatMessage($"The tip curve is {ClientConfig.TipCurve}");
                                return false;
                            }
                            else {
                                if (int.TryParse(content, out int tipCurveValue)) {
                                    if (tipCurveValue > Configs.ClientConfig.TIP_MAX)
                                        tipCurveValue = Configs.ClientConfig.TIP_MAX;
                                    else if (tipCurveValue < Configs.ClientConfig.TIP_MIN)
                                        tipCurveValue = Configs.ClientConfig.TIP_MIN;

                                    ClientConfig.TipCurve = tipCurveValue;
                                    changeCurve = true;
                                }
                            }
                        }
                        else if (content.StartsWith(@"/curvehelp")) {
                            AddClientChatMessage(HELP_MESSAGE);
                            return false;
                        }

                        if (changeCurve) {
                            SendNewCurvedStickValues();
                            return false;
                        }
                    }
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(ChatManager_Client_SendChatMessage_Patch)} Prefix().\n{ex}");
                }

                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(string content, bool isQuickChat, bool isTeamChat) {
                try {
                    // If this is the server, do not use the patch.
                    if (ServerFunc.IsDedicatedServer())
                        return;

                    if (content.StartsWith(@"/")) {
                        content = content.ToLowerInvariant();

                        if (content.StartsWith(@"/help"))
                            AddClientChatMessage(HELP_MESSAGE);
                    }
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(ChatManager_Client_SendChatMessage_Patch)} Postfix().\n{ex}");
                }
            }
        }

        /// <summary>
        /// Class that patches the Server_SetGameState event from GameManager.
        /// </summary>
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.Server_SetGameState))]
        public class GameManager_Server_SetGameState_Patch {
            [HarmonyPostfix]
            public static void Postfix(GamePhase? phase, int? tick, int? period, int? blueScore, int? redScore, bool? isOvertime) {
                try {
                    // If this is not the server, do not use the patch.
                    if (!ServerFunc.IsDedicatedServer())
                        return;

                    if (phase != GamePhase.PreGame)
                        return;

                    _sentOutOfDateMessage.Clear();
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(GameManager_Server_SetGameState_Patch)} Postfix().\n{ex}");
                }
            }
        }

        /// <summary>
        /// Class that patches the OnDestroy event from StickMesh.
        /// </summary>
        [HarmonyPatch(typeof(StickMesh), "OnDestroy")]
        public class StickMesh_OnDestroy_Patch {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.VeryLow)]
            public static bool Prefix(StickMesh __instance) {
                try {
                    MeshRenderer stickMeshRenderer = GetPrivateField<MeshRenderer>(typeof(StickMesh), __instance, "stickMeshRenderer");
                    if (stickMeshRenderer != null && stickMeshRenderer.material != null)
                        UnityEngine.Object.Destroy(stickMeshRenderer.material);

                    MeshRenderer shaftTapeMeshRenderer = GetPrivateField<MeshRenderer>(typeof(StickMesh), __instance, "shaftTapeMeshRenderer");
                    if (shaftTapeMeshRenderer != null && shaftTapeMeshRenderer.material != null)
                        UnityEngine.Object.Destroy(shaftTapeMeshRenderer.material);

                    MeshRenderer bladeTapeMeshRenderer = GetPrivateField<MeshRenderer>(typeof(StickMesh), __instance, "bladeTapeMeshRenderer");
                    if (bladeTapeMeshRenderer != null && bladeTapeMeshRenderer.material != null)
                        UnityEngine.Object.Destroy(bladeTapeMeshRenderer.material);
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(StickMesh_OnDestroy_Patch)} Prefix().\n{ex}");
                }

                return false;
            }
        }

        /// <summary>
        /// Method called when a client has connected (joined a server) on the server-side.
        /// Used to set server-sided stuff after the game has loaded.
        /// </summary>
        /// <param name="message">Dictionary of string and object, content of the event.</param>
        public static void Event_Everyone_OnGameStateChanged(Dictionary<string, object> message) {
            if (!ServerFunc.IsDedicatedServer())
                return;

            try {
                GameState oldGameState = (GameState)message["oldGameState"];
                GameState newGameState = (GameState)message["newGameState"];

                if (oldGameState.Phase == newGameState.Phase)
                    return;

                if (newGameState.Phase != GamePhase.Replay)
                    return;

                _ = Resources.UnloadUnusedAssets();
                new Timer(UpdateAllSticksForReplayTimerCallback, null, 500, Timeout.Infinite);
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(Event_Everyone_OnGameStateChanged)}.\n{ex}");
            }
        }

        /// <summary>
        /// Method called when a client has connected (joined a server) on the server-side.
        /// Used to set server-sided stuff after the game has loaded.
        /// </summary>
        /// <param name="message">Dictionary of string and object, content of the event.</param>
        public static void Event_Everyone_OnClientConnected(Dictionary<string, object> message) {
            if (!ServerFunc.IsDedicatedServer())
                return;

            try {
                if (NetworkManager.Singleton != null && !_hasRegisteredWithNamedMessageHandler) {
                    Logging.Log($"RegisterNamedMessageHandler {Constants.FROM_CLIENT_TO_SERVER}.", ServerConfig);
                    NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Constants.FROM_CLIENT_TO_SERVER, ReceiveData);
                    _hasRegisteredWithNamedMessageHandler = true;
                }
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(Event_Everyone_OnClientConnected)}.\n{ex}");
            }
        }

        /// <summary>
        /// Method called when a client has connected (joined a server) on the server-side.
        /// Used to set server-sided stuff after the game has loaded.
        /// </summary>
        /// <param name="message">Dictionary of string and object, content of the event.</param>
        public static void Event_Everyone_OnClientDisconnected(Dictionary<string, object> message) {
            if (!ServerFunc.IsDedicatedServer())
                return;

            try {
                ulong clientId = (ulong)message["clientId"];

                _sentOutOfDateMessage.Remove(clientId);
                _playersCurve.Remove(clientId);
                _sticksToUpdate.Remove(clientId);
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(Event_Everyone_OnClientDisconnected)}.\n{ex}");
            }
        }

        /// <summary>
        /// Method that manages received data from client-server communications.
        /// </summary>
        /// <param name="clientId">Ulong, Id of the client that sent the data. (0 if the server sent the data)</param>
        /// <param name="reader">FastBufferReader, stream containing the received data.</param>
        public static void ReceiveData(ulong clientId, FastBufferReader reader) {
            try {
                string dataName, dataStr;
                if (clientId == NetworkManager.ServerClientId) // If client Id is 0, we received data from the server, so we are client-sided.
                    (dataName, dataStr) = NetworkCommunication.GetData(clientId, reader, ClientConfig);
                else
                    (dataName, dataStr) = NetworkCommunication.GetData(clientId, reader, ServerConfig);

                switch (dataName) {
                    case Constants.MOD_NAME + "_" + nameof(MOD_VERSION): // CLIENT-SIDE : Mod version check, warn if client and server versions are not the same.
                        _serverHasResponded = true;
                        if (MOD_VERSION == dataStr)
                            break;
                        else if (OLD_MOD_VERSIONS.Contains(dataStr)) {
                            _addServerModVersionOutOfDateMessage = true;
                            break;
                        }

                        _askForModOutOfDateWarning = true;
                        break;

                    case nameof(SetCurvedStick):
                        SetCurvedStickClientReceiveData(dataStr);
                        break;

                    case nameof(SetCurvedStick) + "ALL":
                        foreach (string playerCurvedStickDataStr in dataStr.Split('!'))
                            SetCurvedStickClientReceiveData(playerCurvedStickDataStr);
                        break;

                    case nameof(SetCurvedStick) + "ALLREPLAY":
                        if (dataStr != "1")
                            return;

                        foreach (Player _player in PlayerManager.Instance.GetPlayers(true).Where(x => x.IsReplay.Value)) {
                            Player realPlayer = PlayerManager.Instance.GetPlayerByClientId(_player.OwnerClientId - REPLAY_PLAYER_OFFSET);
                            if (!realPlayer)
                                continue;

                            if (realPlayer.IsLocalPlayer)
                                SetCurvedStick(_player, ClientConfig, realPlayer.Handedness.Value);
                            else
                                SetCurvedStick(_player, _playersCurve[_player.OwnerClientId - REPLAY_PLAYER_OFFSET], realPlayer.Handedness.Value);
                        }
                        break;

                    case Constants.MOD_NAME + "_modoutofdate": // SERVER-SIDE : Warn the client that the mod is out of date.
                        if (dataStr != "1")
                            break;

                        //NetworkManager.Singleton.DisconnectClient(clientId,
                        //$"Mod is out of date. Please unsubscribe from {Constants.WORKSHOP_MOD_NAME} in the workshop and restart your game to update.");

                        if (!_sentOutOfDateMessage.TryGetValue(clientId, out DateTime lastCheckTime)) {
                            lastCheckTime = DateTime.MinValue;
                            _sentOutOfDateMessage.Add(clientId, lastCheckTime);
                        }

                        DateTime utcNow = DateTime.UtcNow;
                        if (lastCheckTime + TimeSpan.FromSeconds(900) < utcNow) {
                            if (string.IsNullOrEmpty(PlayerManager.Instance.GetPlayerByClientId(clientId).Username.Value.ToString()))
                                break;

                            Logging.Log($"Warning client {clientId} mod out of date.", ServerConfig);
                            ChatManager.Instance.BroadcastMessage($"{PlayerManager.Instance.GetPlayerByClientId(clientId).Username.Value} : {Constants.WORKSHOP_MOD_NAME} Mod is out of date. Please unsubscribe from {Constants.WORKSHOP_MOD_NAME} in the workshop and restart your game to update.");
                            _sentOutOfDateMessage[clientId] = utcNow;
                        }
                        break;

                    case ASK_SERVER_FOR_DATA: // SERVER-SIDE : Send the necessary data to client.
                        if (dataStr != "1")
                            break;

                        NetworkCommunication.SendData(Constants.MOD_NAME + "_" + nameof(MOD_VERSION), MOD_VERSION, clientId, Constants.FROM_SERVER_TO_CLIENT, ServerConfig);
                        StringBuilder dataToSend = new StringBuilder();
                        Dictionary<ulong, Configs.ClientConfig> playersCurve = new Dictionary<ulong, Configs.ClientConfig>(_playersCurve);
                        foreach (KeyValuePair<ulong, Configs.ClientConfig> curve in playersCurve)
                            dataToSend.Append($"{curve.Key};{FormatCurveStickForCommunication(curve.Value)}!");

                        string dataToSendStr = dataToSend.ToString();
                        if (!string.IsNullOrEmpty(dataToSendStr))
                            NetworkCommunication.SendData(nameof(SetCurvedStick) + "ALL", dataToSendStr.Substring(0, dataToSendStr.Length - 1), clientId, Constants.FROM_SERVER_TO_CLIENT, ServerConfig);
                        break;

                    case Constants.NEW_CURVED_STICK_VALUES: // SERVER-SIDE : Receive new stick values and asks everyone to update it.
                        string[] splittedDataStrNewCurveStickValues = dataStr.Split(';');
                        if (!_playersCurve.TryGetValue(clientId, out Configs.ClientConfig curveNewCurveStickValues)) {
                            curveNewCurveStickValues = new Configs.ClientConfig();
                            _playersCurve.Add(clientId, curveNewCurveStickValues);
                        }

                        curveNewCurveStickValues.HeelCurve = int.Parse(splittedDataStrNewCurveStickValues[0]);
                        curveNewCurveStickValues.MiddleCurve = int.Parse(splittedDataStrNewCurveStickValues[1]);
                        curveNewCurveStickValues.ToeCurve = int.Parse(splittedDataStrNewCurveStickValues[2]);
                        curveNewCurveStickValues.TipCurve = int.Parse(splittedDataStrNewCurveStickValues[3]);

                        curveNewCurveStickValues.CheckCurveValues();

                        _sticksToUpdate.Add(clientId);
                        break;
                }
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(ReceiveData)}.\n{ex}");
            }
        }

        private static void SetCurvedStickClientReceiveData(string dataStr) {
            try {
                string[] splittedDataStrSetCurvedStick = dataStr.Split(';');
                ulong clientId = ulong.Parse(splittedDataStrSetCurvedStick[0]);
                if (!_playersCurve.TryGetValue(clientId, out Configs.ClientConfig curveSetCurvedStick)) {
                    curveSetCurvedStick = new Configs.ClientConfig();
                    _playersCurve.Add(clientId, curveSetCurvedStick);
                }

                curveSetCurvedStick.HeelCurve = int.Parse(splittedDataStrSetCurvedStick[1]);
                curveSetCurvedStick.MiddleCurve = int.Parse(splittedDataStrSetCurvedStick[2]);
                curveSetCurvedStick.ToeCurve = int.Parse(splittedDataStrSetCurvedStick[3]);
                curveSetCurvedStick.TipCurve = int.Parse(splittedDataStrSetCurvedStick[4]);

                Player player = PlayerManager.Instance.GetPlayerByClientId(clientId);
                if (player == null || !player)
                    return;

                SetCurvedStick(player, curveSetCurvedStick);
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(SetCurvedStickClientReceiveData)}.\n{ex}");
            }
        }

        private static void UpdateStickTimerCallback(object stateInfo) {
            try {
                _sticksToUpdate.Add((ulong)stateInfo);
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(UpdateStickTimerCallback)}.\n{ex}");
            }
        }

        private static void UpdateAllSticksForReplayTimerCallback(object stateInfo) {
            try {
                _updateAllSticksForReplay = true;
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(UpdateAllSticksForReplayTimerCallback)}.\n{ex}");
            }
        }

        private static void SetCurvedStick(Player player, Configs.ClientConfig curve) {
            if (!player || player.Role != PlayerRole.Attacker)
                return;

            SetCurvedStick(player, curve, player.Handedness.Value);
        }

        private static void SetCurvedStick(Player player, Configs.ClientConfig curve, PlayerHandedness handedness) {
            try {
                if (!player || player.Role != PlayerRole.Attacker)
                    return;

                if (_curvedStickAsset == null) {
                    _curvedStickAsset = new GameObject(Constants.MOD_NAME + "CurvedStickAsset").AddComponent<CurvedStickAsset>();
                    _curvedStickAsset.LoadAssets();
                }

                if (_curvedStickAsset.Meshes.Count == 0 || _curvedStickAsset.Errors.Count != 0) {
                    if (_curvedStickAsset.Meshes.Count == 0)
                        Logging.LogError("No mesh found in the assets.");
                    foreach (string error in _curvedStickAsset.Errors)
                        Logging.LogError(error);

                    return;
                }

                // Find parent stickMesh object.
                Transform stickMeshTransform = player.gameObject.transform.Find("Stick (Attacker)(Clone)");
                if (!stickMeshTransform)
                    return;

                stickMeshTransform = stickMeshTransform.gameObject.transform.Find("Rotation Container");
                if (!stickMeshTransform)
                    return;

                stickMeshTransform = stickMeshTransform.gameObject.transform.Find("Stick Mesh (Attacker)");
                if (!stickMeshTransform)
                    return;

                GameObject stickMesh = stickMeshTransform.gameObject;

                string handednessStr;
                if (handedness == PlayerHandedness.Right)
                    handednessStr = CurvedStickAsset.RIGHT;
                else
                    handednessStr = CurvedStickAsset.LEFT;

                if (!ServerFunc.IsDedicatedServer()) {
                    SkinnedMeshRenderer prefabSkinnedMeshRendererStick = _curvedStickAsset.Meshes[CurvedStickAsset.STICK].transform.GetComponentInChildren<SkinnedMeshRenderer>();
                    SetCurvedStickMagicClient(stickMesh, prefabSkinnedMeshRendererStick, curve, handednessStr);
                }
                else {
                    Transform curvedBladeTransform = _curvedStickAsset.Meshes[CurvedStickAsset.BLADE].transform;
                    SkinnedMeshRenderer prefabSkinnedMeshRendererBlade = curvedBladeTransform.GetComponentInChildren<SkinnedMeshRenderer>();

                    // Set blade collider for puck.
                    GameObject bladePuckGameObject = stickMesh.transform.Find("Puck Colliders").gameObject.transform.Find("Blade").gameObject;
                    SetCurvedStickMagicServer(bladePuckGameObject, prefabSkinnedMeshRendererBlade, curve, handednessStr);
                    ChangeLayerOfAllChild(bladePuckGameObject.transform, bladePuckGameObject.layer);

                    // Set blade collider for stick.
                    GameObject bladeStickGameObject = stickMesh.transform.Find("Stick Colliders").gameObject.transform.Find("Blade").gameObject;
                    SetCurvedStickMagicServer(bladeStickGameObject, prefabSkinnedMeshRendererBlade, curve, handednessStr);
                    ChangeLayerOfAllChild(bladeStickGameObject.transform, bladeStickGameObject.layer);
                }
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(SetCurvedStick)}.\n{ex}");
            }
        }

        private static void ChangeLayerOfAllChild(Transform transform, int layer) {
            try {
                for (int i = 0; i < transform.childCount; i++)
                    ChangeLayerOfAllChild(transform.GetChild(i), layer);

                transform.gameObject.layer = layer;
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(ChangeLayerOfAllChild)}.\n{ex}");
            }
        }

        private static void SetCurvedStickMagicClient(GameObject stickMesh, SkinnedMeshRenderer prefabSkinnedMeshRenderer, Configs.ClientConfig curve,
            string handedness) {
            try {
                GameObject stickAttackerGameObject = stickMesh.transform.Find("stick_attacker").gameObject;
                GameObject stickGameObject = stickAttackerGameObject.transform.Find("Stick (Attacker)").gameObject;

                MeshRenderer originalMeshRenderer = null;
                SkinnedMeshRenderer skinnedMeshRenderer = null;
                bool replaceOldStick = true;
                try {
                    originalMeshRenderer = stickGameObject.GetComponent<MeshRenderer>();
                    if (originalMeshRenderer == null) {
                        replaceOldStick = false;
                        skinnedMeshRenderer = stickGameObject.GetComponent<SkinnedMeshRenderer>();
                    }
                }
                catch {
                    replaceOldStick = false;
                    skinnedMeshRenderer = stickGameObject.GetComponent<SkinnedMeshRenderer>();
                }

                if (replaceOldStick) {
                    Material[] originalMeshRendererSharedMaterials = originalMeshRenderer.sharedMaterials;
                    UnityEngine.Object.DestroyImmediate(originalMeshRenderer);
                    UnityEngine.Object.DestroyImmediate(stickGameObject.GetComponent<MeshFilter>());

                    skinnedMeshRenderer = stickGameObject.AddComponent<SkinnedMeshRenderer>();
                    if (skinnedMeshRenderer.sharedMesh != null)
                        UnityEngine.GameObject.Destroy(skinnedMeshRenderer.sharedMesh);
                    skinnedMeshRenderer.sharedMesh = DuplicateMesh(prefabSkinnedMeshRenderer.sharedMesh);

                    // --- 1. Get the bone mapping ---
                    // Create a dictionary of the target skeleton's bones for efficient lookup
                    var boneMap = new Dictionary<string, Transform>();
                    var boneInfo = new Dictionary<string, BoneInfo>();
                    foreach (var t in prefabSkinnedMeshRenderer.rootBone.GetComponentsInChildren<Transform>()) {
                        boneMap[t.name] = t;
                        boneInfo[t.name] = new BoneInfo {
                            name = t.name,
                            localPosition = t.localPosition,
                            localRotation = t.localRotation,
                            localScale = t.localScale,
                        };
                    }

                    // Create the new bones array for the SkinnedMeshRenderer
                    Transform[] newBones = new Transform[prefabSkinnedMeshRenderer.bones.Length];
                    for (int i = 0; i < prefabSkinnedMeshRenderer.bones.Length; i++) {
                        string boneName = prefabSkinnedMeshRenderer.bones[i].name;
                        boneInfo[boneName].parentIndex = i;
                        if (boneMap.TryGetValue(boneName, out Transform mappedBone)) {
                            newBones[i] = UnityEngine.Object.Instantiate(mappedBone, mappedBone.position, mappedBone.rotation);
                        }
                        else {
                            Logging.LogError($"Could not find bone '{boneName}' in the target skeleton.");
                            return;
                        }
                    }

                    // Store the parent index for each bone
                    for (int i = 0; i < prefabSkinnedMeshRenderer.bones.Length; i++) {
                        Transform parent = prefabSkinnedMeshRenderer.bones[i].parent;
                        if (parent != null && boneMap.ContainsKey(parent.name))
                            newBones[i].SetParent(newBones.First(x => x.name.StartsWith(parent.name)), false);
                        else
                            newBones[i].SetParent(stickGameObject.transform, false);

                        newBones[i].localPosition = boneInfo.Values.First(x => x.parentIndex == i).localPosition;
                        newBones[i].localRotation = boneInfo.Values.First(x => x.parentIndex == i).localRotation;
                        newBones[i].localScale = boneInfo.Values.First(x => x.parentIndex == i).localScale;
                    }

                    boneMap.Clear();
                    boneInfo.Clear();

                    skinnedMeshRenderer.bones = newBones;
                    skinnedMeshRenderer.rootBone = skinnedMeshRenderer.bones.First(x => x.name.StartsWith("Base"));
                    if (skinnedMeshRenderer.sharedMaterials != null) {
                        foreach (Material material in skinnedMeshRenderer.sharedMaterials)
                            UnityEngine.GameObject.Destroy(material);
                    }
                    skinnedMeshRenderer.sharedMaterials = originalMeshRendererSharedMaterials;

                    skinnedMeshRenderer.rootBone.SetParent(stickGameObject.transform, false);
                }

                // Set stick mesh values.
                SetTransformRotationForCurve(skinnedMeshRenderer, handedness, curve);

                // TODO : Set blade tape mesh.
                MeshFilter tapeMeshFilter = stickAttackerGameObject.transform.Find("Blade Tape (Attacker)").gameObject.GetComponent<MeshFilter>();
                if (_originalTapeMesh == null)
                    _originalTapeMesh = tapeMeshFilter.sharedMesh;
                if (tapeMeshFilter.sharedMesh != null && tapeMeshFilter.sharedMesh != _originalTapeMesh)
                    UnityEngine.GameObject.Destroy(tapeMeshFilter.sharedMesh);
                if (curve.NoCurve) // TODO : Remove temp code to add a real tape.
                    tapeMeshFilter.sharedMesh = _originalTapeMesh;
                else
                    tapeMeshFilter.sharedMesh = null;
                // TODO : Set tape mesh values.
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(SetCurvedStickMagicClient)}.\n{ex}");
            }
        }

        private static void SetCurvedStickMagicServer(GameObject gameObject, SkinnedMeshRenderer prefabSkinnedMeshRenderer, Configs.ClientConfig curve,
            string handedness) {
            try {
                SkinnedMeshRenderer skinnedMeshRenderer = null;
                bool replaceOldStick = false;
                try {
                    skinnedMeshRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();
                    if (skinnedMeshRenderer == null)
                        replaceOldStick = true;
                }
                catch {
                    replaceOldStick = true;
                }

                MeshCollider meshCollider = gameObject.GetComponent<MeshCollider>();

                if (replaceOldStick) {
                    meshCollider.convex = true;

                    skinnedMeshRenderer = gameObject.AddComponent<SkinnedMeshRenderer>();
                    if (skinnedMeshRenderer.sharedMesh != null)
                        UnityEngine.GameObject.Destroy(skinnedMeshRenderer.sharedMesh);
                    skinnedMeshRenderer.sharedMesh = DuplicateMesh(prefabSkinnedMeshRenderer.sharedMesh);
                    skinnedMeshRenderer.updateWhenOffscreen = true;

                    // Create a dictionary of the target skeleton's bones for efficient lookup
                    Dictionary<string, Transform> boneMap = new Dictionary<string, Transform>();
                    Dictionary<string, BoneInfo> boneInfo = new Dictionary<string, BoneInfo>();

                    foreach (var t in prefabSkinnedMeshRenderer.rootBone.GetComponentsInChildren<Transform>()) {
                        boneMap[t.name] = t;
                        boneInfo[t.name] = new BoneInfo {
                            name = t.name,
                            localPosition = t.localPosition,
                            localRotation = t.localRotation,
                            localScale = t.localScale,
                        };
                    }

                    // Create the new bones array for the SkinnedMeshRenderer
                    Transform[] newBones = new Transform[prefabSkinnedMeshRenderer.bones.Length];
                    for (int i = 0; i < prefabSkinnedMeshRenderer.bones.Length; i++) {
                        string boneName = prefabSkinnedMeshRenderer.bones[i].name;
                        boneInfo[boneName].parentIndex = i;
                        if (boneMap.TryGetValue(boneName, out Transform mappedBone))
                            newBones[i] = UnityEngine.Object.Instantiate(mappedBone, mappedBone.position, mappedBone.rotation);
                        else {
                            Logging.LogError($"Could not find bone '{boneName}' in the target skeleton.");
                            return;
                        }
                    }

                    // Store the parent index for each bone
                    for (int i = 0; i < prefabSkinnedMeshRenderer.bones.Length; i++) {
                        Transform parent = prefabSkinnedMeshRenderer.bones[i].parent;
                        if (parent != null && boneMap.ContainsKey(parent.name))
                            newBones[i].SetParent(newBones.First(x => x.name.StartsWith(parent.name)), false);
                        else
                            newBones[i].SetParent(gameObject.transform, false);

                        newBones[i].localPosition = boneInfo.Values.First(x => x.parentIndex == i).localPosition;
                        newBones[i].localRotation = boneInfo.Values.First(x => x.parentIndex == i).localRotation;
                        newBones[i].localScale = boneInfo.Values.First(x => x.parentIndex == i).localScale;
                    }

                    skinnedMeshRenderer.bones = newBones;
                    skinnedMeshRenderer.rootBone = skinnedMeshRenderer.bones.First(x => x.name.StartsWith("Base"));

                    skinnedMeshRenderer.rootBone.SetParent(gameObject.transform, false);

                    // Remove old prefab gameObjects.
                    Transform baseClone = gameObject.transform.GetChild(0);
                    UnityEngine.GameObject.Destroy(baseClone.GetChild(0).gameObject);
                    Transform heelClone = baseClone.GetChild(2);
                    UnityEngine.GameObject.Destroy(heelClone.GetChild(0).gameObject);
                    Transform middleClone = heelClone.GetChild(1);
                    UnityEngine.GameObject.Destroy(middleClone.GetChild(0).gameObject);
                    Transform toeClone = middleClone.GetChild(1);
                    UnityEngine.GameObject.Destroy(toeClone.GetChild(0).gameObject);
                    Transform tipClone = toeClone.GetChild(1);
                    UnityEngine.GameObject.Destroy(tipClone.GetChild(0).gameObject);
                }

                // Set stick mesh values.
                SetTransformRotationForCurve(skinnedMeshRenderer, handedness, curve);

                if (meshCollider.sharedMesh != null)
                    UnityEngine.GameObject.Destroy(meshCollider.sharedMesh);
                Mesh colliderMesh = new Mesh();
                skinnedMeshRenderer.BakeMesh(colliderMesh);
                meshCollider.sharedMesh = colliderMesh;
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(SetCurvedStickMagicServer)}.\n{ex}");
            }
        }

        private static void SetTransformRotationForCurve(SkinnedMeshRenderer skinnedMeshRenderer, string handedness, Configs.ClientConfig curve) {
            try {
                Transform heelTransform = skinnedMeshRenderer.bones.First(x => x.name.StartsWith("Heel")).transform;
                heelTransform.localRotation = new Quaternion(
                    heelTransform.localRotation.x,
                    handedness == CurvedStickAsset.RIGHT ? curve.HeelCurveF / -2f : curve.HeelCurveF / 2f,
                    handedness == CurvedStickAsset.RIGHT ? curve.HeelCurveF : curve.HeelCurveF / -1,
                    heelTransform.localRotation.w);

                Transform middleTransform = skinnedMeshRenderer.bones.First(x => x.name.StartsWith("Middle")).transform;
                middleTransform.localRotation = new Quaternion(
                    middleTransform.localRotation.x,
                    handedness == CurvedStickAsset.RIGHT ? curve.MiddleCurveF / -2f : curve.MiddleCurveF / 2f,
                    handedness == CurvedStickAsset.RIGHT ? curve.MiddleCurveF : curve.MiddleCurveF / -1,
                    middleTransform.localRotation.w);

                Transform toeTransform = skinnedMeshRenderer.bones.First(x => x.name.StartsWith("Toe")).transform;
                toeTransform.localRotation = new Quaternion(
                    toeTransform.localRotation.x,
                    handedness == CurvedStickAsset.RIGHT ? curve.ToeCurveF / -2f : curve.ToeCurveF / 2f,
                    handedness == CurvedStickAsset.RIGHT ? curve.ToeCurveF : curve.ToeCurveF / -1,
                    toeTransform.localRotation.w);

                Transform tipTransform = skinnedMeshRenderer.bones.First(x => x.name.StartsWith("Tip")).transform;
                float tipYValue = handedness == CurvedStickAsset.RIGHT ? curve.TipCurveF / -2f : curve.TipCurveF / 2f;
                if (tipYValue > 0.5f)
                    tipYValue = 0.5f;
                tipTransform.localRotation = new Quaternion(
                    tipTransform.localRotation.x,
                    tipYValue,
                    handedness == CurvedStickAsset.RIGHT ? curve.TipCurveF : curve.TipCurveF / -1,
                    tipTransform.localRotation.w);
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(SetTransformRotationForCurve)}.\n{ex}");
            }
        }

        private static Mesh DuplicateMesh(Mesh sourceMesh) {
            Mesh targetMesh = new Mesh {
                name = "curvedStickMesh_" + new System.Random().Next(1000000),
                vertices = sourceMesh.vertices,
                normals = sourceMesh.normals,
                tangents = sourceMesh.tangents,
                triangles = sourceMesh.triangles,
                uv = sourceMesh.uv,
                colors = sourceMesh.colors,
                subMeshCount = sourceMesh.subMeshCount,
                bindposes = sourceMesh.bindposes,
                boneWeights = sourceMesh.boneWeights,
            };

            for (int i = 0; i < targetMesh.subMeshCount; ++i)
                targetMesh.SetSubMesh(i, sourceMesh.GetSubMesh(i));

            return targetMesh;
        }

        private static void SendNewCurvedStickValues() {
            ClientConfig.SaveConfig();
            NetworkCommunication.SendData(
                Constants.NEW_CURVED_STICK_VALUES,
                FormatCurveStickForCommunication(ClientConfig),
                NetworkManager.ServerClientId,
                Constants.FROM_CLIENT_TO_SERVER,
                ClientConfig);
        }

        private static string FormatCurveStickForCommunication(Configs.ClientConfig config) {
            return $"{config.HeelCurve};{config.MiddleCurve};{config.ToeCurve};{config.TipCurve}";
        }

        private static void Event_Everyone_OnPlayerHandednessChanged(Dictionary<string, object> message) {
            try {
                Player player = (Player)message["player"];
                if (player.OwnerClientId > REPLAY_PLAYER_OFFSET)
                    return;

                if (player.IsLocalPlayer)
                    SetCurvedStick(player, ClientConfig);
                else
                    SetCurvedStick(player, _playersCurve[player.OwnerClientId]);
            }
            catch (KeyNotFoundException) { }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(Event_Everyone_OnPlayerHandednessChanged)}.\n{ex}");
            }
        }

        /// <summary>
        /// Method called when the client has stopped on the client-side.
        /// Used to reset the config so that it doesn't carry over between servers.
        /// </summary>
        /// <param name="message">Dictionary of string and object, content of the event.</param>
        public static void Event_OnClientStopped(Dictionary<string, object> message) {
            try {
                if (NetworkManager.Singleton == null || ServerFunc.IsDedicatedServer())
                    return;

                ServerConfig = new Configs.ServerConfig();

                _serverHasResponded = false;
                _askServerForStartupDataCount = 0;
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(Event_OnClientStopped)}.\n{ex}");
            }
        }

        public static void AddClientChatMessage(string message) {
            ChatMessage chatMsg = new ChatMessage {
                SteamID = null,
                Username = null,
                Team = null,
                Content = message,
                Timestamp = Utils.GetTimestamp(),
                IsQuickChat = false,
                IsTeamChat = false,
                IsSystem = true,
            };
            ChatManager.Instance.AddChatMessage(chatMsg);
        }

        /// <summary>
        /// Method that launches when the mod is being enabled.
        /// </summary>
        /// <returns>Bool, true if the mod successfully enabled.</returns>
        public bool OnEnable() {
            try {
                Logging.Log($"Enabling...", ServerConfig, true);

                if (Application.version != Constants.CURRENT_APPLICATION_VERSION)
                    Logging.LogWarning($"Server game version is {Application.version} and not {Constants.CURRENT_APPLICATION_VERSION} !");

                _harmony.PatchAll();

                Logging.Log($"Enabled.", ServerConfig, true);

                if (ServerFunc.IsDedicatedServer()) {
                    if (NetworkManager.Singleton != null && NetworkManager.Singleton.CustomMessagingManager != null) {
                        Logging.Log($"RegisterNamedMessageHandler {Constants.FROM_CLIENT_TO_SERVER}.", ServerConfig);
                        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Constants.FROM_CLIENT_TO_SERVER, ReceiveData);
                        _hasRegisteredWithNamedMessageHandler = true;
                    }

                    Logging.Log("Setting server sided config.", ServerConfig, true);
                    ServerConfig = Configs.ServerConfig.ReadConfig();
                }
                else {
                    Logging.Log("Setting client sided config.", ServerConfig, true);
                    ClientConfig = Configs.ClientConfig.ReadConfig();
                }

                Logging.Log("Subscribing to events.", ServerConfig, true);

                if (ServerFunc.IsDedicatedServer()) {
                    EventManager.AddEventListener("Event_Everyone_OnClientConnected", Event_Everyone_OnClientConnected);
                    EventManager.AddEventListener("Event_Everyone_OnClientDisconnected", Event_Everyone_OnClientDisconnected);
                    EventManager.AddEventListener("Event_Everyone_OnGameStateChanged", Event_Everyone_OnGameStateChanged);
                }
                else {
                    EventManager.AddEventListener("Event_OnClientStopped", Event_OnClientStopped);
                }

                EventManager.AddEventListener("Event_Everyone_OnPlayerHandednessChanged", Event_Everyone_OnPlayerHandednessChanged);

                return true;
            }
            catch (Exception ex) {
                Logging.LogError($"Failed to enable.\n{ex}");
                return false;
            }
        }

        /// <summary>
        /// Method that launches when the mod is being disabled.
        /// </summary>
        /// <returns>Bool, true if the mod successfully disabled.</returns>
        public bool OnDisable() {
            try {
                Logging.Log("Unsubscribing from events.", ServerConfig, true);

                if (ServerFunc.IsDedicatedServer()) {
                    EventManager.RemoveEventListener("Event_Everyone_OnClientConnected", Event_Everyone_OnClientConnected);
                    EventManager.RemoveEventListener("Event_Everyone_OnClientDisconnected", Event_Everyone_OnClientDisconnected);
                    EventManager.RemoveEventListener("Event_Everyone_OnGameStateChanged", Event_Everyone_OnGameStateChanged);
                }
                else {
                    EventManager.RemoveEventListener("Event_OnClientStopped", Event_OnClientStopped);
                }

                EventManager.RemoveEventListener("Event_Everyone_OnPlayerHandednessChanged", Event_Everyone_OnPlayerHandednessChanged);

                _hasRegisteredWithNamedMessageHandler = false;
                _serverHasResponded = false;
                _askServerForStartupDataCount = 0;
                _playersCurve.Clear();
                _curvedStickAsset?.DestroyGameObjects();

                Logging.Log($"Disabling...", ServerConfig, true);

                _harmony.UnpatchSelf();

                Logging.Log($"Disabled.", ServerConfig, true);
                return true;
            }
            catch (Exception ex) {
                Logging.LogError($"Failed to disable.\n{ex}");
                return false;
            }
        }

        public static T GetPrivateField<T>(Type typeContainingField, object instanceOfType, string fieldName) {
            if (instanceOfType == null)
                return (T)typeContainingField.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static).GetValue(instanceOfType);
            else
                return (T)typeContainingField.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instanceOfType);
        }

        private class BoneInfo {
            public string name;
            public int parentIndex;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }
    }
}
