using UnityEditor;
using Realtime.Examples.Presentation.Editor;
namespace Realtime.Examples.InterestLesson.Editor {public static class ExampleBuild {
    public static void Windows()=>LessonBuild.Run("Interest","RF_INTEREST_BUILD PASS",BuildTarget.StandaloneWindows64);
    public static void Web()=>LessonBuild.Run("Interest","RF_INTEREST_BUILD PASS",BuildTarget.WebGL);
}}
