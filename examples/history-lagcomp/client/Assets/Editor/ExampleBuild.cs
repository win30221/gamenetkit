using UnityEditor;
using Realtime.Examples.Presentation.Editor;
namespace Realtime.Examples.HistoryLesson.Editor {public static class ExampleBuild {
    public static void Windows()=>LessonBuild.Run("History","RF_HISTORY_BUILD PASS",BuildTarget.StandaloneWindows64);
    public static void Web()=>LessonBuild.Run("History","RF_HISTORY_BUILD PASS",BuildTarget.WebGL);
}}
