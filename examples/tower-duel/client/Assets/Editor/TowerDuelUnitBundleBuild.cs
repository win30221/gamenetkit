using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TowerDuel.Editor
{
    // WebGL-only texture split. The canonical source files and their GUIDs are
    // stable outside Resources; Windows and Editor retain their direct references.
    // A scoped build temporarily strips only those references from the Player.
    internal sealed class TowerDuelUnitBundleBuild : IDisposable
    {
        private const string ManifestPath="Assets/Resources/TowerDuel/WebUnitBundles.json";
        private const string Cache="Library/TowerDuelWebUnitBundles";
        private const string EmptyManifest="{\"version\":0,\"bundles\":[]}\n";
        private readonly List<Snapshot> snapshots=new List<Snapshot>();
        private readonly List<DuelUnitBundleRecord> records=new List<DuelUnitBundleRecord>();
        private byte[] manifestBytes;
        private bool disposed;

        private sealed class Snapshot
        {
            // BuildAssetBundles, asset import and BuildPlayer may unload native
            // Unity objects. Keep only persistent asset paths across these calls.
            internal string setPath,idlePath,walkPath,attackPath;
            internal bool detached;
            internal IEnumerable<string> AtlasPaths => new[]{idlePath,walkPath,attackPath};

            internal DuelSpriteSet LoadSet()
            {
                var set=AssetDatabase.LoadAssetAtPath<DuelSpriteSet>(setPath);
                if(!set)throw new InvalidOperationException("Cannot reload sprite metadata: "+setPath);
                return set;
            }

            internal void Detach()
            {
                var set=LoadSet();
                if(!set.HasAtlases)throw new InvalidOperationException("Prepared atlas references disappeared: "+setPath);
                detached=true;
                set.ClearFrames();
                set.Idle=set.Walk=set.Attack=null;
                EditorUtility.SetDirty(set);
            }

            internal void Restore()
            {
                if(!detached)return;
                var idle=AssetDatabase.LoadAssetAtPath<Texture2D>(idlePath);
                var walk=AssetDatabase.LoadAssetAtPath<Texture2D>(walkPath);
                var attack=AssetDatabase.LoadAssetAtPath<Texture2D>(attackPath);
                if(!idle||!walk||!attack)
                    throw new InvalidOperationException("Cannot reload original atlas textures: "+setPath);
                var set=LoadSet();
                set.ClearFrames();
                set.Idle=idle;set.Walk=walk;set.Attack=attack;
                EditorUtility.SetDirty(set);
            }
        }

        internal static void ResetManifest()
        {
            if(File.Exists(ManifestPath)&&File.ReadAllText(ManifestPath)==EmptyManifest)return;
            File.WriteAllText(ManifestPath,EmptyManifest);
            AssetDatabase.ImportAsset(ManifestPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        }

        internal static string Sha256(string path)
        {
            using(var stream=File.OpenRead(path))
            using(var sha=SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }

        internal static TowerDuelUnitBundleBuild Prepare()
        {
            var scope=new TowerDuelUnitBundleBuild();
            try {scope.BuildAndDetach();return scope;}
            catch
            {
                scope.RestoreAfterFailure();
                throw;
            }
        }

        private void BuildAndDetach()
        {
            Directory.CreateDirectory(Cache);
            var builds=new List<AssetBundleBuild>();
            foreach(string file in Directory.GetFiles(TowerDuelSpriteContent.UnitRoot,"definition.json",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal))
            {
                var d=B3SpriteDefinition.Read(file);
                string setPath="Assets/Resources/TowerDuel/B3/"+d.kind+".asset";
                var set=AssetDatabase.LoadAssetAtPath<DuelSpriteSet>(setPath);
                if(!set||!set.HasAtlases)throw new InvalidOperationException("Missing prepared atlas references: "+d.kind);
                var paths=new[]{set.Idle,set.Walk,set.Attack}.Select(AssetDatabase.GetAssetPath).ToArray();
                if(paths.Any(x=>!x.StartsWith(TowerDuelSpriteContent.UnitRoot+"/",StringComparison.Ordinal)))
                    throw new InvalidOperationException("Atlas is still in Resources: "+d.kind);
                snapshots.Add(new Snapshot{setPath=setPath,idlePath=paths[0],walkPath=paths[1],attackPath=paths[2]});
                builds.Add(new AssetBundleBuild{assetBundleName=d.kind+".bundle",assetNames=paths,addressableNames=new[]{"idle","walk","attack"}});
            }
            if(builds.Count==0||builds.Select(x=>x.assetBundleName).Distinct().Count()!=builds.Count)
                throw new InvalidOperationException("Empty or duplicate unit bundle definitions");
            Debug.Log("Building "+builds.Count+" WebGL unit texture bundles");
            var result=BuildPipeline.BuildAssetBundles(Cache,builds.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.WebGL);
            if(!result)throw new InvalidOperationException("WebGL unit AssetBundle build failed");
            Debug.Log("WebGL unit texture bundle build returned successfully; validating bundle files");
            foreach(var item in builds)
            {
                if(result.GetAllDependencies(item.assetBundleName).Length!=0)
                    throw new InvalidOperationException("Unexpected texture-bundle dependency: "+item.assetBundleName);
                string file=Path.Combine(Cache,item.assetBundleName);
                if(!BuildPipeline.GetCRCForAssetBundle(file,out uint crc))throw new InvalidOperationException("Missing bundle CRC: "+file);
                records.Add(new DuelUnitBundleRecord{kind=Path.GetFileNameWithoutExtension(item.assetBundleName),sha256=Sha256(file),bytes=new FileInfo(file).Length,crc=crc});
            }
            var manifest=new DuelUnitBundleManifest{version=1,bundles=records.ToArray()};
            File.WriteAllText(ManifestPath,JsonUtility.ToJson(manifest,true)+"\n");
            AssetDatabase.ImportAsset(ManifestPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            manifestBytes=File.ReadAllBytes(ManifestPath);
            // Resolve the current native objects only after the build/import steps.
            // Never use a UnityEngine.Object captured before BuildAssetBundles.
            foreach(var snapshot in snapshots)snapshot.Detach();
            AssetDatabase.SaveAssets();
            Debug.Log("Detached "+snapshots.Count+" sprite atlas references for the WebGL Player");
        }

        internal void AssertNoInlineAtlases(BuildReport report)
        {
            var atlasPaths=new HashSet<string>(snapshots.SelectMany(x=>x.AtlasPaths),StringComparer.Ordinal);
            if(report.packedAssets==null||report.packedAssets.Length==0)throw new InvalidOperationException("Missing detailed Player asset report");
            foreach(var packed in report.packedAssets)
                foreach(var asset in packed.contents)
                    if(atlasPaths.Contains(asset.sourceAssetPath))
                        throw new InvalidOperationException("WebGL Player still includes bundled atlas: "+asset.sourceAssetPath);
        }

        internal void Publish(string output)
        {
            string folder=Path.Combine(output,"assets/bundles");
            if(Directory.Exists(folder))Directory.Delete(folder,true);
            foreach(var record in records)
            {
                string source=Path.Combine(Cache,record.kind+".bundle");
                if(Sha256(source)!=record.sha256)throw new InvalidOperationException("Bundle changed during Player build: "+record.kind);
                string target=Path.Combine(output,record.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source,target,true);
            }
            File.WriteAllBytes(Path.Combine(output,"unit-bundles.json"),manifestBytes);
        }

        // A secondary cleanup error must never replace the original build error.
        internal void RestoreAfterFailure()
        {
            try {Dispose();}
            catch(Exception error)
            {
                Debug.LogError("Could not fully restore Editor atlas references. Run Tower Duel > Prepare playable scene before testing or rebuilding.");
                Debug.LogException(error);
            }
        }

        public void Dispose()
        {
            if(disposed)return;
            var errors=new List<Exception>();
            foreach(var snapshot in snapshots)
            {
                if(!snapshot.detached)continue;
                try {snapshot.Restore();}
                catch(Exception error){errors.Add(error);}
            }
            // Save successfully restored sets even if another set failed.
            try {AssetDatabase.SaveAssets();}
            catch(Exception error){errors.Add(error);}
            try {ResetManifest();}
            catch(Exception error){errors.Add(error);}
            if(errors.Count>0)
                throw new AggregateException("Could not restore WebGL bundle build state",errors);
            foreach(var snapshot in snapshots)snapshot.detached=false;
            disposed=true;
        }
    }
}
