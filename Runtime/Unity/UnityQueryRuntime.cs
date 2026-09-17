using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using WakeQuery.Internal;

namespace WakeQuery.Unity
{
    public static class UnityQueryRuntime
    {
        private static PlayerLoopHost _host;

        public static QueryClient CreateClient(QueryClientOptions options = null)
        {
            PlayerLoopHost host = _host;
            if (host == null)
            {
                host = new PlayerLoopHost();
                _host = host;
            }

            return new QueryClient(host, options);
        }

        public static void NotifyReconnected()
        {
            PlayerLoopHost host = _host;
            host?.NotifyReconnected();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PlayerLoopHost host = _host;
            _host = null;
            host?.Reset();
            RemoveMarker();
        }

        private static void Release(PlayerLoopHost host)
        {
            if (ReferenceEquals(_host, host))
            {
                _host = null;
            }
        }

        private static void RunPlayerLoop()
        {
            _host?.RunFrame();
        }

        private static void InstallMarker()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (CountMarkers(loop) > 0)
            {
                return;
            }

            var marker = new PlayerLoopSystem
            {
                type = typeof(UnityQueryRuntime),
                updateDelegate = RunPlayerLoop
            };

            if (!InsertInto(ref loop, typeof(Update), marker))
            {
                throw new InvalidOperationException(
                    "WakeQuery could not find UnityEngine.PlayerLoop.Update in the current PlayerLoop.");
            }

            PlayerLoop.SetPlayerLoop(loop);
        }

