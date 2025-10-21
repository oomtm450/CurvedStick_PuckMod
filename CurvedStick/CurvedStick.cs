using HarmonyLib;
using oomtm450PuckMod_CurvedStick.Configs;
using oomtm450PuckMod_CurvedStick.SystemFunc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.Netcode;
using UnityEngine;

namespace oomtm450PuckMod_CurvedStick {
    /// <summary>
    /// Class containing the main code for the CurvedStick patch.
    /// </summary>
    public class CurvedStick : IPuckMod {
        #region Constants
        /// <summary>
        /// Const string, version of the mod.
        /// </summary>
        private const string MOD_VERSION = "0.1.0DEV15";

        private const string ASK_SERVER_FOR_DATA = Constants.MOD_NAME + "ASKDATA";
        #endregion

        #region Fields
        /// <summary>
        /// Harmony, harmony instance to patch the Puck's code.
        /// </summary>
        private static readonly Harmony _harmony = new Harmony(Constants.MOD_NAME);

        /// <summary>
        /// ServerConfig, config set and sent by the server.
        /// </summary>
        private static ServerConfig _serverConfig = new ServerConfig();

        /// <summary>
        /// ServerConfig, config set by the client.
        /// </summary>
        private static ClientConfig _clientConfig = new ClientConfig();

        private static CurvedStickAsset _curvedStickAsset;

        #region Client-Side
        private static DateTime _lastDateTimeAskData = DateTime.MinValue;

        private static bool _hasRegisteredWithNamedMessageHandler = false;

        private static bool _serverHasResponded = false;
        #endregion

        #region Server-Side
        private static bool _updateAllSticksForReplay = false;
        private static readonly LockList<string> _sticksToUpdate = new LockList<string>();
        #endregion
        #endregion

        /// <summary>
        /// Class that patches the Update event from ServerManager.
        /// </summary>
        [HarmonyPatch(typeof(ServerManager), "Update")]
        public class ServerManager_Update_Patch {
            [HarmonyPostfix]
            public static bool Prefix() {
                try {
                    if (!ServerFunc.IsDedicatedServer())
                        return true;

                    if (_updateAllSticksForReplay) {
                        _updateAllSticksForReplay = false;

                        foreach (Player player in PlayerManager.Instance.GetPlayers(true).Where(x => x.IsReplay.Value))
                            SetCurvedStick(player);

                        NetworkCommunication.SendDataToAll(nameof(SetCurvedStick) + "ALLREPLAY", "1", Constants.FROM_SERVER, _serverConfig);
                    }
                    else {
                        foreach (string steamId in _sticksToUpdate) {
                            Player player = PlayerManager.Instance.GetPlayerBySteamId(steamId);
                            if (player == null || !player)
                                continue;

                            SetCurvedStick(PlayerManager.Instance.GetPlayerBySteamId(steamId));
                            NetworkCommunication.SendDataToAll(nameof(SetCurvedStick), steamId, Constants.FROM_SERVER, _serverConfig);
                        }
                    }

                    _sticksToUpdate.Clear();
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in Stick_UpdateStick_Patch Postfix().\n{ex}");
                }

                return true;
            }
        }

        /*/// <summary>
        /// Class that patches the UpdateStick event from Stick.
        /// </summary>
        [HarmonyPatch(typeof(Stick), nameof(Stick.UpdateStick))]
        public class Stick_UpdateStick_Patch {
            [HarmonyPostfix]
            public static void Postfix(Stick __instance) {
                try {
                    if (!ServerFunc.IsDedicatedServer())
                        return;

                    Logging.Log("Stick_UpdateStick_Patch", _serverConfig);
                    SetCurvedStick(__instance.Player);
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in Stick_UpdateStick_Patch Postfix().\n{ex}");
                }
            }
        }*/

