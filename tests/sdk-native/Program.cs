using System;
using System.Text.Json;

// Only the test harness uses Unity APIs (logging and reading a JSON fixture).
// These shims let the identical pure SDK contract suite run without an Editor
// license. This does not validate Unity JsonUtility, IL2CPP or Player behavior.
namespace UnityEngine
{
    public static class Debug { public static void Log(object value) => Console.WriteLine(value); }
    public static class JsonUtility
    {
        public static T FromJson<T>(string value) => JsonSerializer.Deserialize<T>(value, new JsonSerializerOptions { IncludeFields = true });
    }
}
public static class NativeSdkTests
{
    public static int Main()
    {
        try { SdkTests.Run(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
