using UnityEngine;
using Realtime.Examples.Presentation;
namespace Realtime.Examples.ObjectsActions {public static class ExampleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Run()=>LessonBehaviour.Boot<ExampleClient>("ObjectsActions");
}}
