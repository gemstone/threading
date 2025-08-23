//******************************************************************************************************
//  KeyedSignal.cs - Gbtc
//
//  Copyright © 2025, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may not use this
//  file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  08/22/2025 - J. Ritchie Carroll
//       Generated original version of source code with assistance from ChatGPT.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Gemstone.Threading;

/// <summary>
/// Represents a wait-by-key async signaling operation that can work with multiple concurrent waiters
/// per key. Multiple callers await a task associated with a key; another single thread then signals
/// completion with a result or failure with an exception for that key.
/// </summary>
/// <remarks>
/// Pruning happens periodically to avoid orphaned waiters when all callers timeout from no signal.
/// </remarks>
/// <typeparam name="TKey">Key type, e.g., a <see cref="Guid"/>.</typeparam>
/// <typeparam name="TResult">Result type delivered to all waiters when signaled.</typeparam>
public sealed class KeyedSignal<TKey, TResult> : IDisposable, IAsyncDisposable where TKey : notnull
{
    #region [ Members ]

    // Nested Types
    private sealed class Waiters
    {
        public readonly Lock Gate = new();
        public readonly List<TaskCompletionSource<TResult>> List = [];

        // Atomically captures the current completion sources and clears the list
        public TaskCompletionSource<TResult>[] Drain()
        {
            lock (Gate)
            {
                TaskCompletionSource<TResult>[] sources = List.ToArray();
                List.Clear();
                return sources;
            }
        }
    }

    // Fields
    private readonly ConcurrentDictionary<TKey, Waiters> m_waiters;

    // Background prune used to handle possible orphaned waiters (off the hot path)
    private readonly CancellationTokenSource m_pruneCompletionSource;
    private readonly Task m_pruneTask;
    private readonly PeriodicTimer? m_pruneTimer;

