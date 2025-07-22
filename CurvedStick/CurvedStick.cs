using HarmonyLib;
using oomtm450PuckMod_CurvedStick.Configs;
using oomtm450PuckMod_CurvedStick.SystemFunc;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;

namespace oomtm450PuckMod_CurvedStick {
    /// <summary>
    /// Class containing the main code for the CurvedStick patch.
    /// </summary>
    public class CurvedStick : IPuckMod {
        #region Constants
        /// <summary>
        /// Const string, version of the mod.
        /// </summary>
        private const string MOD_VERSION = "0.1.0DEV2";
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
        #endregion

        /// <summary>
        /// Class that patches the Server_SpawnStick event from Player.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.Server_SpawnStick))]
        public class Player_Server_SpawnStick_Patch {
            [HarmonyPostfix]
            public static void Postfix(Player __instance, Vector3 position, Quaternion rotation, PlayerRole role) {
                try {
                    //if (!ServerFunc.IsDedicatedServer())
                        //return;

                    Logging.Log("Player_Server_SpawnStick_Patch", _serverConfig);

                    // Find parent stickMesh object.
                    GameObject stickMesh = __instance.gameObject.transform.Find("Stick (Attacker)(Clone)").gameObject.transform.Find("Rotation Container").gameObject.transform.Find("Stick Mesh (Attacker)").gameObject;

                    Logging.Log("1", _serverConfig, true);

                    GameObject curvedStickAssetObject = new GameObject(Constants.MOD_NAME + "CurvedStickAsset");
                    CurvedStickAsset curvedStickAsset = curvedStickAssetObject.AddComponent<CurvedStickAsset>();
                    curvedStickAsset.LoadAssets();

                    if (curvedStickAsset.Meshes.Count == 0 || curvedStickAsset.Errors.Count != 0) {
                        if (curvedStickAsset.Meshes.Count == 0)
                            Logging.LogError("No mesh found in the assets.");
                        foreach (string error in curvedStickAsset.Errors)
                            Logging.LogError(error);

                        return;
                    }

                    Logging.Log("2", _serverConfig, true);

                    // Set stick mesh.
                    stickMesh.transform.Find("stick_attacker").gameObject.transform.Find("Stick (Attacker)").gameObject.GetComponent<MeshFilter>().sharedMesh = curvedStickAsset.Meshes["LeftStick"];
                    //stickMesh.transform.Find("stick_attacker").gameObject.transform.Find("Stick (Attacker)").gameObject.GetComponent<MeshFilter>().mesh = null;

                    Logging.Log("3", _serverConfig, true);

                    // Set blade tape mesh.
                    stickMesh.transform.Find("stick_attacker").gameObject.transform.Find("Blade Tape (Attacker)").gameObject.GetComponent<MeshFilter>().sharedMesh = null;
                    //stickMesh.transform.Find("stick_attacker").gameObject.transform.Find("Blade Tape (Attacker)").gameObject.GetComponent<MeshFilter>().mesh = null;

                    Logging.Log("4", _serverConfig, true);

                    // Set blade collider.
                    stickMesh.transform.Find("Puck Colliders").gameObject.transform.Find("Blade").GetComponent<MeshCollider>().sharedMesh = curvedStickAsset.Meshes["LeftBlade"];

                    Logging.Log("5", _serverConfig, true);

                    // Set stick collider.
                    stickMesh.transform.Find("Stick Colliders").gameObject.transform.Find("Blade").GetComponent<MeshCollider>().sharedMesh = curvedStickAsset.Meshes["LeftBlade"];

                    Logging.Log("6", _serverConfig, true);
                }
                catch (Exception ex) {
                    Logging.LogError($"Error in Player_Server_SpawnStick_Patch Postfix().\n{ex}");
                }
            }
        }

        /// <summary>
        /// Method called when a client has "spawned" (joined a server) on the server-side.
        /// Used to send data to the new client that has connected (config and mod version).
        /// </summary>
        /// <param name="message">Dictionary of string and object, content of the event.</param>
        public static void Event_OnPlayerSpawned(Dictionary<string, object> message) {
            if (!ServerFunc.IsDedicatedServer())
                return;
            
            Logging.Log("Event_OnPlayerSpawned", _serverConfig);

            try {
                Player player = (Player)message["player"];

                NetworkCommunication.SendData(Constants.MOD_NAME + "_" + nameof(MOD_VERSION), MOD_VERSION, player.OwnerClientId, Constants.FROM_SERVER, _serverConfig);
            }
            catch (Exception ex) {
                Logging.LogError($"Error in Event_OnPlayerSpawned.\n{ex}");
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
                        if (MOD_VERSION == dataStr) // TODO : Move the kick later so that it doesn't break anything. Maybe even add a chat message and a 3-5 sec wait.
                            break;

                        NetworkCommunication.SendData(Constants.MOD_NAME + "_" + "kick", "1", clientId, Constants.FROM_SERVER, _serverConfig);
                        break;

                    case Constants.MOD_NAME + "_" + "kick": // SERVER-SIDE : Kick the client that asked to be kicked.
                        if (dataStr != "1")
                            break;

                        Logging.Log($"Kicking client {clientId}.", _serverConfig);
                        NetworkManager.Singleton.DisconnectClient(clientId,
                            $"Mod is out of date. Please unsubscribe from {Constants.WORKSHOP_MOD_NAME} in the workshop and restart your game to update.");
                        break;
                }
            }
            catch (Exception ex) {
                Logging.LogError($"Error in ReceiveData.\n{ex}");
            }
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
                    Logging.Log("Setting server sided config.", _serverConfig, true);
                    NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(Constants.FROM_CLIENT, ReceiveData);

                    _serverConfig = ServerConfig.ReadConfig(ServerManager.Instance.AdminSteamIds);
                }
                else {
                    Logging.Log("Setting client sided config.", _serverConfig, true);
                    _clientConfig = ClientConfig.ReadConfig();
                }

                Logging.Log("Subscribing to events.", _serverConfig, true);
                EventManager.Instance.AddEventListener("Event_OnPlayerSpawned", Event_OnPlayerSpawned);

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

                EventManager.Instance.RemoveEventListener("Event_OnPlayerSpawned", Event_OnPlayerSpawned);

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