        /// <summary>
        /// Class that patches the Server_SpawnStick event from Player.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.Server_SpawnStick))]
        public class Player_Server_SpawnStick_Patch {
            [HarmonyPostfix]
            public static void Postfix(Player __instance, Vector3 position, Quaternion rotation, PlayerRole role) {
                try {
                    if (!ServerFunc.IsDedicatedServer())
                        return;

                    Logging.Log("Player_Server_SpawnStick_Patch", _serverConfig);
                    new Timer(UpdateStickTimerCallback, __instance.SteamId.Value.ToString(), 100, Timeout.Infinite);
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in Player_Server_SpawnStick_Patch Postfix().\n{ex}");
                }
            }
        }

        /// <summary>
        /// Class that patches the UpdatePlayer event from UIScoreboard.
        /// </summary>
        [HarmonyPatch(typeof(UIScoreboard), nameof(UIScoreboard.UpdatePlayer))]
        public class UIScoreboard_UpdatePlayer_Patch {
            [HarmonyPostfix]
            public static void Postfix(Player player) {
                try {
                    // If this is the server, do not use the patch.
                    if (ServerFunc.IsDedicatedServer())
                        return;

                    if (!_hasRegisteredWithNamedMessageHandler || !_serverHasResponded) {
                        //Logging.Log($"RegisterNamedMessageHandler {Constants.FROM_SERVER}.", _clientConfig);
                        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Constants.FROM_SERVER, ReceiveData);
                        _hasRegisteredWithNamedMessageHandler = true;

                        DateTime now = DateTime.UtcNow;
                        if (_lastDateTimeAskData + TimeSpan.FromSeconds(1) < now) {
                            _lastDateTimeAskData = now;
                            NetworkCommunication.SendData(ASK_SERVER_FOR_DATA, "1", NetworkManager.ServerClientId, Constants.FROM_CLIENT, _clientConfig);
                        }
                    }
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in UIScoreboard_UpdateServer_Patch Postfix().\n{ex}");
                }
            }
        }

        /// <summary>
        /// Method called when a client has connected (joined a server) on the server-side.
        /// Used to set server-sided stuff after the game has loaded.
        /// </summary>
        /// <param name="message">Dictionary of string and object, content of the event.</param>
        public static void Event_OnGamePhaseChanged(Dictionary<string, object> message) {
            if (!ServerFunc.IsDedicatedServer() || (GamePhase)message["newGamePhase"] != GamePhase.Replay)
                return;

            new Timer(UpdateAllSticksForReplayTimerCallback, null, 500, Timeout.Infinite);
        }

