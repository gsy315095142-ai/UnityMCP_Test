#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Core
{
    /// <summary>
    /// 主线程调度器。
    /// 将后台线程的操作调度到 Unity 主线程执行，确保 Unity API 调用的线程安全。
    /// </summary>
    [InitializeOnLoad]
    public static class MainThread
    {
        private static readonly ConcurrentQueue<Action> _actionQueue = new();
        private static readonly int _mainThreadId;

        static MainThread()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update -= ProcessQueue;
            EditorApplication.update += ProcessQueue;
        }

        /// <summary>
        /// 当前是否在主线程上执行
        /// </summary>
        public static bool IsMainThread =>
            Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>
        /// 在主线程上同步执行操作。
        /// 如果已在主线程，则直接执行；否则入队等待主线程处理。
        /// </summary>
        public static void Run(Action action)
        {
            if (IsMainThread)
            {
                action();
                return;
            }

            var tcs = new TaskCompletionSource<bool>();
            _actionQueue.Enqueue(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            tcs.Task.Wait();
        }

        /// <summary>
        /// 在主线程上同步执行操作并返回结果。
        /// 如果已在主线程，则直接执行；否则入队等待主线程处理。
        /// </summary>
        public static T Run<T>(Func<T> func)
        {
            if (IsMainThread)
                return func();

            var tcs = new TaskCompletionSource<T>();
            _actionQueue.Enqueue(() =>
            {
                try
                {
                    tcs.TrySetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task.Result;
        }

        /// <summary>
        /// 在主线程上异步执行操作并返回结果。
        /// 不会阻塞调用线程，适合在 async 方法中使用。
        /// </summary>
        public static Task<T> RunAsync<T>(Func<T> func)
        {
            if (IsMainThread)
            {
                try
                {
                    return Task.FromResult(func());
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }
            }

            var tcs = new TaskCompletionSource<T>();
            _actionQueue.Enqueue(() =>
            {
                try
                {
                    tcs.TrySetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        /// <summary>
        /// 在主线程上异步执行操作（无返回值）
        /// </summary>
        public static Task RunAsync(Action action)
        {
            if (IsMainThread)
            {
                try
                {
                    action();
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
            }

            var tcs = new TaskCompletionSource<bool>();
            _actionQueue.Enqueue(() =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        private static void ProcessQueue()
        {
            while (_actionQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
