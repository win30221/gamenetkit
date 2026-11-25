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
            internal DuelSpriteSet set;
            internal Texture2D idle,walk,attack;
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
            catch {scope.Dispose();throw;}
        }

        private void BuildAndDetach()
        {
            Directory.CreateDirectory(Cache);
            var builds=new List<AssetBundleBuild>();
            foreach(string file in Directory.GetFiles(TowerDuelSpriteContent.UnitRoot,"definition.json",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal))
            {
                var d=B3SpriteDefinition.Read(file);
                var set=AssetDatabase.LoadAssetAtPath<DuelSpriteSet>("Assets/Resources/TowerDuel/B3/"+d.kind+".asset");
                if(!set||!set.HasAtlases)throw new InvalidOperationException("Missing prepared atlas references: "+d.kind);
                var paths=new[]{set.Idle,set.Walk,set.Attack}.Select(AssetDatabase.GetAssetPath).ToArray();
                if(paths.Any(x=>!x.StartsWith(TowerDuelSpriteContent.UnitRoot+"/",StringComparison.Ordinal)))
                    throw new InvalidOperationException("Atlas is still in Resources: "+d.kind);
                snapshots.Add(new Snapshot{set=set,idle=set.Idle,walk=set.Walk,attack=set.Attack});
                builds.Add(new AssetBundleBuild{assetBundleName=d.kind+".bundle",assetNames=paths,addressableNames=new[]{"idle","walk","attack"}});
            }
            if(builds.Count==0||builds.Select(x=>x.assetBundleName).Distinct().Count()!=builds.Count)
                throw new InvalidOperationException("Empty or duplicate unit bundle definitions");
            var result=BuildPipeline.BuildAssetBundles(Cache,builds.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.WebGL);
            if(!result)throw new InvalidOperationException("WebGL unit AssetBundle build failed");
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
            foreach(var snapshot in snapshots)
            {
                snapshot.set.ClearFrames();
                snapshot.set.Idle=snapshot.set.Walk=snapshot.set.Attack=null;
                EditorUtility.SetDirty(snapshot.set);
            }
            AssetDatabase.SaveAssets();
        }

        internal void AssertNoInlineAtlases(BuildReport report)
        {
            var atlasPaths=new HashSet<string>(snapshots.SelectMany(x=>new[]{x.idle,x.walk,x.attack}).Select(AssetDatabase.GetAssetPath),StringComparer.Ordinal);
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

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            foreach(var snapshot in snapshots)
            {
                snapshot.set.Idle=snapshot.idle;
                snapshot.set.Walk=snapshot.walk;
                snapshot.set.Attack=snapshot.attack;
                EditorUtility.SetDirty(snapshot.set);
            }
            AssetDatabase.SaveAssets();
            ResetManifest();
        }
    }
}
