//******************************************************************************************************
//  AsyncLock.cs - Gbtc
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
//  06/29/2020 - Stephen C. Wills
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Gemstone.Threading
{
    /// <summary>
    /// Represents a lock that can be awaited to obtain exclusive
    /// access to resources within a critical region of code.
    /// </summary>
    public class AsyncLock
    {
        private Task m_task = Task.CompletedTask;

        /// <summary>
        /// Attempts to obtain exclusive access to the lock.
        /// </summary>
        /// <returns>
        /// A task that, if cancelled, indicates the lock was not taken,
        /// and must be awaited to obtain the token that will release the
        /// lock on <see cref="IDisposable.Dispose"/>.
        /// </returns>
        /// <exception cref="TaskCanceledException">The lock could not be taken.</exception>
        public Task<IDisposable> TryEnterAsync() =>
            TryEnterAsync(TimeSpan.Zero);

        /// <summary>
        /// Attempts to obtain exclusive access to the lock.
        /// </summary>
        /// <param name="milliseconds">The number of milliseconds to wait before failing to take the lock.</param>
        /// <returns>
        /// A task that, if cancelled, indicates the lock was not taken,
        /// and must be awaited to obtain the token that will release the
        /// lock on <see cref="IDisposable.Dispose"/>.
        /// </returns>
        /// <exception cref="TaskCanceledException">The timeout expires before the lock could be taken.</exception>
        public Task<IDisposable> TryEnterAsync(int milliseconds) =>
            TryEnterAsync(TimeSpan.FromMilliseconds(milliseconds));

        /// <summary>
        /// Attempts to obtain exclusive access to the lock.
        /// </summary>
        /// <param name="timeout">The amount of time to wait before failing to take the lock.</param>
        /// <returns>
        /// A task that, if cancelled, indicates the lock was not taken,
        /// and must be awaited to obtain the token that will release the
        /// lock on <see cref="IDisposable.Dispose"/>.
        /// </returns>
        /// <exception cref="TaskCanceledException">The <paramref name="timeout"/> expires before the lock could be taken.</exception>
        /// <remarks>
        /// <para>
        /// The following illustrates an example of using try-catch to detect a failure to take the lock.
        /// </para>
        /// 
        /// <code>
        /// AsyncLock asyncLock = new AsyncLock();
        /// 
        /// try
        /// {
        ///     using IDisposable token = await asyncLock.TryEnterAsync();
        ///     // Critical region
        /// }
        /// catch (TaskCanceledException)
        /// {
        ///     // Lock failed
        /// }
        /// </code>
        /// 
        /// <para>
        /// The following illustrates an example of using <see cref="Task.ContinueWith{TResult}(Func{Task, TResult})"/>
        /// to detect a failure to take the lock.
        /// </para>
        /// 
        /// <code>
        /// AsyncLock asyncLock = new AsyncLock();
        /// 
        /// await asyncLock.TryEnterAsync().ContinueWith(async tokenTask =>
        /// {
        ///     if (tokenTask.IsCanceled)
        ///     {
        ///         // Lock failed
        ///         return;
        ///     }
        /// 
        ///     using IDisposable token = await tokenTask;
        ///     // Critical region
        /// }).Unwrap();
        /// </code>
        /// </remarks>
        public async Task<IDisposable> TryEnterAsync(TimeSpan timeout)
        {
            LockEntry lockEntry = new LockEntry();

            try
            {
                Task timeoutTask = Task.Delay(timeout);
                Task lockReleasedTask = Interlocked.Exchange(ref m_task, lockEntry.WhenReleased());
                await Task.WhenAny(timeoutTask, lockReleasedTask).ConfigureAwait(false);

                if (!lockReleasedTask.IsCompleted)
                    throw new TaskCanceledException("Timed out while attempting to get exclusive access to async lock.");

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
        /// Obtains exclusive access to the lock.
        /// </summary>
        /// <returns>
        /// A task that must be awaited to obtain the token that will
        /// release the lock on <see cref="IDisposable.Dispose"/>.
        /// </returns>
        public async Task<IDisposable> EnterAsync()
        {
            LockEntry lockEntry = new LockEntry();
            await Interlocked.Exchange(ref m_task, lockEntry.WhenReleased()).ConfigureAwait(false);
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
}