        /// <summary>
        /// Method called when a client has connected (joined a server) on the server-side.
        /// Used to set server-sided stuff after the game has loaded.
        /// </summary>
        /// <param name="message">Dictionary of string and object, content of the event.</param>
        public static void Event_OnClientConnected(Dictionary<string, object> message) {
            if (!ServerFunc.IsDedicatedServer())
                return;

            Logging.Log("Event_OnClientConnected", _serverConfig);

            try {
                if (NetworkManager.Singleton != null && !_hasRegisteredWithNamedMessageHandler) {
                    Logging.Log($"RegisterNamedMessageHandler {Constants.FROM_CLIENT}.", _serverConfig);
                    NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Constants.FROM_CLIENT, ReceiveData);
                    _hasRegisteredWithNamedMessageHandler = true;
                }
            }
            catch (Exception ex) {
                Logging.LogError($"Error in Event_OnClientConnected.\n{ex}");
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
                if (clientId == NetworkManager.ServerClientId) { // If client Id is 0, we received data from the server, so we are client-sided.
                    //Logging.Log("ReceiveData", _clientConfig);
                    (dataName, dataStr) = NetworkCommunication.GetData(clientId, reader, _clientConfig);
                }
                else {
                    //Logging.Log("ReceiveData", _serverConfig);
                    (dataName, dataStr) = NetworkCommunication.GetData(clientId, reader, _serverConfig);
                }

                switch (dataName) {
                    case Constants.MOD_NAME + "_" + nameof(MOD_VERSION): // CLIENT-SIDE : Mod version check, kick if client and server versions are not the same.
                        _serverHasResponded = true;

                        if (MOD_VERSION == dataStr) // TODO : Move the kick later so that it doesn't break anything. Maybe even add a chat message and a 3-5 sec wait.
                            break;

                        NetworkCommunication.SendData(Constants.MOD_NAME + "_" + "kick", "1", clientId, Constants.FROM_CLIENT, _serverConfig);
                        break;

                    case nameof(SetCurvedStick):
                        if (ServerFunc.IsDedicatedServer())
                            return;

                        Player player = PlayerManager.Instance.GetPlayerBySteamId(dataStr);
                        if (player == null || !player)
                            return;

                        SetCurvedStick(player);
                        break;

                    case nameof(SetCurvedStick) + "ALLREPLAY":
                        if (ServerFunc.IsDedicatedServer() || dataStr != "1")
                            return;

                        foreach (Player _player in PlayerManager.Instance.GetPlayers(true).Where(x => x.IsReplay.Value))
                            SetCurvedStick(_player);
                        break;

                    case Constants.MOD_NAME + "_" + "kick": // SERVER-SIDE : Kick the client that asked to be kicked.
                        if (dataStr != "1")
                            break;

                        Logging.Log($"Kicking client {clientId}.", _serverConfig);
                        NetworkManager.Singleton.DisconnectClient(clientId,
                            $"Mod is out of date. Please unsubscribe from {Constants.WORKSHOP_MOD_NAME} in the workshop and restart your game to update.");
                        break;

                    case ASK_SERVER_FOR_DATA: // SERVER-SIDE : Send the necessary data to client.
                        if (dataStr != "1")
                            break;

                        NetworkCommunication.SendData(Constants.MOD_NAME + "_" + nameof(MOD_VERSION), MOD_VERSION, clientId, Constants.FROM_SERVER, _serverConfig);
                        break;
                }
            }
            catch (Exception ex) {
                Logging.LogError($"Error in ReceiveData.\n{ex}");
            }
        }

        private static void UpdateStickTimerCallback(object stateInfo) {
            _sticksToUpdate.Add((string)stateInfo);
        }

        private static void UpdateAllSticksForReplayTimerCallback(object stateInfo) {
            _updateAllSticksForReplay = true;
        }

        private static void SetCurvedStick(Player player) {
            if (!player || player.Role.Value != PlayerRole.Attacker)
                return;

            GameObject curvedStickAssetObject = new GameObject(Constants.MOD_NAME + "CurvedStickAsset");
            _curvedStickAsset = curvedStickAssetObject.AddComponent<CurvedStickAsset>();
            _curvedStickAsset.LoadAssets();

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

            string handedness;
            if (player.Handedness.Value == PlayerHandedness.Right)
                handedness = CurvedStickAsset.RIGHT;
            else
                handedness = CurvedStickAsset.LEFT;

            GameObject stickMesh = stickMeshTransform.gameObject;

            if (!ServerFunc.IsDedicatedServer()) {
                GameObject stickAttackerGameObject = stickMesh.transform.Find("stick_attacker").gameObject;

                // Set stick mesh.
                GameObject stickGameObject = stickAttackerGameObject.transform.Find("Stick (Attacker)").gameObject;
                stickGameObject.GetComponent<MeshFilter>().sharedMesh = _curvedStickAsset.Meshes[handedness + CurvedStickAsset.STICK];

                // Set blade tape mesh.
                stickAttackerGameObject.transform.Find("Blade Tape (Attacker)").gameObject.GetComponent<MeshFilter>().sharedMesh = _curvedStickAsset.Meshes[handedness + CurvedStickAsset.TAPE];
            }

            // Set blade collider for puck.
            GameObject bladePuckGameObject = stickMesh.transform.Find("Puck Colliders").gameObject.transform.Find("Blade").gameObject;
            MeshCollider bladePuckMeshCollider = bladePuckGameObject.GetComponent<MeshCollider>();
            bladePuckMeshCollider.convex = true;
            bladePuckMeshCollider.sharedMesh = _curvedStickAsset.Meshes[handedness + CurvedStickAsset.BLADE];
            //MeshFilter mf = stickMesh.transform.Find("Puck Colliders").gameObject.transform.Find("Blade").gameObject.AddComponent<MeshFilter>();
            //mf.sharedMesh = _curvedStickAsset.Meshes[handedness + CurvedStickAsset.BLADE];
            //MeshRenderer mr = stickMesh.transform.Find("Puck Colliders").gameObject.transform.Find("Blade").gameObject.AddComponent<MeshRenderer>();
            //mr.material = new Material(stickMesh.transform.Find("stick_attacker").gameObject.transform.Find("Stick (Attacker)").gameObject.GetComponent<MeshRenderer>().material) {
            //    color = new Color(1, 0, 0, 0.8f),
            //};

            // Set blade collider for stick.
            GameObject bladeStickGameObject = stickMesh.transform.Find("Stick Colliders").gameObject.transform.Find("Blade").gameObject;
            MeshCollider bladeStickMeshCollider = bladeStickGameObject.GetComponent<MeshCollider>();
            bladeStickMeshCollider.convex = true;
            bladeStickMeshCollider.sharedMesh = _curvedStickAsset.Meshes[handedness + CurvedStickAsset.BLADE];
        }

        private static void Event_OnPlayerHandednessChanged(Dictionary<string, object> message) {
            SetCurvedStick((Player)message["player"]);
        }

        /// <summary>
        /// Method that launches when the mod is being enabled.
        /// </summary>
        /// <returns>Bool, true if the mod successfully enabled.</returns>
        public bool OnEnable() {
            try {
                Logging.Log($"Enabling...", _serverConfig, true);

                _harmony.PatchAll();

                Logging.Log($"Enabled.", _serverConfig, true);

                if (ServerFunc.IsDedicatedServer()) {
                    if (NetworkManager.Singleton != null && NetworkManager.Singleton.CustomMessagingManager != null) {
                        Logging.Log($"RegisterNamedMessageHandler {Constants.FROM_CLIENT}.", _serverConfig);
                        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Constants.FROM_CLIENT, ReceiveData);
                        _hasRegisteredWithNamedMessageHandler = true;
                    }

                    Logging.Log("Setting server sided config.", _serverConfig, true);
                    _serverConfig = ServerConfig.ReadConfig(ServerManager.Instance.AdminSteamIds);
                }
                else {
                    Logging.Log("Setting client sided config.", _serverConfig, true);
                    _clientConfig = ClientConfig.ReadConfig();
                }

                Logging.Log("Subscribing to events.", _serverConfig, true);

                if (ServerFunc.IsDedicatedServer()) {
                    EventManager.Instance.AddEventListener("Event_OnClientConnected", Event_OnClientConnected);
                    EventManager.Instance.AddEventListener("Event_OnGamePhaseChanged", Event_OnGamePhaseChanged);
                }
                EventManager.Instance.AddEventListener("Event_OnPlayerHandednessChanged", Event_OnPlayerHandednessChanged);

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
                Logging.Log("Unsubscribing from events.", _serverConfig, true);

                if (ServerFunc.IsDedicatedServer()) {
                    EventManager.Instance.RemoveEventListener("Event_OnClientConnected", Event_OnClientConnected);
                    EventManager.Instance.RemoveEventListener("Event_OnGamePhaseChanged", Event_OnGamePhaseChanged);
                }
                EventManager.Instance.RemoveEventListener("Event_OnPlayerHandednessChanged", Event_OnPlayerHandednessChanged);

                Logging.Log($"Disabling...", _serverConfig, true);

                _harmony.UnpatchSelf();

                Logging.Log($"Disabled.", _serverConfig, true);
                return true;
            }
            catch (Exception ex) {
                Logging.LogError($"Failed to disable.\n{ex}");
                return false;
            }
        }
    }
}
