using oomtm450PuckMod_CurvedStick.SystemFunc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace oomtm450PuckMod_CurvedStick {
    internal class CurvedStickAsset : MonoBehaviour {
        #region Constants
        private const string ASSETS_FOLDER_PATH1 = "assets";
        private const string ASSETS_FOLDER_PATH2 = "curvedstick";
        private const string ASSETS_EXTENSION = ".unity3d";

        internal const string STICK = "stick";
        internal const string BLADE = "blade";
        internal const string TAPE = "tape";

        internal const string LEFT = "left";
        internal const string RIGHT = "right";
        #endregion

        #region Fields
        private static AssetBundle _assetBundle = null;
        #endregion

        #region Properties
        internal List<string> Errors { get; } = new List<string>();
        internal Dictionary<string, GameObject> Meshes { get; } = new Dictionary<string, GameObject>();
        #endregion

        #region Methods/Functions
        internal void DestroyGameObjects() {
            while (Meshes.Count != 0) {
                var meshObject = Meshes.First();
                Meshes.Remove(meshObject.Key);
            }

            Destroy(gameObject);
        }

        internal void LoadAssets() {
            try {
                if (Meshes.Count != 0)
                    return;

                DontDestroyOnLoad(gameObject);

                string fullPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), ASSETS_FOLDER_PATH1);
                fullPath = Path.Combine(fullPath, ASSETS_FOLDER_PATH2);

                if (!Directory.Exists(fullPath)) {
                    Logging.LogError($"Assets not found at: {fullPath}");
                    return;
                }

                GetAssets(fullPath);
            }
            catch (Exception ex) {
                Logging.LogError($"Error loading Images.\n{ex}");
            }
        }

        private void GetAssets(string path) {
            foreach (string file in Directory.GetFiles(path, "*" + ASSETS_EXTENSION, SearchOption.AllDirectories)) {
                try {
                    string filePath = new Uri(Path.GetFullPath(file)).LocalPath;

                    if (_assetBundle == null)
                        _assetBundle = AssetBundle.LoadFromFile(filePath);

                    new Mesh().UploadMeshData(true);

                    GameObject mesh = _assetBundle.LoadAsset<GameObject>("assets/assetbundle/stick.prefab");
                    Meshes.Add(STICK, mesh);

                    mesh = _assetBundle.LoadAsset<GameObject>("assets/assetbundle/blade.prefab");
                    Meshes.Add(BLADE, mesh);

                    //mesh = _assetBundle.LoadAsset<GameObject>("assets/assetbundle/tape.prefab"); // TODO
                    Meshes.Add(TAPE, null);

                    _assetBundle.Unload(false);

                    break;
                }
                catch (Exception ex) {
                    Errors.Add(ex.ToString());
                }
            }
        }
        #endregion
    }
}
