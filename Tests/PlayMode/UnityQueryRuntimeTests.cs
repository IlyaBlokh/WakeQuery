using System;
using NUnit.Framework;
using UnityEngine.LowLevel;

namespace WakeQuery.Unity.Tests
{
    public sealed class UnityQueryRuntimeTests
    {
        [Test]
        public void OnePlayerLoopMarkerIsSharedAndRemovedWithLastClient()
        {
            Assert.That(CountMarkers(PlayerLoop.GetCurrentPlayerLoop()), Is.Zero);
            QueryClient first = UnityQueryRuntime.CreateClient();
            QueryClient second = null;

            try
            {
                Assert.That(CountMarkers(PlayerLoop.GetCurrentPlayerLoop()), Is.EqualTo(1));

                second = UnityQueryRuntime.CreateClient();
                Assert.That(CountMarkers(PlayerLoop.GetCurrentPlayerLoop()), Is.EqualTo(1));

                first.Dispose();
                first = null;
                Assert.That(CountMarkers(PlayerLoop.GetCurrentPlayerLoop()), Is.EqualTo(1));
            }
            finally
            {
                second?.Dispose();
                first?.Dispose();
            }

            Assert.That(CountMarkers(PlayerLoop.GetCurrentPlayerLoop()), Is.Zero);
        }

        [Test]
        public void IdlePlayerLoopCallbackDoesNotAllocate()
        {
            using QueryClient client = UnityQueryRuntime.CreateClient();
            PlayerLoopSystem.UpdateFunction callback =
                FindMarker(PlayerLoop.GetCurrentPlayerLoop()).updateDelegate;
            for (int index = 0; index < 10; index++)
            {
                callback();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 100; index++)
            {
                callback();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        private static int CountMarkers(PlayerLoopSystem system)
        {
            int count = system.type == typeof(UnityQueryRuntime) ? 1 : 0;
            PlayerLoopSystem[] children = system.subSystemList;
            if (children == null)
            {
                return count;
            }

            for (int index = 0; index < children.Length; index++)
            {
                count += CountMarkers(children[index]);
            }

            return count;
        }

        private static PlayerLoopSystem FindMarker(PlayerLoopSystem system)
        {
            if (system.type == typeof(UnityQueryRuntime))
            {
                return system;
            }

            PlayerLoopSystem[] children = system.subSystemList;
            if (children != null)
            {
                for (int index = 0; index < children.Length; index++)
                {
                    PlayerLoopSystem marker = FindMarker(children[index]);
                    if (marker.type == typeof(UnityQueryRuntime))
                    {
                        return marker;
                    }
                }
            }

            return default;
        }
    }
}
