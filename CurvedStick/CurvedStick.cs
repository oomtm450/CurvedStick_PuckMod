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
        private const string MOD_VERSION = "0.2.0DEV10";

        private const string ASK_SERVER_FOR_DATA = Constants.MOD_NAME + "ASKDATA";

        private const string HELP_MESSAGE = "Curve stick commands:\n* <b>/curve</b> - Adjust all curve values heel,middle,toe,tip (0-100 0-100 0-100 0-500)\n* <b>/heelcurve</b> - Adjust the curve on the heel (0-100)\n* <b>/middlecurve</b> - Adjust the curve on the middle (0-100)\n* <b>/toecurve</b> - Adjust the curve on the toe (0-100)\n* <b>/tipcurve</b> - Adjust the curve on the tip (0-500)\n";
        #endregion

        #region Fields/Properties
        /// <summary>
        /// Harmony, harmony instance to patch the Puck's code.
        /// </summary>
        private static readonly Harmony _harmony = new Harmony(Constants.MOD_NAME);

        /// <summary>
        /// ServerConfig, config set and sent by the server.
        /// </summary>
        private static ServerConfig ServerConfig { get; set; } = new ServerConfig();

        /// <summary>
        /// ServerConfig, config set by the client.
        /// </summary>
        private static ClientConfig ClientConfig { get; set; } = new ClientConfig();

        private static CurvedStickAsset _curvedStickAsset = null;

        private static readonly LockDictionary<ulong, ClientConfig> _playersCurve = new LockDictionary<ulong, ClientConfig>();

        #region Client-Side
        private static DateTime _lastDateTimeAskStartupData = DateTime.MinValue;

        private static bool _hasRegisteredWithNamedMessageHandler = false;

        private static bool _serverHasResponded = false;

        /// <summary>
        /// Int, number of time client asked the server for startup data.
        /// </summary>
        private static int _askServerForStartupDataCount = 0;
        #endregion

        #region Server-Side
        private static bool _updateAllSticksForReplay = false;
        private static readonly LockList<ulong> _sticksToUpdate = new LockList<ulong>();
        #endregion
        #endregion

        /// <summary>
        /// Class that patches the Update event from ServerManager.
        /// </summary>
        [HarmonyPatch(typeof(ServerManager), "Update")]
        public static class ServerManager_Update_Patch {
            [HarmonyPrefix]
            public static bool Prefix() {
                try {
                    if (!ServerFunc.IsDedicatedServer())
                        return true;

                    if (_updateAllSticksForReplay) {
                        _updateAllSticksForReplay = false;

                        foreach (Player player in PlayerManager.Instance.GetPlayers(true).Where(x => x.IsReplay.Value))
                            SetCurvedStick(player, _playersCurve[player.OwnerClientId]);

                        NetworkCommunication.SendDataToAll(nameof(SetCurvedStick) + "ALLREPLAY", "1", Constants.FROM_SERVER_TO_CLIENT, ServerConfig);
                    }
                    else {
                        List<ulong> sticksToUpdate = new List<ulong>(_sticksToUpdate);
                        _sticksToUpdate.Clear();
                        foreach (ulong clientId in sticksToUpdate) {
                            Player player = PlayerManager.Instance.GetPlayerByClientId(clientId);
                            if (player == null || !player)
                                continue;

                            SetCurvedStick(player, _playersCurve[clientId]);
                            NetworkCommunication.SendDataToAll(nameof(SetCurvedStick), $"{clientId};{FormatCurveStickForCommunication(_playersCurve[clientId])}", Constants.FROM_SERVER_TO_CLIENT, ServerConfig);
                        }
                    }
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(ServerManager_Update_Patch)} Prefix().\n{ex}");
                }

                return true;
            }
        }

        /*/// <summary>
        /// Class that patches the UpdateStick event from Stick.
        /// </summary>
        [HarmonyPatch(typeof(Stick), nameof(Stick.UpdateStick))]
        public static class Stick_UpdateStick_Patch {
            [HarmonyPostfix]
            public static void Postfix(Stick __instance) {
                try {
                    if (!ServerFunc.IsDedicatedServer())
                        return;

                    Logging.Log("Stick_UpdateStick_Patch", ServerConfig);
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
        public static class Player_Server_SpawnStick_Patch {
            [HarmonyPostfix]
            public static void Postfix(Player __instance, Vector3 position, Quaternion rotation, PlayerRole role) {
                try {
                    if (!ServerFunc.IsDedicatedServer())
                        return;

                    new Timer(UpdateStickTimerCallback, __instance.OwnerClientId, 150, Timeout.Infinite);
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(Player_Server_SpawnStick_Patch)} Postfix().\n{ex}");
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
                        //Logging.Log($"RegisterNamedMessageHandler {Constants.FROM_SERVER}.", ClientConfig);
                        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Constants.FROM_SERVER_TO_CLIENT, ReceiveData);
                        _hasRegisteredWithNamedMessageHandler = true;

                        DateTime now = DateTime.UtcNow;
                        if (_lastDateTimeAskStartupData + TimeSpan.FromSeconds(1) < now && _askServerForStartupDataCount++ < 10) {
                            _lastDateTimeAskStartupData = now;
                            NetworkCommunication.SendData(ASK_SERVER_FOR_DATA, "1", NetworkManager.ServerClientId, Constants.FROM_CLIENT_TO_SERVER, ClientConfig);
                            SendNewCurvedStickValues();
                        }
                    }
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(UIScoreboard_UpdatePlayer_Patch)} Postfix().\n{ex}");
                }
            }
        }

        /// <summary>
        /// Class that patches the Client_SendClientChatMessage event from UIChat.
        /// </summary>
        [HarmonyPatch(typeof(UIChat), nameof(UIChat.Client_SendClientChatMessage))]
        public class UIChat_Client_SendClientChatMessage_Patch {
            [HarmonyPrefix]
            public static bool Prefix(string message, bool useTeamChat) {
                try {
                    // If this is the server, do not use the patch.
                    if (ServerFunc.IsDedicatedServer())
                        return true;

                    if (message.StartsWith(@"/")) {
                        message = message.ToLowerInvariant();

                        bool changeCurve = false;
                        if (message.StartsWith(@"/curve")) {
                            message = message.Replace(@"/curve", "").Trim();

                            if (string.IsNullOrEmpty(message))
                                UIChat.Instance.AddChatMessage($"The curve is {FormatCurveStickForCommunication(ClientConfig).Replace(';', ' ')}");
                            else {
                                string[] splittedMessageCurve = message.Split(' ');
                                for (int i = 0; i < splittedMessageCurve.Length; i++) {
                                    if (int.TryParse(splittedMessageCurve[i], out int curveValue)) {
                                        if (i == 3) {
                                            if (curveValue > 500) // 0.5
                                                curveValue = 500;
                                            else if (curveValue < 0)
                                                curveValue = 0;
                                        }
                                        else {
                                            if (curveValue > 100) // 0.1
                                                curveValue = 100;
                                            else if (curveValue < 0)
                                                curveValue = 0;
                                        }

                                        switch (i) {
                                            case 0:
                                                ClientConfig.HeelCurve = curveValue;
                                                break;

                                            case 1:
                                                ClientConfig.MiddleCurve = curveValue;
                                                break;

                                            case 2:
                                                ClientConfig.ToeCurve = curveValue;
                                                break;

                                            case 3:
                                                ClientConfig.TipCurve = curveValue;
                                                break;
                                        }

                                        changeCurve = true;
                                    }
                                }
                            }
                        }
                        else if (message.StartsWith(@"/heelcurve")) {
                            message = message.Replace(@"/heelcurve", "").Trim();

                            if (string.IsNullOrEmpty(message))
                                UIChat.Instance.AddChatMessage($"The heel curve is {ClientConfig.HeelCurve}");
                            else {
                                if (int.TryParse(message, out int heelCurveValue)) {
                                    if (heelCurveValue > 100) // 0.1
                                        heelCurveValue = 100;
                                    else if (heelCurveValue < 0)
                                        heelCurveValue = 0;

                                    ClientConfig.HeelCurve = heelCurveValue;
                                    changeCurve = true;
                                }
                            }
                        }
                        else if (message.StartsWith(@"/middlecurve")) {
                            message = message.Replace(@"/middlecurve", "").Trim();

                            if (string.IsNullOrEmpty(message))
                                UIChat.Instance.AddChatMessage($"The middle curve is {ClientConfig.MiddleCurve}");
                            else {
                                if (int.TryParse(message, out int middleCurveValue)) {
                                    if (middleCurveValue > 100) // 0.1
                                        middleCurveValue = 100;
                                    else if (middleCurveValue < 0)
                                        middleCurveValue = 0;

                                    ClientConfig.MiddleCurve = middleCurveValue;
                                    changeCurve = true;
                                }
                            }
                        }
                        else if (message.StartsWith(@"/toecurve")) {
                            message = message.Replace(@"/toecurve", "").Trim();

                            if (string.IsNullOrEmpty(message))
                                UIChat.Instance.AddChatMessage($"The toe curve is {ClientConfig.ToeCurve}");
                            else {
                                if (int.TryParse(message, out int toeCurveValue)) {
                                    if (toeCurveValue > 100) // 0.1
                                        toeCurveValue = 100;
                                    else if (toeCurveValue < 0)
                                        toeCurveValue = 0;

                                    ClientConfig.ToeCurve = toeCurveValue;
                                    changeCurve = true;
                                }
                            }
                        }
                        else if (message.StartsWith(@"/tipcurve")) {
                            message = message.Replace(@"/tipcurve", "").Trim();

                            if (string.IsNullOrEmpty(message))
                                UIChat.Instance.AddChatMessage($"The tip curve is {ClientConfig.TipCurve}");
                            else {
                                if (int.TryParse(message, out int tipCurveValue)) {
                                    if (tipCurveValue > 500) // 0.5
                                        tipCurveValue = 500;
                                    else if (tipCurveValue < 0)
                                        tipCurveValue = 0;

                                    ClientConfig.TipCurve = tipCurveValue;
                                    changeCurve = true;
                                }
                            }
                        }

                        if (changeCurve)
                            SendNewCurvedStickValues();
                    }
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(UIChat_Client_SendClientChatMessage_Patch)} Prefix().\n{ex}");
                }

                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(string message, bool useTeamChat) {
                try {
                    // If this is the server, do not use the patch.
                    if (ServerFunc.IsDedicatedServer())
                        return;

                    if (message.StartsWith(@"/")) {
                        message = message.ToLowerInvariant();

                        if (message.StartsWith(@"/help") || message.StartsWith(@"/curvehelp"))
                            UIChat.Instance.AddChatMessage(HELP_MESSAGE);
                    }
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in {nameof(UIChat_Client_SendClientChatMessage_Patch)} Postfix().\n{ex}");
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

            Logging.Log("Event_OnClientConnected", ServerConfig);

            try {
                if (NetworkManager.Singleton != null && !_hasRegisteredWithNamedMessageHandler) {
                    Logging.Log($"RegisterNamedMessageHandler {Constants.FROM_CLIENT_TO_SERVER}.", ServerConfig);
                    NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Constants.FROM_CLIENT_TO_SERVER, ReceiveData);
                    _hasRegisteredWithNamedMessageHandler = true;
                }
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(Event_OnClientConnected)}.\n{ex}");
            }
        }

        /// <summary>
        /// Method called when a client has connected (joined a server) on the server-side.
        /// Used to set server-sided stuff after the game has loaded.
        /// </summary>
        /// <param name="message">Dictionary of string and object, content of the event.</param>
        public static void Event_OnClientDisconnected(Dictionary<string, object> message) {
            if (!ServerFunc.IsDedicatedServer())
                return;

            Logging.Log("Event_OnClientDisconnected", ServerConfig);

            try {
                ulong clientId = (ulong)message["clientId"];

                //_sentOutOfDateMessage.Remove(clientId);
                _playersCurve.Remove(clientId);
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(Event_OnClientDisconnected)}.\n{ex}");
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
                    (dataName, dataStr) = NetworkCommunication.GetData(clientId, reader, ClientConfig);
                }
                else {
                    (dataName, dataStr) = NetworkCommunication.GetData(clientId, reader, ServerConfig);
                }

                switch (dataName) {
                    case Constants.MOD_NAME + "_" + nameof(MOD_VERSION): // CLIENT-SIDE : Mod version check, kick if client and server versions are not the same.
                        _serverHasResponded = true;

                        if (MOD_VERSION == dataStr) // TODO : Move the kick later so that it doesn't break anything. Maybe even add a chat message and a 3-5 sec wait.
                            break;

                        NetworkCommunication.SendData(Constants.MOD_NAME + "_" + "kick", "1", clientId, Constants.FROM_CLIENT_TO_SERVER, ServerConfig);
                        break;

                    case nameof(SetCurvedStick):
                        string[] splittedDataStrSetCurvedStick = dataStr.Split(';');
                        if (!_playersCurve.TryGetValue(clientId, out ClientConfig curveSetCurvedStick)) {
                            curveSetCurvedStick = new ClientConfig();
                            _playersCurve.Add(clientId, curveSetCurvedStick);
                        }

                        curveSetCurvedStick.HeelCurve = int.Parse(splittedDataStrSetCurvedStick[1]);
                        curveSetCurvedStick.MiddleCurve = int.Parse(splittedDataStrSetCurvedStick[2]);
                        curveSetCurvedStick.ToeCurve = int.Parse(splittedDataStrSetCurvedStick[3]);
                        curveSetCurvedStick.TipCurve = int.Parse(splittedDataStrSetCurvedStick[4]);

                        Player player = PlayerManager.Instance.GetPlayerByClientId(ulong.Parse(splittedDataStrSetCurvedStick[0]));
                        if (player == null || !player)
                            return;

                        SetCurvedStick(player, curveSetCurvedStick);
                        break;

                    case nameof(SetCurvedStick) + "ALLREPLAY":
                        if (dataStr != "1")
                            return;

                        foreach (Player _player in PlayerManager.Instance.GetPlayers(true).Where(x => x.IsReplay.Value))
                            SetCurvedStick(_player, _playersCurve[_player.OwnerClientId]);
                        break;

                    case Constants.MOD_NAME + "_" + "kick": // SERVER-SIDE : Kick the client that asked to be kicked.
                        if (dataStr != "1")
                            break;

                        Logging.Log($"Kicking client {clientId}.", ServerConfig);
                        NetworkManager.Singleton.DisconnectClient(clientId,
                            $"Mod is out of date. Please unsubscribe from {Constants.WORKSHOP_MOD_NAME} in the workshop and restart your game to update.");
                        break;

                    case ASK_SERVER_FOR_DATA: // SERVER-SIDE : Send the necessary data to client.
                        if (dataStr != "1")
                            break;

                        NetworkCommunication.SendData(Constants.MOD_NAME + "_" + nameof(MOD_VERSION), MOD_VERSION, clientId, Constants.FROM_SERVER_TO_CLIENT, ServerConfig);
                        foreach (KeyValuePair<ulong, ClientConfig> curve in _playersCurve)
                            NetworkCommunication.SendData(nameof(SetCurvedStick), $"{curve.Key};{FormatCurveStickForCommunication(curve.Value)}", clientId, Constants.FROM_SERVER_TO_CLIENT, ServerConfig);
                        break;

                    case Constants.NEW_CURVED_STICK_VALUES: // SERVER-SIDE : Receive new stick values and asks everyone to update it.
                        string[] splittedDataStrNewCurveStickValues = dataStr.Split(';');
                        if (!_playersCurve.TryGetValue(clientId, out ClientConfig curveNewCurveStickValues)) {
                            curveNewCurveStickValues = new ClientConfig();
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
                Logging.LogError($"Error in ReceiveData.\n{ex}");
            }
        }

        private static void UpdateStickTimerCallback(object stateInfo) {
            _sticksToUpdate.Add((ulong)stateInfo);
        }

        private static void UpdateAllSticksForReplayTimerCallback(object stateInfo) {
            _updateAllSticksForReplay = true;
        }

        private static void SetCurvedStick(Player player, ClientConfig curve) {
            if (!player || player.Role.Value != PlayerRole.Attacker)
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

            string handedness;
            if (player.Handedness.Value == PlayerHandedness.Right)
                handedness = CurvedStickAsset.RIGHT;
            else
                handedness = CurvedStickAsset.LEFT;

            if (!ServerFunc.IsDedicatedServer()) {
                SkinnedMeshRenderer prefabSkinnedMeshRendererStick = _curvedStickAsset.Meshes[CurvedStickAsset.STICK].GetComponentInChildren<SkinnedMeshRenderer>();

                SetCurvedStickMagicClient(stickMesh, prefabSkinnedMeshRendererStick, curve, handedness);
            }
            else {
                SkinnedMeshRenderer prefabSkinnedMeshRendererBlade = _curvedStickAsset.Meshes[CurvedStickAsset.BLADE].GetComponentInChildren<SkinnedMeshRenderer>();

                // Set blade collider for puck.
                GameObject bladePuckGameObject = stickMesh.transform.Find("Puck Colliders").gameObject.transform.Find("Blade").gameObject;
                SetCurvedStickMagicServer(bladePuckGameObject, prefabSkinnedMeshRendererBlade, curve, handedness);

                // Set blade collider for stick.
                GameObject bladeStickGameObject = stickMesh.transform.Find("Stick Colliders").gameObject.transform.Find("Blade").gameObject;
                SetCurvedStickMagicServer(bladeStickGameObject, prefabSkinnedMeshRendererBlade, curve, handedness);
            }
        }

        private static void SetCurvedStickMagicClient(GameObject stickMesh, SkinnedMeshRenderer prefabSkinnedMeshRenderer, ClientConfig curve,
            string handedness) {
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

                skinnedMeshRenderer.bones = newBones;
                skinnedMeshRenderer.rootBone = skinnedMeshRenderer.bones.First(x => x.name.StartsWith("Base"));
                skinnedMeshRenderer.sharedMaterials = originalMeshRendererSharedMaterials;

                skinnedMeshRenderer.rootBone.SetParent(stickGameObject.transform, false);
            }

            // Set stick mesh values.
            SetTransformRotationForCurve(skinnedMeshRenderer, handedness, curve);

            // TODO : Set blade tape mesh.
            stickAttackerGameObject.transform.Find("Blade Tape (Attacker)").gameObject.GetComponent<MeshFilter>().sharedMesh = null;
            // TODO : Set tape mesh values.
        }

        private static void SetCurvedStickMagicServer(GameObject gameObject, SkinnedMeshRenderer prefabSkinnedMeshRenderer, ClientConfig curve,
            string handedness) {
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
                        newBones[i].SetParent(gameObject.transform, false);

                    newBones[i].localPosition = boneInfo.Values.First(x => x.parentIndex == i).localPosition;
                    newBones[i].localRotation = boneInfo.Values.First(x => x.parentIndex == i).localRotation;
                    newBones[i].localScale = boneInfo.Values.First(x => x.parentIndex == i).localScale;
                }

                skinnedMeshRenderer.bones = newBones;
                skinnedMeshRenderer.rootBone = skinnedMeshRenderer.bones.First(x => x.name.StartsWith("Base"));

                skinnedMeshRenderer.rootBone.SetParent(gameObject.transform, false);
            }

            // Set stick mesh values.
            SetTransformRotationForCurve(skinnedMeshRenderer, handedness, curve);

            Mesh colliderMesh = new Mesh();
            skinnedMeshRenderer.BakeMesh(colliderMesh);
            meshCollider.sharedMesh = colliderMesh;
        }

        private static void SetTransformRotationForCurve(SkinnedMeshRenderer skinnedMeshRenderer, string handedness, ClientConfig curve) {
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

        private static Mesh DuplicateMesh(Mesh sourceMesh) {
            Mesh targetMesh = new Mesh();
            targetMesh.name = sourceMesh.name + "_" + new System.Random().Next(1000000);
            targetMesh.vertices = sourceMesh.vertices;
            targetMesh.normals = sourceMesh.normals;
            targetMesh.tangents = sourceMesh.tangents;
            targetMesh.triangles = sourceMesh.triangles;
            targetMesh.uv = sourceMesh.uv;
            targetMesh.colors = sourceMesh.colors;
            targetMesh.subMeshCount = sourceMesh.subMeshCount;
            targetMesh.bindposes = sourceMesh.bindposes;
            targetMesh.boneWeights = sourceMesh.boneWeights;

            for (int i = 0; i < targetMesh.subMeshCount; ++i) {
                targetMesh.SetSubMesh(i, sourceMesh.GetSubMesh(i));
            }

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

        private static string FormatCurveStickForCommunication(ClientConfig config) {
            return $"{config.HeelCurve};{config.MiddleCurve};{config.ToeCurve};{config.TipCurve}";
        }

        private static void Event_OnPlayerHandednessChanged(Dictionary<string, object> message) {
            try {
                Player player = (Player)message["player"];
                SetCurvedStick(player, _playersCurve[player.OwnerClientId]);
            }
            catch (Exception ex) {
                Logging.LogError($"Error in {nameof(Event_OnPlayerHandednessChanged)}.\n{ex}");
            }
        }

        /// <summary>
        /// Method called when the client has stopped on the client-side.
        /// Used to reset the config so that it doesn't carry over between servers.
        /// </summary>
        /// <param name="message">Dictionary of string and object, content of the event.</param>
        public static void Event_Client_OnClientStopped(Dictionary<string, object> message) {
            if (NetworkManager.Singleton == null || ServerFunc.IsDedicatedServer())
                return;

            try {
                ServerConfig = new ServerConfig();

                _serverHasResponded = false;
                _askServerForStartupDataCount = 0;
            }
            catch (Exception ex) {
                Logging.LogError($"Error in Event_Client_OnClientStopped.\n{ex}");
            }
        }

        /// <summary>
        /// Method that launches when the mod is being enabled.
        /// </summary>
        /// <returns>Bool, true if the mod successfully enabled.</returns>
        public bool OnEnable() {
            try {
                Logging.Log($"Enabling...", ServerConfig, true);

                _harmony.PatchAll();

                Logging.Log($"Enabled.", ServerConfig, true);

                if (ServerFunc.IsDedicatedServer()) {
                    if (NetworkManager.Singleton != null && NetworkManager.Singleton.CustomMessagingManager != null) {
                        Logging.Log($"RegisterNamedMessageHandler {Constants.FROM_CLIENT_TO_SERVER}.", ServerConfig);
                        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Constants.FROM_CLIENT_TO_SERVER, ReceiveData);
                        _hasRegisteredWithNamedMessageHandler = true;
                    }

                    Logging.Log("Setting server sided config.", ServerConfig, true);
                    ServerConfig = ServerConfig.ReadConfig(ServerManager.Instance.AdminSteamIds);
                }
                else {
                    Logging.Log("Setting client sided config.", ServerConfig, true);
                    ClientConfig = ClientConfig.ReadConfig();
                }

                Logging.Log("Subscribing to events.", ServerConfig, true);

                if (ServerFunc.IsDedicatedServer()) {
                    EventManager.Instance.AddEventListener("Event_OnClientConnected", Event_OnClientConnected);
                    EventManager.Instance.AddEventListener("Event_OnClientDisconnected", Event_OnClientDisconnected);
                    EventManager.Instance.AddEventListener("Event_OnGamePhaseChanged", Event_OnGamePhaseChanged);
                }
                else {
                    EventManager.Instance.AddEventListener("Event_Client_OnClientStopped", Event_Client_OnClientStopped);
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
                Logging.Log("Unsubscribing from events.", ServerConfig, true);

                if (ServerFunc.IsDedicatedServer()) {
                    EventManager.Instance.RemoveEventListener("Event_OnClientConnected", Event_OnClientConnected);
                    EventManager.Instance.RemoveEventListener("Event_OnClientDisconnected", Event_OnClientDisconnected);
                    EventManager.Instance.RemoveEventListener("Event_OnGamePhaseChanged", Event_OnGamePhaseChanged);
                }
                else {
                    EventManager.Instance.RemoveEventListener("Event_Client_OnClientStopped", Event_Client_OnClientStopped);
                }

                EventManager.Instance.RemoveEventListener("Event_OnPlayerHandednessChanged", Event_OnPlayerHandednessChanged);

                _hasRegisteredWithNamedMessageHandler = false;
                _serverHasResponded = false;
                _askServerForStartupDataCount = 0;
                _playersCurve.Clear();

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

        private class BoneInfo {
            public string name;
            public int parentIndex;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }
    }
}
