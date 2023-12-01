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
// ReSharper disable UnusedMember.Global
#pragma warning disable CA1416

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Gemstone.Threading;

/// <summary>
/// Represents a cross-platform, interprocess named semaphore, which limits the number of threads that can concurrently 
/// access a resource or a pool of resources.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="NamedSemaphore"/> is a synchronization object that can be utilized across multiple processes.
/// </para>
/// <para>
/// On POSIX systems, the <see cref="NamedSemaphore"/> exhibits kernel persistence, meaning instances will remain
/// active beyond the lifespan of the creating process. Named semaphores must be explicitly removed by invoking 
/// <see cref="Unlink"/> when they are no longer needed. Kernel persistence necessitates careful design consideration
/// regarding the responsibility for invoking <see cref="Unlink"/>. Since the common use case for named semaphores is
/// across multiple applications, it is advisable for the last exiting process to handle the cleanup. In cases where
/// an application may crash before calling <see cref="Unlink"/>, the semaphore persists in the system, potentially
/// leading to resource leakage. Implementations should include strategies to address and mitigate this risk.
/// </para>
/// </remarks>
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
    /// The unique name identifying the semaphore. This name is case-sensitive. Use a backslash (\\) to specify a
    /// namespace, but avoid it elsewhere in the name. On Unix-based systems, the name should conform to valid file
    /// naming conventions, excluding slashes except for an optional namespace backslash. The name length is limited
    /// to 250 characters after any optional namespace.
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
    /// The unique name identifying the semaphore. This name is case-sensitive. Use a backslash (\\) to specify a
    /// namespace, but avoid it elsewhere in the name. On Unix-based systems, the name should conform to valid file
    /// naming conventions, excluding slashes except for an optional namespace backslash. The name length is limited
    /// to 250 characters after any optional namespace.
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
        get => m_semaphore.SafeWaitHandle ?? new SafeWaitHandle(InvalidHandle, false);
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
    public int Release()
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
    /// Opens an existing named semaphore.
    /// </summary>
    /// <param name="name">
    /// The unique name identifying the semaphore. This name is case-sensitive. Use a backslash (\\) to specify a
    /// namespace, but avoid it elsewhere in the name. On Unix-based systems, the name should conform to valid file
    /// naming conventions, excluding slashes except for an optional namespace backslash. The name length is limited
    /// to 250 characters after any optional namespace.
    /// </param>
    /// <returns>
    /// An object that represents the opened named semaphore.
    /// </returns>
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
    /// Opens the specified named semaphore, if it already exists, and returns a value that indicates whether the
    /// operation succeeded.
    /// </summary>
    /// <param name="name">
    /// The unique name identifying the semaphore. This name is case-sensitive. Use a backslash (\\) to specify a
    /// namespace, but avoid it elsewhere in the name. On Unix-based systems, the name should conform to valid file
    /// naming conventions, excluding slashes except for an optional namespace backslash. The name length is limited
    /// to 250 characters after any optional namespace.
    /// </param>
    /// <param name="semaphore">
    /// When this method returns, contains a <see cref="NamedSemaphore" /> object that represents the named semaphore
    /// if the call succeeded, or <c>null</c> if the call failed. This parameter is treated as uninitialized.
    /// </param>
    /// <returns>
    /// <c>true</c> if the named semaphore was opened successfully; otherwise, <c>false</c>. In some cases,
    /// <c>false</c> may be returned for invalid names.
    /// </returns>
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

    /// <summary>
    /// Removes a named semaphore.
    /// </summary>
    /// <param name="name">
    /// The unique name identifying the semaphore. This name is case-sensitive. Use a backslash (\\) to specify a
    /// namespace, but avoid it elsewhere in the name. On Unix-based systems, the name should conform to valid file
    /// naming conventions, excluding slashes except for an optional namespace backslash. The name length is limited
    /// to 250 characters after any optional namespace.
    /// </param>
    /// <remarks>
    /// On POSIX systems, calling this method removes the named semaphore referred to by <paramref name="name"/>.
    /// The semaphore name is removed immediately and is destroyed once all other processes that have the semaphore
    /// open close it. Calling this method on Windows systems does nothing.
    /// </remarks>
    public static void Unlink(string name)
    {
        if (Common.IsPosixEnvironment)
            NamedSemaphoreUnix.Unlink(name);
    }
}