    private bool m_disposed;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new instance of the <see cref="KeyedSignal{TKey, TResult}"/> class.
    /// </summary>
    /// <param name="pruneInterval">
    /// Internal for pruning orphaned waiters.<br />
    /// Use <c>null</c> for default pruning interval of 1 minute.<br />
    /// Use <see cref="Timeout.InfiniteTimeSpan"/> for no pruning.
    /// </param>
    /// <param name="keyComparer">Key comparer; use <c>null</c> for default.</param>
    public KeyedSignal(TimeSpan? pruneInterval = null, IEqualityComparer<TKey>? keyComparer = null)
    {
        TimeSpan interval = pruneInterval ?? TimeSpan.FromMinutes(1);

        m_waiters = new ConcurrentDictionary<TKey, Waiters>(keyComparer ?? EqualityComparer<TKey>.Default);
        m_pruneCompletionSource = new CancellationTokenSource();

        if (interval == Timeout.InfiniteTimeSpan || interval <= TimeSpan.Zero)
        {
            m_pruneTask = Task.CompletedTask;
        }
        else
        {
            m_pruneTimer = new PeriodicTimer(interval);
            m_pruneTask = Task.Run(() => PruneLoopAsync(m_pruneCompletionSource.Token));
        }
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets the number of pending keys that have waiters.
    /// </summary>
    public int PendingKeysCount => m_waiters.Count;

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Releases all the resources used by the <see cref="KeyedSignal{TKey, TResult}"/> object.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        
        try
        {
            m_pruneCompletionSource.Cancel();

            try
            {
                m_pruneTask.Wait();
            }
            catch (Exception ex)
            {
                LibraryEvents.OnSuppressedException(this, ex);
            }

            m_pruneTimer?.Dispose();
            m_pruneCompletionSource.Dispose();
        }
        finally
        {
            m_disposed = true;  // Prevent duplicate dispose.
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (m_disposed)
            return;

        try
        {
            await m_pruneCompletionSource.CancelAsync();

            try
            {
                await m_pruneTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LibraryEvents.OnSuppressedException(this, ex);
            }

            m_pruneTimer?.Dispose();
            m_pruneCompletionSource.Dispose();
        }
        finally
        {
            m_disposed = true;  // Prevent duplicate dispose.
        }
    }

    /// <summary>
    /// Waits synchronously for the specified <paramref name="key"/> to be signaled or failed.
    /// Multiple concurrent waiters on the same key complete together.
    /// </summary>
    /// <param name="key">Key to wait on.</param>
    /// <param name="cancellationToken">Optional token to cancel or timeout the wait.</param>
    /// <returns>
    /// A tuple where:
    /// <list type="bullet">
    /// <item>
    /// <c>result</c> is the signaled result when successful; otherwise <c>default</c>.
    /// </item>
    /// <item>
    /// <c>ex</c> is <c>null</c> on success; otherwise the exception that caused the failure,
    /// including <see cref="OperationCanceledException"/> when canceled.
    /// </item>
    /// </list>
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// A failure triggered by <see cref="Fail"/> returns <c>(default, ex)</c> where <c>ex</c> is the
    /// exception provided to <see cref="Fail"/>.Cancellation is treated like a failure where returned
    /// <c>ex</c> is an <see cref="OperationCanceledException"/>.
    /// </para>
    /// <para>
    /// For asynchronous waiting, use <see cref="WaitAsync"/>.
    /// </para>
    /// </remarks>
    public (TResult result, Exception? ex) Wait(TKey key, CancellationToken cancellationToken = default)
    {
        try
        {
            TResult result = WaitAsync(key, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
            return (result, null);
        }
        catch (Exception ex)
        {
            return (default!, ex);
        }
    }

    /// <summary>
    /// Asynchronously waits for <paramref name="key"/> to be signaled (or failed).
    /// Multiple concurrent waiters on the same key complete together.
    /// </summary>
    /// <param name="key">Key to wait on.</param>
    /// <param name="cancellationToken">Optional token to cancel or timeout the wait.</param>
    /// <returns>A task that completes when signaled, failed, or canceled.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
    /// <remarks>
    /// We never remove the per-key bucket on cancellation—only individual waiters.
    /// Buckets are removed on <see cref="Signal"/>/<see cref="Fail"/> or by periodic pruning.
    /// A mapping-stability check ensures we never add to an orphaned bucket.
    /// </remarks>
    public Task<TResult> WaitAsync(TKey key, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<TResult>(cancellationToken);

        TaskCompletionSource<TResult> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Waiters bucket;

        // Handle possible race condition where mapping changes between GetOrAdd and lock
        while (true)
        {
            bucket = m_waiters.GetOrAdd(key, static _ => new Waiters());

            // Check mapping-stability under the bucket lock
            lock (bucket.Gate)
            {
                // Check if mapping changed, e.g., signaled/failed and removed, in which case continue and retry
                if (!m_waiters.TryGetValue(key, out Waiters? current) || !ReferenceEquals(current, bucket))
                    continue;

                bucket.List.Add(source);
                break;
            }
        }

        // Cancellation removes only this waiter, not the bucket
        CancellationTokenRegistration tokenRegistration = default;

        if (cancellationToken.CanBeCanceled)
        {
            // Track disposable registration to cancel this waiter
            tokenRegistration = cancellationToken.Register(static state =>
            {
                (Waiters bucket, TaskCompletionSource<TResult> source, CancellationToken cancellationToken) =
                    ((Waiters, TaskCompletionSource<TResult>, CancellationToken))state!;

                bool removed;

                lock (bucket.Gate)
                    removed = bucket.List.Remove(source);

                if (removed)
                    source.TrySetCanceled(cancellationToken);
            },
            (bucket, source, cancellationToken));
        }

        // Ensure registration is disposed on all completion paths
        _ = source.Task.ContinueWith(static (_, state) =>
        {
            ((CancellationTokenRegistration)state!).Dispose();
        },
        tokenRegistration, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        return source.Task;
    }

    /// <summary>
    /// Tries to signal all current waiters for <paramref name="key"/> with <paramref name="result"/>.
    /// New waiters added after this call for same key will await a subsequent signal/fail.
    /// </summary>
    /// <param name="key">Key to signal.</param>
    /// <param name="result">Result to signal to all waiters.</param>
    /// <returns>
    /// <c>true</c> if the signal was successful; otherwise,
    /// <c>false</c> if there were no waiters for the key.
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
    public bool TrySignal(TKey key, TResult result)
    {
        return Signal(key, result) > 0;
    }

    /// <summary>
    /// Signals all current waiters for <paramref name="key"/> with <paramref name="result"/>.
    /// New waiters added after this call for same key will await a subsequent signal/fail.
    /// </summary>
    /// <param name="key">Key to signal.</param>
    /// <param name="result">Result to signal to all waiters.</param>
    /// <returns>The number of waiters released.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
    public int Signal(TKey key, TResult result)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        if (!m_waiters.TryRemove(key, out Waiters? bucket))
            return 0;

        TaskCompletionSource<TResult>[] sources = bucket.Drain();

        foreach (TaskCompletionSource<TResult> source in sources)
            source.TrySetResult(result);

        return sources.Length;
    }

    /// <summary>
    /// Tries to fail all current waiters for <paramref name="key"/> with <paramref name="ex"/>.
    /// New waiters added after this call for same key will await a subsequent signal/fail.
    /// </summary>
    /// <param name="key">Key to fail.</param>
    /// <param name="ex">Exception to signal to all waiters.</param>
    /// <returns>
    /// <c>true</c> if the fail was successful; otherwise,
    /// <c>false</c> if there were no waiters for the key.
    /// </returns>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
    public bool TryFail(TKey key, Exception ex)
    {
        return Fail(key, ex) > 0;
    }

    /// <summary>
    /// Fails all current waiters for <paramref name="key"/> with <paramref name="ex"/>.
    /// New waiters added after this call for same key will await a subsequent signal/fail.
    /// </summary>
    /// <param name="key">Key to fail.</param>
    /// <param name="ex">Exception to signal to all waiters.</param>
    /// <returns>The number of waiters released.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
    public int Fail(TKey key, Exception ex)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        if (!m_waiters.TryRemove(key, out Waiters? bucket))
            return 0;

        TaskCompletionSource<TResult>[] sources = bucket.Drain();

        foreach (TaskCompletionSource<TResult> source in sources)
            source.TrySetException(ex);

        return sources.Length;
    }

    // Background prune loop scans and prunes empty buckets at configured interval.
    private async Task PruneLoopAsync(CancellationToken token)
    {
        try
        {
            while (await m_pruneTimer!.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                foreach ((TKey key, Waiters value) in m_waiters)
                    TryPruneEmpty(key, value);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    // Attempts to remove the mapping, key to bucket, if bucket is still empty
    // and the dictionary still maps to exact instance. This avoids race condition
    // where a newer bucket of waiters could have been created for the same key.
    private void TryPruneEmpty(TKey key, Waiters bucket)
    {
        lock (bucket.Gate)
        {
            if (bucket.List.Count != 0)
                return;

            // Ensure mapping still points to this bucket
            if (!m_waiters.TryGetValue(key, out Waiters? current) || !ReferenceEquals(current, bucket))
                return;

            // Value-sensitive remove (compare-and-remove), checks key AND value
            ((ICollection<KeyValuePair<TKey, Waiters>>)m_waiters)
                .Remove(new KeyValuePair<TKey, Waiters>(key, bucket));
        }
    }

    #endregion
}
