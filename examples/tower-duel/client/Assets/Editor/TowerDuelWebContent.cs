using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace TowerDuel.Editor
{
    [Serializable]
    internal sealed class TowerDuelWebContent
    {
        private const string FileName = "tower-duel-content.json";
        private const string MapPath = "Assets/Resources/TowerDuel/map.json";
        private const string RulesPath = "Assets/Resources/TowerDuel/rules.json";

        public string mapHash;
        public string rulesHash;
        private DateTime mapWriteTime;
        private DateTime rulesWriteTime;

        internal static TowerDuelWebContent BeginBuild(string output, out DuelRules rules)
        {
            Directory.CreateDirectory(output);
            // A failed or cancelled rebuild can leave a partially overwritten Player.
            File.Delete(Path.Combine(output, FileName));

            const ImportAssetOptions import = ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport;
            AssetDatabase.ImportAsset(MapPath, import);
            AssetDatabase.ImportAsset(RulesPath, import);
            rules = DuelRules.Load();
            return new TowerDuelWebContent {
                mapHash = DuelMap.Load().Hash,
                rulesHash = rules.Hash,
                mapWriteTime = File.GetLastWriteTimeUtc(MapPath),
                rulesWriteTime = File.GetLastWriteTimeUtc(RulesPath)
            };
        }

        internal void Publish(string output)
        {
            VerifySource(MapPath, mapHash, mapWriteTime);
            VerifySource(RulesPath, rulesHash, rulesWriteTime);
            string path = Path.Combine(output, FileName);
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(this, true) + "\n");
                File.Move(temporary, path);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        private static void VerifySource(string path, string importedHash, DateTime originalWriteTime)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                string diskHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (diskHash != importedHash || File.GetLastWriteTimeUtc(path) != originalWriteTime)
                    throw new InvalidOperationException($"{path} changed during the WebGL build or differs from the imported asset. Rebuild this Player.");
            }
        }
    }
}
