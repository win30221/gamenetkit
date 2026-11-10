using UnityEditor;
using Realtime.Examples.Presentation.Editor;
namespace Realtime.Examples.NetworkRecovery.Editor {public static class ExampleBuild {
    public static void Windows()=>LessonBuild.Run("NetworkRecovery","RF_NETWORKRECOVERY_BUILD PASS",BuildTarget.StandaloneWindows64);
    public static void Web()=>LessonBuild.Run("NetworkRecovery","RF_NETWORKRECOVERY_BUILD PASS",BuildTarget.WebGL);
}}