        private static void RemoveMarker()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!RemoveFrom(ref loop))
            {
                return;
            }

            PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool InsertInto(
            ref PlayerLoopSystem system,
            Type parentType,
            PlayerLoopSystem marker)
        {
            if (system.type == parentType)
            {
                PlayerLoopSystem[] existing = system.subSystemList;
                int count = existing?.Length ?? 0;
                var updated = new PlayerLoopSystem[count + 1];
                if (count > 0)
                {
                    Array.Copy(existing, updated, count);
                }

                updated[count] = marker;
                system.subSystemList = updated;
                return true;
            }

            PlayerLoopSystem[] children = system.subSystemList;
            if (children == null)
            {
                return false;
            }

            for (int index = 0; index < children.Length; index++)
            {
                PlayerLoopSystem child = children[index];
                if (!InsertInto(ref child, parentType, marker))
                {
                    continue;
                }

                children[index] = child;
                system.subSystemList = children;
                return true;
            }

            return false;
        }

        private static bool RemoveFrom(ref PlayerLoopSystem system)
        {
            PlayerLoopSystem[] children = system.subSystemList;
            if (children == null)
            {
                return false;
            }

            bool changed = false;
            int retained = 0;
            for (int index = 0; index < children.Length; index++)
            {
                PlayerLoopSystem child = children[index];
                if (child.type == typeof(UnityQueryRuntime))
                {
                    changed = true;
                    continue;
                }

                if (RemoveFrom(ref child))
                {
                    changed = true;
                }

                children[retained++] = child;
            }

            if (!changed)
            {
                return false;
            }

            if (retained == children.Length)
            {
                system.subSystemList = children;
                return true;
            }

            var updated = new PlayerLoopSystem[retained];
            if (retained > 0)
            {
                Array.Copy(children, updated, retained);
            }

            system.subSystemList = updated;
            return true;
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

        private sealed class PlayerLoopHost : IQueryRuntimeHost
        {
            private readonly List<QueryClient> _clients = new List<QueryClient>();
            private readonly object _queueLock = new object();
            private Queue<Action> _pending = new Queue<Action>();
            private Queue<Action> _draining = new Queue<Action>();
            private bool _acceptingPosts;
            private bool _isUpdating;
            private bool _removeRequested;

            public PlayerLoopHost()
            {
                OwnerThreadId = Environment.CurrentManagedThreadId;
            }

            public int OwnerThreadId { get; }

            public TimeSpan Elapsed =>
                TimeSpan.FromSeconds(Time.realtimeSinceStartupAsDouble);

            public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

            public Action<Exception> DefaultUnhandledException =>
                Debug.LogException;

            public void Post(Action action)
            {
                if (action == null)
                {
                    throw new ArgumentNullException(nameof(action));
                }

                lock (_queueLock)
                {
                    if (_acceptingPosts)
                    {
                        _pending.Enqueue(action);
                    }
                }
            }

            public void Register(QueryClient client)
            {
                AssertOwnerThread();
                if (_clients.Count == 0)
                {
                    InstallMarker();
                    Application.focusChanged += OnFocusChanged;
                    lock (_queueLock)
                    {
                        _acceptingPosts = true;
                    }
                }

                _clients.Add(client);
                client.OnFocusChanged(Application.isFocused);
            }

            public void Unregister(QueryClient client)
            {
                AssertOwnerThread();
                _clients.Remove(client);
                if (_clients.Count != 0)
                {
                    return;
                }

                Application.focusChanged -= OnFocusChanged;
                lock (_queueLock)
                {
                    _acceptingPosts = false;
                    _pending.Clear();
                    _draining.Clear();
                }

                if (_isUpdating)
                {
                    _removeRequested = true;
                }
                else
                {
                    RemoveAndRelease();
                }
            }

            public void RunFrame()
            {
                AssertOwnerThread();
                _isUpdating = true;
                try
                {
                    lock (_queueLock)
                    {
                        Queue<Action> swap = _draining;
                        _draining = _pending;
                        _pending = swap;
                    }

                    while (_draining.Count > 0)
                    {
                        try
                        {
                            _draining.Dequeue()();
                        }
                        catch (Exception exception)
                        {
                            Debug.LogException(exception);
                        }
                    }

                    int index = 0;
                    while (index < _clients.Count)
                    {
                        QueryClient client = _clients[index];
                        try
                        {
                            client.Pump();
                        }
                        catch (Exception exception)
                        {
                            Debug.LogException(exception);
                        }

                        if (index < _clients.Count &&
                            ReferenceEquals(_clients[index], client))
                        {
                            index++;
                        }
                    }
                }
                finally
                {
                    _isUpdating = false;
                    if (_removeRequested && _clients.Count == 0)
                    {
                        RemoveAndRelease();
                    }
                }
            }

            public void NotifyReconnected()
            {
                AssertOwnerThread();
                int index = 0;
                while (index < _clients.Count)
                {
                    QueryClient client = _clients[index];
                    client.OnReconnect();
                    if (index < _clients.Count &&
                        ReferenceEquals(_clients[index], client))
                    {
                        index++;
                    }
                }
            }

            public void Reset()
            {
                AssertOwnerThread();
                Application.focusChanged -= OnFocusChanged;
                for (int index = 0; index < _clients.Count; index++)
                {
                    _clients[index].AbandonFromRuntime();
                }

                _clients.Clear();
                lock (_queueLock)
                {
                    _acceptingPosts = false;
                    _pending.Clear();
                    _draining.Clear();
                }
            }

            private void OnFocusChanged(bool isFocused)
            {
                if (Environment.CurrentManagedThreadId != OwnerThreadId)
                {
                    Post(() => OnFocusChanged(isFocused));
                    return;
                }

                int index = 0;
                while (index < _clients.Count)
                {
                    QueryClient client = _clients[index];
                    client.OnFocusChanged(isFocused);
                    if (index < _clients.Count &&
                        ReferenceEquals(_clients[index], client))
                    {
                        index++;
                    }
                }
            }

            private void RemoveAndRelease()
            {
                _removeRequested = false;
                RemoveMarker();
                Release(this);
            }

            private void AssertOwnerThread()
            {
                if (Environment.CurrentManagedThreadId != OwnerThreadId)
                {
                    throw new InvalidOperationException(
                        "UnityQueryRuntime must be controlled from Unity's owning thread.");
                }
            }
        }
    }
}
