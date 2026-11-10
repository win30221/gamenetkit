using UnityEditor;
using Realtime.Examples.Presentation.Editor;
namespace Realtime.Examples.ObjectsActions.Editor {public static class ExampleBuild {
    public static void Windows()=>LessonBuild.Run("ObjectsActions","RF_OBJECTSACTIONS_BUILD PASS",BuildTarget.StandaloneWindows64);
    public static void Web()=>LessonBuild.Run("ObjectsActions","RF_OBJECTSACTIONS_BUILD PASS",BuildTarget.WebGL);
}}
