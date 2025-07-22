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
        private const string ASSETS_FOLDER_PATH = @"assets\curvedstick";
        private const string ASSETS_EXTENSION = ".unity3d";
        #endregion

        #region Fields
        private static AssetBundle _assetBundle;
        #endregion

        #region Properties
        internal List<string> Errors { get; } = new List<string>();
        internal Dictionary<string, Mesh> Meshes { get; } = new Dictionary<string, Mesh>();
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

                string fullPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), ASSETS_FOLDER_PATH);

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

                    Mesh mesh = _assetBundle.LoadAsset<Mesh>("assets/leftstickfixed.fbx");
                    Meshes.Add("LeftStick", mesh);

                    mesh = _assetBundle.LoadAsset<Mesh>("assets/leftbladefixed.fbx");
                    Meshes.Add("LeftBlade", mesh);

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
