using UnityEditor;
using Realtime.Examples.Presentation.Editor;
namespace Realtime.Examples.CodecLesson.Editor {public static class ExampleBuild {
    public static void Windows()=>LessonBuild.Run("Codec","RF_CODEC_BUILD PASS",BuildTarget.StandaloneWindows64);
    public static void Web()=>LessonBuild.Run("Codec","RF_CODEC_BUILD PASS",BuildTarget.WebGL);
}}
