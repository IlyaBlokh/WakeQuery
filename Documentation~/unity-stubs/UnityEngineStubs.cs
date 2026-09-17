// Minimal stand-ins for the UnityEngine APIs used by Runtime/Unity.
// They exist only so WakeQuery.Unity.Docs.csproj can compile the Unity adapter
// for API documentation without a Unity installation. Unity never imports this
// folder because its parent name ends with '~'.
using System;

namespace UnityEngine
{
    internal static class Application
    {
        public static event Action<bool> focusChanged
        {
            add { }
            remove { }
        }

        public static bool isFocused => true;
    }

    internal static class Time
    {
        public static double realtimeSinceStartupAsDouble => 0;
    }

    internal static class Debug
    {
        public static void LogException(Exception exception)
        {
        }
    }

    internal enum RuntimeInitializeLoadType
    {
        SubsystemRegistration
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType)
        {
        }
    }
}

namespace UnityEngine.LowLevel
{
    internal struct PlayerLoopSystem
    {
        public delegate void UpdateFunction();

        public Type type;
        public PlayerLoopSystem[] subSystemList;
        public UpdateFunction updateDelegate;
    }

    internal static class PlayerLoop
    {
        public static PlayerLoopSystem GetCurrentPlayerLoop() => default;

        public static void SetPlayerLoop(PlayerLoopSystem loop)
        {
        }
    }
}

namespace UnityEngine.PlayerLoop
{
    internal struct Update
    {
    }
}
