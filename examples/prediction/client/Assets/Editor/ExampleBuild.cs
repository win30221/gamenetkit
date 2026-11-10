using UnityEditor;
using Realtime.Examples.Presentation.Editor;
namespace Realtime.Examples.PredictionLesson.Editor {public static class ExampleBuild {
    public static void Windows()=>LessonBuild.Run("Prediction","RF_PREDICTION_BUILD PASS",BuildTarget.StandaloneWindows64);
    public static void Web()=>LessonBuild.Run("Prediction","RF_PREDICTION_BUILD PASS",BuildTarget.WebGL);
}}
