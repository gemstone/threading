//******************************************************************************************************
//  AsyncReaderWriterLock.cs - Gbtc
//
//  Copyright © 2022, Grid Protection Alliance.  All Rights Reserved.
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
//  06/28/2020 - Stephen C. Wills
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Gemstone.Threading;

/// <summary>
/// Represents an asynchronous implementation of a reader/writer lock.
/// </summary>
public class AsyncReaderWriterLock
{
    private AsyncLock ReaderListLock { get; } = new();
    private List<Task> ReaderList { get; } = new();

    /// <summary>
    /// Attempts to enter the lock with concurrent access where all
    /// readers can execute concurrently with respect to each other.
    /// </summary>
    /// <param name="milliseconds">The number of milliseconds to wait before timing out.</param>
    /// <returns>The token used to control the duration of entry.</returns>
    /// <exception cref="TimeoutException">The lock could not be entered before the timeout expired.</exception>
    public Task<IDisposable> TryEnterReadLockAsync(int milliseconds) =>
        TryEnterReadLockAsync(TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>
    /// Attempts to enter the lock with concurrent access where all
    /// readers can execute concurrently with respect to each other.
    /// </summary>
    /// <param name="timeout">The amount of time to wait before timing out.</param>
    /// <returns>The token used to control the duration of entry.</returns>
    /// <exception cref="TimeoutException">The lock could not be entered before the <paramref name="timeout"/> expired.</exception>
    public async Task<IDisposable> TryEnterReadLockAsync(TimeSpan timeout)
    {
        LockEntry lockEntry = new();

        try
        {
            Task readerTask = lockEntry.WhenReleased();

            // The exclusive lock must be obtained to update the reader list,
            // but it's also necessary so that the reader will wait on any
            // writers that entered before it
            using (await ReaderListLock.TryEnterAsync(timeout).ConfigureAwait(false))
                ReaderList.Add(readerTask);

            // Prevent the reader list from growing indefinitely
            _ = readerTask.ContinueWith(async _ =>
            {
                using (await ReaderListLock.EnterAsync().ConfigureAwait(false))
                    ReaderList.Remove(readerTask);
            });

            return lockEntry;
        }
        catch
        {
            // Make sure to dispose the lock entry since
            // it's not being returned to the caller
            lockEntry.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Attempts to enter the lock with exclusive access where no other
    /// readers or writers can execute concurrently with the writer.
    /// </summary>
    /// <param name="milliseconds">The number of milliseconds to wait before timing out.</param>
    /// <returns>The token used to control the duration of entry.</returns>
    /// <exception cref="TimeoutException">The lock could not be entered before the timeout expired.</exception>
    public Task<IDisposable> TryEnterWriteLockAsync(int milliseconds) =>
        TryEnterWriteLockAsync(TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>
    /// Attempts to enter the lock with exclusive access where no other
    /// readers or writers can execute concurrently with the writer.
    /// </summary>
    /// <param name="timeout">The amount of time to wait before timing out.</param>
    /// <returns>The token used to control the duration of entry.</returns>
    /// <exception cref="TimeoutException">The lock could not be entered before the <paramref name="timeout"/> expired.</exception>
    public async Task<IDisposable> TryEnterWriteLockAsync(TimeSpan timeout)
    {
        LockEntry lockEntry = new();

        try
        {
            // The writer must maintain exclusive access until the write operation is complete
            IDisposable readerListToken = await ReaderListLock.TryEnterAsync(timeout).ConfigureAwait(false);
            _ = lockEntry.WhenReleased().ContinueWith(_ => readerListToken.Dispose());

            Task timeoutTask = Task.Delay(timeout);
            Task readerTask = Task.WhenAll(ReaderList);
            await Task.WhenAny(timeoutTask, readerTask).ConfigureAwait(false);

            if (!readerTask.IsCompleted)
                throw new TaskCanceledException("Timed out waiting for readers to complete.");

            // Completed readers will eventually remove themselves,
            // but may as well remove them all here
            ReaderList.Clear();
            return lockEntry;
        }
        catch
        {
            // Make sure to dispose the lock entry since
            // it's not being returned to the caller
            lockEntry.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Enters the lock with concurrent access where all readers
    /// can execute concurrently with respect to each other.
    /// </summary>
    /// <returns>The token used to control the duration of entry.</returns>
    public async Task<IDisposable> EnterReadAsync()
    {
        LockEntry lockEntry = new();
        Task readerTask = lockEntry.WhenReleased();

        // The exclusive lock must be obtained to update the reader list,
        // but it's also necessary so that the reader will wait on any
        // writers that entered before it
        using (await ReaderListLock.EnterAsync().ConfigureAwait(false))
            ReaderList.Add(readerTask);

        // Prevent the reader list from growing indefinitely
        _ = readerTask.ContinueWith(async _ =>
        {
            using (await ReaderListLock.EnterAsync().ConfigureAwait(false))
                ReaderList.Remove(readerTask);
        });

        return lockEntry;
    }

    /// <summary>
    /// Enters the lock with exclusive access where no other
    /// readers or writers can execute concurrently with the writer.
    /// </summary>
    /// <returns>The token used to control the duration of entry.</returns>
    public async Task<IDisposable> EnterWriteAsync()
    {
        LockEntry lockEntry = new();

        // The writer must maintain exclusive access until the write operation is complete
        IDisposable readerListToken = await ReaderListLock.EnterAsync().ConfigureAwait(false);
        _ = lockEntry.WhenReleased().ContinueWith(_ => readerListToken.Dispose());

        await Task.WhenAll(ReaderList).ConfigureAwait(false);
        ReaderList.Clear();

        return lockEntry;
    }

    private class LockEntry : IDisposable
    {
        private TaskCompletionSource<object?> TaskCompletionSource { get; }

        public LockEntry() =>
            TaskCompletionSource = TaskCompletionSourceFactory.CreateNew<object?>();

        public Task WhenReleased() =>
            TaskCompletionSource.Task;

        public void Dispose() =>
            TaskCompletionSource.SetResult(null);
    }
}
