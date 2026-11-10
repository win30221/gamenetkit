using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed class BattleFiles:MonoBehaviour
    {
        private sealed class Download:DownloadHandlerScript
        {
            private readonly MemoryStream bytes=new MemoryStream();
            public Download():base(new byte[65536]){}
            protected override bool ReceiveData(byte[] data,int count){if(count<0||bytes.Length+count>BattleRecording.MaxBytes)return false;if(count>0)bytes.Write(data,0,count);return true;}
            public string Read()=>new UTF8Encoding(false,true).GetString(bytes.GetBuffer(),0,(int)bytes.Length);
        }
        private BattleClient client;
        private UnityWebRequest request;
        private int operation,identityRevision;
        private bool choosing;
        [Serializable] private sealed class Selection
        {
            public int requestId=0,size=0;
            public string status=null,url=null,name=null,code=null;
        }
        public bool Loading{get;private set;}
        public string Notice{get;private set;}="";
        public bool PickerSupported{
            get{
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return WindowsReplayPicker.Supported;
#endif
            }
        }
        public bool CanLoad=>client?.Flow?.Content!=null&&!client.Flow.Busy&&client.Flow.Room==null&&!client.Flow.Battle.Connected&&!client.Flow.Battle.Connecting&&!client.Flow.Recovery.Pending&&!client.Flow.Playback.Active&&BattleNetcode.PlaybackFactory!=null;
        private void Awake(){client=GetComponent<BattleClient>();}
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool keyboardCaptured,previousKeyboardCapture;
        [DllImport("__Internal")] private static extern void RF_Rumblebound_Download(string text);
        [DllImport("__Internal")] private static extern void RF_Rumblebound_SelectReplay(string target,int requestId,int maxBytes);
        [DllImport("__Internal")] private static extern void RF_Rumblebound_ReleaseReplay(string target,int requestId);
        [DllImport("__Internal")] private static extern void RF_Rumblebound_CancelReplay(string target,int requestId);
#endif
        public void Export()
        {
            try{
                string text=client.Flow.Recording.Export();
#if UNITY_WEBGL && !UNITY_EDITOR
                RF_Rumblebound_Download(text);Notice="已下載錄製檔。";
#else
                string directory=Path.Combine(Application.persistentDataPath,"Replays");Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,"rumblebound-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".playback.json");
                File.WriteAllText(path,text,new UTF8Encoding(false));Notice="已匯出："+path;
#endif
            }catch(Exception error)when(error is IOException||error is UnauthorizedAccessException||error is InvalidOperationException){Notice="無法匯出錄製檔。";}
        }
        private int Begin()
        {
            identityRevision=client.Flow.Identity.Revision;Loading=true;return ++operation;
        }
        public void SelectReplay()
        {
            if(Loading||!CanLoad)return;
            if(!PickerSupported){Notice="目前平台請使用進階載入。";return;}
            int token=Begin();choosing=true;Notice="請選擇重播檔案…";
#if UNITY_WEBGL && !UNITY_EDITOR
            previousKeyboardCapture=WebGLInput.captureAllKeyboardInput;keyboardCaptured=true;WebGLInput.captureAllKeyboardInput=false;
            RF_Rumblebound_SelectReplay(gameObject.name,token,BattleRecording.MaxBytes);
#else
            try{
                string path=WindowsReplayPicker.Open();choosing=false;
                if(string.IsNullOrEmpty(path)){Loading=false;Notice="已取消選擇。";return;}
                StartCoroutine(LoadFile(path,token,false,Path.GetFileName(path)));
            }catch(Exception error)when(error is IOException||error is UnauthorizedAccessException||error is InvalidOperationException){Loading=false;choosing=false;Notice="無法開啟選檔視窗，請使用進階載入。";}
#endif
        }
        [Preserve] public void OnReplayFileSelected(string value)
        {
            Selection selected;
            try{selected=JsonUtility.FromJson<Selection>(value);}catch(ArgumentException){return;}
            if(selected==null||!choosing||selected.requestId!=operation)return;
            choosing=false;RestoreKeyboard();
            if(selected.status=="selected"&&selected.size>0&&selected.size<=BattleRecording.MaxBytes&&selected.url?.StartsWith("blob:",StringComparison.Ordinal)==true){
                if(!CanLoad||identityRevision!=client.Flow.Identity.Revision){Notice="畫面已變更，請重新選擇檔案。";Loading=false;ReleaseSelection(operation);return;}
                StartCoroutine(LoadFile(selected.url,operation,true,selected.name));return;
            }
            Notice=selected.status=="cancelled"?"已取消選擇。":selected.code=="too_large"?"檔案超過 32 MiB，請選擇較小的重播。":selected.code=="empty"?"這個檔案是空的，請重新選擇。":"無法讀取所選檔案，請重新選擇。";
            Loading=false;ReleaseSelection(operation);
        }
        public void Load(string location){if(!Loading&&CanLoad&&!string.IsNullOrWhiteSpace(location))StartCoroutine(LoadFile(location.Trim(),Begin(),false,null));}
        private IEnumerator LoadFile(string location,int token,bool blob,string name)
        {
            UnityWebRequest current=null;
            try{
                string url=null;
                try{
                    var uri=new Uri(location.Contains("://")||blob?location:new Uri(Path.GetFullPath(location)).AbsoluteUri);
                    if(uri.Scheme!="http"&&uri.Scheme!="https"&&uri.Scheme!="file"&&!(blob&&uri.Scheme=="blob"))throw new FormatException();
#if UNITY_WEBGL && !UNITY_EDITOR
                    if(uri.Scheme=="file")throw new FormatException();
#endif
                    if(uri.Scheme=="file"){
                        long bytes=new FileInfo(uri.LocalPath).Length;
                        if(bytes>BattleRecording.MaxBytes){Notice="檔案超過 32 MiB，請選擇較小的重播。";yield break;}
                    }
                    url=uri.AbsoluteUri;
                }catch(Exception error)when(error is ArgumentException||error is FormatException||error is IOException||error is UnauthorizedAccessException){Notice="請選擇重播檔案，或在進階載入輸入有效網址。";}
                if(url==null)yield break;
                Notice="正在載入重播…";
                using(var download=new Download())using(current=new UnityWebRequest(url,"GET",download,null)){
                    request=current;current.timeout=30;yield return current.SendWebRequest();
                    if(token!=operation)yield break;
                    if(current.result!=UnityWebRequest.Result.Success)Notice="無法載入重播（最大 32 MiB）。";
                    else if(!CanLoad||identityRevision!=client.Flow.Identity.Revision)Notice="畫面已變更，請重新選擇檔案。";
                    else try{string text=download.Read();if(text.Length>0&&text[0]=='\ufeff')text=text.Substring(1);client.Flow.LoadPlayback(text);Notice=string.IsNullOrEmpty(name)?"重播已載入。":"已載入："+name;}
                    catch(Exception error)when(error is InvalidOperationException||error is ArgumentException||error is FormatException){Notice="重播格式不正確，請選擇 .playback.json 檔案。";}
                }
            }finally{if(request==current)request=null;ReleaseSelection(token);if(token==operation)Loading=false;}
        }
        private void RestoreKeyboard()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if(keyboardCaptured){WebGLInput.captureAllKeyboardInput=previousKeyboardCapture;keyboardCaptured=false;}
#endif
        }
        private void ReleaseSelection(int token)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RF_Rumblebound_ReleaseReplay(gameObject.name,token);
#endif
        }
        private void OnDisable()
        {
            int cancelled=operation++;choosing=false;RestoreKeyboard();
#if UNITY_WEBGL && !UNITY_EDITOR
            RF_Rumblebound_CancelReplay(gameObject.name,cancelled);
#endif
            request?.Abort();request?.Dispose();StopAllCoroutines();request=null;Loading=false;
        }
    }
}
