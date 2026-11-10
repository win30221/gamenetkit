using UnityEditor;
using Realtime.Examples.Presentation.Editor;
namespace Realtime.Examples.ReplayLesson.Editor {public static class ExampleBuild {
    public static void Windows()=>LessonBuild.Run("Replay","RF_REPLAY_BUILD PASS",BuildTarget.StandaloneWindows64);
    public static void Web()=>LessonBuild.Run("Replay","RF_REPLAY_BUILD PASS",BuildTarget.WebGL);
}}
