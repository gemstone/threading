//******************************************************************************************************
//  NamedSemaphoreUnix.cs - Gbtc
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
//  11/09/2023 - Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Gemstone.Threading;

/*
   TODO: Implement strategy for cleaning up named semaphores that are no longer in use.

   Since POSIX named semaphores have kernel persistence and must be removed by removed by "sem_unlink" when no longer needed.

   > Note the following challenges:

   1) Kernel Persistence: POSIX named semaphores are persisted in the kernel, meaning they outlive the process that created them. This is
      useful for inter-process communication, but tricky to manage.
   
   2) sem_unlink Responsibility: Deciding which application or part of the application should call sem_unlink is indeed a critical design
      decision. If the semaphore is intended for use by multiple applications, you typically want the last process to exit to perform the
      cleanup. However, identifying the "last" process can be complex, especially in dynamic environments.
   
   3) Resource Leaks Due to Crashes: If an application crashes before it can call sem_unlink, the semaphore remains in the system, leading
      to potential resource leaks. This is a risk in any system where cleanup is the responsibility of the process.

   > Thoughts on how to handle these challenges

   Use reference counting for named semaphore instances and track last access time with an app ID using a persisted file:

   Reference Counting and App ID: Each time a process accesses a semaphore, it increments the count in a shared file and records its app ID.
   When it's done, it decrements the count. This helps track how many processes are currently using the semaphore.
   
   Last Access Time: Recording the last access time of the semaphore can be useful for determining if the semaphore has been abandoned.
   For example, if the last access time is significantly old, and the reference count is not zero (which might indicate an abnormal termination
   of a process), a cleanup routine can be triggered.
   
   Cleanup Strategy: You could implement a cleanup strategy that kicks in based on certain conditions – for instance, if the semaphore hasn't
   been accessed for a predefined timeout period, or if the application that last incremented the reference count has terminated unexpectedly.
   
   Robustness and Error Handling: It's important to ensure that the file operations are robust and handle concurrency well. File locking
   mechanisms might be necessary to prevent race conditions when multiple processes are reading and writing to the file.
   
   Fallback and Recovery: Implement a recovery mechanism in case the file tracking system fails or becomes corrupt. This could be a separate
   monitoring process or part of the application startup routine.
 */

internal partial class NamedSemaphoreUnix : INamedSemaphore
{
    // DllImport code is in Gemstone.POSIX.c
    private const string ImportFileName = "./Gemstone.POSIX.so";

    private readonly ref struct ErrorNo
    {
        public const int ENOENT = 2;
        public const int EINTR= 4;
        public const int EAGAIN = 11;
        public const int ENOMEM = 12;
        public const int EACCES = 13;
        public const int EINVAL = 22;
        public const int ENFILE = 23;
        public const int EMFILE = 24;
        public const int ENAMETOOLONG = 36;
        public const int EOVERFLOW = 75;
        public const int ETIMEDOUT = 110;
    }

    private sealed partial class SemaphorePtr : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SemaphorePtr(nint handle) : base(true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
        {
            return CloseSemaphore(handle) == 0;
        }

        [DllImport(ImportFileName)]
        private static extern int CloseSemaphore(nint semaphore);
    }

    private SemaphorePtr? m_semaphore;
    private SafeWaitHandle? m_safeWaitHandle;
    private string? m_namespace;
    private int m_maximumCount;

    [AllowNull]
    public SafeWaitHandle SafeWaitHandle
    {
        get => m_safeWaitHandle ?? new SafeWaitHandle(NamedSemaphore.InvalidHandle, false);
        set => m_safeWaitHandle = value;
    }

    public void CreateSemaphoreCore(int initialCount, int maximumCount, string name, out bool createdNew)
    {
        if (initialCount < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCount), "Non-negative number required.");

