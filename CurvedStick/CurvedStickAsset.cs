using oomtm450PuckMod_CurvedStick.SystemFunc;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace oomtm450PuckMod_CurvedStick {
    internal class CurvedStickAsset : MonoBehaviour {
        #region Constants
        private const string ASSETS_FOLDER_PATH = @"assets\curvedstick";
        private const string ASSETS_EXTENSION = ".assets";
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
                Destroy(meshObject.Value);
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

                StartCoroutine(GetAssets(fullPath));
            }
            catch (Exception ex) {
                Logging.LogError($"Error loading Images.\n{ex}");
            }
        }

        private IEnumerator GetAssets(string path) {
            foreach (string file in Directory.GetFiles(path, "*" + ASSETS_EXTENSION, SearchOption.AllDirectories)) {
                string filePath = new Uri(Path.GetFullPath(file)).LocalPath;
                UnityWebRequest webRequest = UnityWebRequestAssetBundle.GetAssetBundle(filePath);
                yield return webRequest.SendWebRequest();

                if (webRequest.result != UnityWebRequest.Result.Success)
                    Errors.Add(webRequest.error);
                else {
                    try {
                        string fileName = filePath.Substring(filePath.LastIndexOf('\\') + 1, filePath.Length - filePath.LastIndexOf('\\') - 1).Replace(ASSETS_EXTENSION, "");
                        AssetBundle assetBundle = DownloadHandlerAssetBundle.GetContent(webRequest);

                        foreach (var test in assetBundle.LoadAllAssets())
                            Errors.Add(test.name);
                        Mesh mesh = assetBundle.LoadAsset<Mesh>("LeftStick");
                        DontDestroyOnLoad(mesh);
                        Meshes.Add("LeftStick", mesh);

                        mesh = assetBundle.LoadAsset<Mesh>("LeftBlade");
                        DontDestroyOnLoad(mesh);
                        Meshes.Add("LeftBlade", mesh);

                        //assetBundle.Unload(true);
                    }
                    catch (Exception ex) {
                        Errors.Add(ex.ToString());
                    }
                }
            }
        }
        #endregion
    }
}
