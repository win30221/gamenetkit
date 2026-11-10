using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Rumblebound.Example.Battle.Unity
{
    internal static class WindowsReplayPicker
    {
        public static bool Supported{
            get{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return true;
#else
                return false;
#endif
            }
        }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        private sealed class OpenFileName
        {
            public int size=0;
            public IntPtr owner=IntPtr.Zero,instance=IntPtr.Zero;
            public string filter=null,customFilter=null;
            public int maxCustomFilter=0,filterIndex=1;
            public IntPtr file=IntPtr.Zero;
            public int maxFile=0;
            public IntPtr fileTitle=IntPtr.Zero;
            public int maxFileTitle=0;
            public string initialDirectory=null,title=null;
            public uint flags=0;
            public ushort fileOffset=0,extensionOffset=0;
            public string defaultExtension=null;
            public IntPtr customData=IntPtr.Zero,hook=IntPtr.Zero;
            public string template=null;
            public IntPtr reserved=IntPtr.Zero;
            public uint reservedValue=0,extendedFlags=0;
        }
        [DllImport("comdlg32.dll",EntryPoint="GetOpenFileNameW",CharSet=CharSet.Unicode)]
        [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetOpenFileName([In,Out] OpenFileName value);
        [DllImport("comdlg32.dll")] private static extern uint CommDlgExtendedError();
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        private static string directory;
#endif
        public static string Open()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            IntPtr buffer=Marshal.AllocHGlobal(32768*2);
            try{
                Marshal.WriteInt16(buffer,0);
                var selected=new OpenFileName{size=Marshal.SizeOf(typeof(OpenFileName)),owner=GetActiveWindow(),
                    filter="Rumblebound 重播 (*.playback.json;*.json)\0*.playback.json;*.json\0所有檔案 (*.*)\0*.*\0\0",
                    file=buffer,maxFile=32768,title="選擇重播檔案",defaultExtension="json",initialDirectory=directory,
                    flags=0x00080000|0x00001000|0x00000800|0x00000008|0x00000004|0x02000000};
                if(!GetOpenFileName(selected)){uint error=CommDlgExtendedError();if(error!=0)throw new IOException("Replay file dialog error: "+error);return null;}
                string path=Marshal.PtrToStringUni(buffer);directory=Path.GetDirectoryName(path);return path;
            }finally{Marshal.FreeHGlobal(buffer);}
#else
            throw new PlatformNotSupportedException();
#endif
        }
    }
}
