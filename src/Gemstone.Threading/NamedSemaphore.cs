//******************************************************************************************************
//  NamedSemaphore.cs - Gbtc
//
//  Copyright © 2023, Grid Protection Alliance.  All Rights Reserved.
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
//  11/09/2023 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************
// ReSharper disable InconsistentNaming
// ReSharper disable OutParameterValueIsAlwaysDiscarded.Local
#pragma warning disable CA1416

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Gemstone.Threading;

/// <summary>
/// Represents a cross-platform interprocess named semaphore that limits the number
/// of threads that can access a resource or pool of resources concurrently.
/// </summary>
public class NamedSemaphore : WaitHandle
{
    private readonly INamedSemaphore m_semaphore;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedSemaphore" /> class, specifying the initial number of entries,
    /// the maximum number of concurrent entries, and the name of a system semaphore object.
    /// </summary>
    /// <param name="initialCount">The initial number of requests for the semaphore that can be granted concurrently.</param>
    /// <param name="maximumCount">The maximum number of requests for the semaphore that can be granted concurrently.</param>
    /// <param name="name">
    /// The name used to identity the synchronization object resource shared with other processes.
    /// The name is case-sensitive. The backslash character (\\) is reserved and may only be used to specify a namespace.
    /// On Unix-based operating systems, the name after excluding the namespace must be a valid file name which contains
    /// no slashes beyond optional namespace backslash and is limited to 250 characters. 
    /// </param>
    public NamedSemaphore(int initialCount, int maximumCount, string name) :
        this(initialCount, maximumCount, name, out _)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedSemaphore" /> class, specifying the initial number of entries,
    /// the maximum number of concurrent entries, the name of a system semaphore object, and specifying a variable that
    /// receives a value indicating whether a new system semaphore was created.
    /// </summary>
    /// <param name="initialCount">The initial number of requests for the semaphore that can be granted concurrently.</param>
    /// <param name="maximumCount">The maximum number of requests for the semaphore that can be granted concurrently.</param>
    /// <param name="name">
    /// The name used to identity the synchronization object resource shared with other processes.
    /// The name is case-sensitive. The backslash character (\\) is reserved and may only be used to specify a namespace.
    /// On Unix-based operating systems, the name after excluding the namespace must be a valid file name which contains
    /// no slashes beyond optional namespace backslash and is limited to 250 characters. 
    /// </param>
    /// <param name="createdNew">
    /// When method returns, contains <c>true</c> if the specified named system semaphore was created; otherwise,
    /// <c>false</c> if the semaphore already existed.
    /// </param>
    public NamedSemaphore(int initialCount, int maximumCount, string name, out bool createdNew)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name), "Name cannot be null, empty or whitespace.");

        m_semaphore = Common.IsPosixEnvironment ?
            new NamedSemaphoreUnix() :
            new NamedSemaphoreWindows();

        m_semaphore.CreateSemaphoreCore(initialCount, maximumCount, name, out createdNew);
    }

    private NamedSemaphore(INamedSemaphore semaphore)
    {
        m_semaphore = semaphore;
    }

    /// <summary>
    /// Gets or sets the native operating system handle.
    /// </summary>
    public new SafeWaitHandle SafeWaitHandle
    {
        get => m_semaphore.SafeWaitHandle;
        set => base.SafeWaitHandle = m_semaphore.SafeWaitHandle = value;
    }

    /// <summary>
    /// When overridden in a derived class, releases the unmanaged resources used by the <see cref="NamedSemaphore" />,
    /// and optionally releases the managed resources.
    /// </summary>
    /// <param name="explicitDisposing">
    /// <c>true</c> to release both managed and unmanaged resources; otherwise, <c>false</c> to release only
    /// unmanaged resources.
    /// </param>
    protected override void Dispose(bool explicitDisposing)
    {
        try
        {
            base.Dispose(explicitDisposing);
        }
        finally
        {
            m_semaphore.Dispose();
        }
    }

    /// <summary>
    /// Releases all resources held by the current <see cref="NamedSemaphore" />.
    /// </summary>
    public override void Close()
    {
        m_semaphore.Close();
    }

    /// <summary>
    /// Blocks the current thread until the current <see cref="NamedSemaphore" /> receives a signal.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the current instance receives a signal. If the current instance is never signaled,
    /// <see cref="NamedSemaphore" /> never returns.
    /// </returns>
    public override bool WaitOne()
    {
        return m_semaphore.WaitOne();
    }

    /// <summary>
    /// Blocks the current thread until the current instance receives a signal, using a <see cref="TimeSpan" />
    /// to specify the time interval.
    /// </summary>
    /// <param name="timeout">
    /// A <see cref="TimeSpan" /> that represents the number of milliseconds to wait, or a <see cref="TimeSpan" />
    /// that represents -1 milliseconds to wait indefinitely.
    /// </param>
    /// <returns><c>true</c> if the current instance receives a signal; otherwise, <c>false</c>.</returns>
    public override bool WaitOne(TimeSpan timeout)
    {
        return m_semaphore.WaitOne(timeout);
    }

    /// <summary>
    /// Blocks the current thread until the current instance receives a signal, using a 32-bit signed integer to
    /// specify the time interval in milliseconds.
    /// </summary>
    /// <param name="millisecondsTimeout">
    /// The number of milliseconds to wait, or <see cref="Timeout.Infinite" /> (-1) to wait indefinitely.
    /// </param>
    /// <returns><c>true</c> if the current instance receives a signal; otherwise, <c>false</c>.</returns>
    public override bool WaitOne(int millisecondsTimeout)
    {
        return m_semaphore.WaitOne(millisecondsTimeout);
    }

    /// <summary>
    /// Blocks the current thread until the current instance receives a signal, using a <see cref="TimeSpan" />
    /// to specify the time interval and specifying whether to exit the synchronization domain before the wait.
    /// </summary>
    /// <param name="timeout">
    /// A <see cref="TimeSpan" /> that represents the number of milliseconds to wait, or a <see cref="TimeSpan" />
    /// that represents -1 milliseconds to wait indefinitely.
    /// </param>
    /// <param name="exitContext">
    /// <c>true</c> to exit the synchronization domain for the context before the wait (if in a synchronized context),
    /// and reacquire it afterward; otherwise, <c>false</c>.
    /// </param>
    /// <returns><c>true</c> if the current instance receives a signal; otherwise, <c>false</c>.</returns>
    public override bool WaitOne(TimeSpan timeout, bool exitContext)
    {
        return m_semaphore.WaitOne(timeout, exitContext);
    }

    /// <summary>
    /// Blocks the current thread until the current <see cref="NamedSemaphore" /> receives a signal, using a
    /// 32-bit signed integer to specify the time interval and specifying whether to exit the synchronization
    /// domain before the wait.
    /// </summary>
    /// <param name="millisecondsTimeout">
    /// The number of milliseconds to wait, or <see cref="Timeout.Infinite" /> (-1) to wait indefinitely.
    /// </param>
    /// <param name="exitContext">
    /// <c>true</c> to exit the synchronization domain for the context before the wait (if in a synchronized context),
    /// and reacquire it afterward; otherwise, <c>false</c>.
    /// </param>
    /// <returns><c>true</c> if the current instance receives a signal; otherwise, <c>false</c>.</returns>
    public override bool WaitOne(int millisecondsTimeout, bool exitContext)
    {
        return m_semaphore.WaitOne(millisecondsTimeout, exitContext);
    }

    /// <summary>
    /// Exits the semaphore and returns the previous count.
    /// </summary>
    /// <returns>The count on the semaphore before the method was called.</returns>
    private int Release()
    {
        return m_semaphore.ReleaseCore(1);
    }

    /// <summary>
    /// Exits the semaphore a specified number of times and returns the previous count.
    /// </summary>
    /// <param name="releaseCount">The number of times to exit the semaphore.</param>
    /// <returns>The count on the semaphore before the method was called.</returns>
    public int Release(int releaseCount)
    {
        if (releaseCount < 1)
            throw new ArgumentOutOfRangeException(nameof(releaseCount), "Non-negative number required.");

        return m_semaphore.ReleaseCore(releaseCount);
    }

    private static OpenExistingResult OpenExistingWorker(string name, out INamedSemaphore? semaphore)
    {
        return Common.IsPosixEnvironment ? 
            NamedSemaphoreUnix.OpenExistingWorker(name, out semaphore) : 
            NamedSemaphoreWindows.OpenExistingWorker(name, out semaphore);
    }

    /// <summary>
    /// Opens the specified named semaphore, if it already exists.
    /// </summary>
    /// <param name="name">
    /// The name used to identity the synchronization object resource shared with other processes.
    /// The name is case-sensitive. The backslash character (\\) is reserved and may only be used to specify a namespace.
    /// On Unix-based operating systems, the name after excluding the namespace must be a valid file name which contains
    /// no slashes beyond optional namespace backslash and is limited to 250 characters. 
    /// </param>
    /// <returns>An object that represents the named system semaphore.</returns>
    public static NamedSemaphore OpenExisting(string name)
    {
        switch (OpenExistingWorker(name, out INamedSemaphore? result))
        {
            case OpenExistingResult.NameNotFound:
                throw new WaitHandleCannotBeOpenedException();
            case OpenExistingResult.NameInvalid:
                throw new WaitHandleCannotBeOpenedException($"Semaphore with name '{name}' cannot be created.");
            case OpenExistingResult.PathTooLong:
                throw new IOException($"Path too long for semaphore with name '{name}'.");
            case OpenExistingResult.AccessDenied:
                throw new UnauthorizedAccessException($"Access to the semaphore with name '{name}' is denied.");
            default:
                Debug.Assert(result is not null, "result should be non-null on success");
                return new NamedSemaphore(result);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="name">
    /// The name used to identity the synchronization object resource shared with other processes.
    /// The name is case-sensitive. The backslash character (\\) is reserved and may only be used to specify a namespace.
    /// On Unix-based operating systems, the name after excluding the namespace must be a valid file name which contains
    /// no slashes beyond optional namespace backslash and is limited to 250 characters. 
    /// </param>
    /// <param name="semaphore"></param>
    /// <returns></returns>
    public static bool TryOpenExisting(string name, [NotNullWhen(true)] out NamedSemaphore? semaphore)
    {
        if (OpenExistingWorker(name, out INamedSemaphore? result) == OpenExistingResult.Success)
        {
            semaphore = new NamedSemaphore(result!);
            return true;
        }

        semaphore = null;
        return false;
    }

    internal new static readonly IntPtr InvalidHandle = WaitHandle.InvalidHandle;
}