        if (maximumCount < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumCount), "Positive number required.");

        if (initialCount > maximumCount)
            throw new ArgumentException("The initial count for the semaphore must be greater than or equal to zero and less than the maximum count.");

        m_maximumCount = maximumCount;

        int namespaceSeparatorIndex = name.IndexOf('\\');

        if (namespaceSeparatorIndex > 0)
        {
            string namespaceName = name[..namespaceSeparatorIndex];

            if (namespaceName != "Global" && namespaceName != "Local")
                throw new ArgumentException("When using a namespace, the name of the semaphore must be prefixed with either \"Global\\\" or \"Local\\\".");

            m_namespace = namespaceName;
            name = name[(namespaceSeparatorIndex + 1)..];
        }

        if (name.Length > 250)
            throw new ArgumentException("The name of the semaphore must be less than 251 characters.");

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The name of the semaphore contains invalid characters.");

        int retval = CreateSemaphore(name, initialCount, out createdNew, out nint semaphoreHandle);

        switch (retval)
        {
            case 0 when semaphoreHandle > 0:
                m_semaphore = new SemaphorePtr(semaphoreHandle);
                SafeWaitHandle = new SafeWaitHandle(semaphoreHandle, false);
                return;
            case ErrorNo.EACCES:
                throw new UnauthorizedAccessException("The named semaphore exists, but the user does not have the security access required to use it.");
            case ErrorNo.EINVAL:
                throw new ArgumentOutOfRangeException(nameof(initialCount), "The value was greater than SEM_VALUE_MAX.");
            case ErrorNo.EMFILE:
                throw new IOException("The per-process limit on the number of open file descriptors has been reached.");
            case ErrorNo.ENAMETOOLONG:
                throw new PathTooLongException("The 'name' is too long. Length restrictions may depend on the operating system or configuration.");
            case ErrorNo.ENFILE:
                throw new IOException("The system limit on the total number of open files has been reached.");
            case ErrorNo.ENOENT:
                throw new WaitHandleCannotBeOpenedException("The 'name' is not well formed.");
            case ErrorNo.ENOMEM:
                throw new OutOfMemoryException("Insufficient memory to create the named semaphore.");
            default:
                if (semaphoreHandle == 0)
                    throw new WaitHandleCannotBeOpenedException("The semaphore handle is invalid.");

                throw new InvalidOperationException($"An unknown error occurred while creating the named semaphore. Error code: {retval}");
        }
    }

    public void Dispose()
    {
        m_semaphore?.Dispose();
        m_semaphore = null;
    }

    public static OpenExistingResult OpenExistingWorker(string name, out INamedSemaphore? semaphore)
    {
        semaphore = null;

        int namespaceSeparatorIndex = name.IndexOf('\\');

        if (namespaceSeparatorIndex > 0)
        {
            string namespaceName = name[..namespaceSeparatorIndex];

            if (namespaceName != "Global" && namespaceName != "Local")
                return OpenExistingResult.NameInvalid;

            name = name[(namespaceSeparatorIndex + 1)..];
        }

        if (name.Length > 250)
            return OpenExistingResult.NameInvalid;

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return OpenExistingResult.NameInvalid;

        int retval = OpenExistingSemaphore(name, out nint semaphoreHandle);

        switch (retval)
        {
            case 0 when semaphoreHandle > 0:
                semaphore = new NamedSemaphoreUnix
                {
                    m_semaphore = new SemaphorePtr(semaphoreHandle),
                    SafeWaitHandle = new SafeWaitHandle(semaphoreHandle, false)
                };

                return OpenExistingResult.Success;
            case ErrorNo.ENAMETOOLONG:
                return OpenExistingResult.PathTooLong;
            default:
                // Just return NameNotFound for all other errors
                return OpenExistingResult.NameNotFound;
        }
    }

    public int ReleaseCore(int releaseCount)
    {
        if (m_semaphore is null)
            throw new ObjectDisposedException(nameof(NamedSemaphoreUnix));

        if (releaseCount < 1)
            throw new ArgumentOutOfRangeException(nameof(releaseCount), "Non-negative number required.");

        int? previousCount = null;

        for (int i = 0; i < releaseCount; i++)
        {
            int retval = GetSemaphoreCount(m_semaphore, out int count);
            
            if (retval == ErrorNo.EINVAL)
                throw new InvalidOperationException("The named semaphore is invalid.");

            if (retval != 0)
                throw new InvalidOperationException($"An unknown error occurred while getting current count for the named semaphore. Error code: {retval}");

            if (count >= m_maximumCount)
                throw new SemaphoreFullException("The semaphore count is already at the maximum value.");

            previousCount ??= count;

            retval = ReleaseSemaphore(m_semaphore);

            switch (retval)
            {
                case 0:
                    continue;
                case ErrorNo.EOVERFLOW:
                    throw new SemaphoreFullException("The maximum count for the semaphore would be exceeded.");
                case ErrorNo.EINVAL:
                    throw new InvalidOperationException("The named semaphore is invalid.");
                default:
                    throw new InvalidOperationException($"An unknown error occurred while releasing the named semaphore. Error code: {retval}");
            }
        }

        return previousCount ?? 0;
    }

    public void Close()
    {
        Dispose();
    }

    public bool WaitOne()
    {
        return WaitOne(Timeout.Infinite);
    }

    public bool WaitOne(TimeSpan timeout)
    {
        return WaitOne(ToTimeoutMilliseconds(timeout));
    }

    public bool WaitOne(int millisecondsTimeout)
    {
        if (m_semaphore is null)
            return false;

        int retval = WaitSemaphore(m_semaphore, millisecondsTimeout);

        return retval switch
        {
            0 => true,
            ErrorNo.EINTR => false,
            ErrorNo.EAGAIN => false,
            ErrorNo.ETIMEDOUT => false,
            ErrorNo.EINVAL => throw new InvalidOperationException("The named semaphore is invalid."),
            _ => throw new InvalidOperationException($"An unknown error occurred while releasing the named semaphore. Error code: {retval}")
        };
    }

    public bool WaitOne(int millisecondsTimeout, bool exitContext)
    {
        return WaitOne(millisecondsTimeout);
    }

    public bool WaitOne(TimeSpan timeout, bool exitContext)
    {
        return WaitOne(timeout);
    }

    internal static int ToTimeoutMilliseconds(TimeSpan timeout)
    {
        long timeoutMilliseconds = (long)timeout.TotalMilliseconds;
            
        return timeoutMilliseconds switch
        {
            < -1 => throw new ArgumentOutOfRangeException(nameof(timeout), "Argument must be either non-negative and less than or equal to Int32.MaxValue or -1"),
            > int.MaxValue => throw new ArgumentOutOfRangeException(nameof(timeout), "Argument must be less than or equal to Int32.MaxValue milliseconds."),
            _ => (int)timeoutMilliseconds
        };
    }

    [DllImport(ImportFileName)]
    private static extern int CreateSemaphore(string name, int initialCount, out bool createdNew, out nint semaphoreHandle);

    [DllImport(ImportFileName)]
    private static extern int OpenExistingSemaphore(string name, out nint semaphoreHandle);

    [DllImport(ImportFileName)]
    private static extern int GetSemaphoreCount(SemaphorePtr semaphore, out int count);

    [DllImport(ImportFileName)]
    private static extern int ReleaseSemaphore(SemaphorePtr semaphore);

    [DllImport(ImportFileName)]
    private static extern int WaitSemaphore(SemaphorePtr semaphore, int timeout);

    [DllImport(ImportFileName)]
    private static extern int UnlinkSemaphore(string name);
}
