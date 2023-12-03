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
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
#pragma warning disable VSSpell001

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Gemstone.Threading;

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

    #if NET
        [LibraryImport(ImportFileName)]
        private static partial int CloseSemaphore(nint semaphore);
    #else
        [DllImport(ImportFileName)]
        private static extern int CloseSemaphore(nint semaphore);
    #endif
    }

    private SemaphorePtr? m_semaphore;
    private int m_maximumCount;

    // We can ignore any user assigned SafeWaitHandle since we are using a custom SafeHandle implementation. Internal to
    // this class we assign a new new SafeWaitHandle around our semaphore pointer that the parent class then passes to
    // the WaitHandle base class so that the parent class can be used by standard WaitHandle class methods.
    public SafeWaitHandle? SafeWaitHandle { get; set; }

    public void CreateSemaphoreCore(int initialCount, int maximumCount, string name, out bool createdNew)
    {
        if (initialCount < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCount), "Non-negative number required.");

        if (maximumCount < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumCount), "Positive number required.");

        if (initialCount > maximumCount)
            throw new ArgumentException("The initial count for the semaphore must be greater than or equal to zero and less than the maximum count.");

        m_maximumCount = maximumCount;

        (bool result, ArgumentException? ex) = ParseSemaphoreName(name, out string? namespaceName, out string? semaphoreName);

        if (!result)
            throw ex!;

        int retVal = CreateSemaphore(semaphoreName!,namespaceName == "Global", initialCount, out createdNew, out nint semaphoreHandle);

        switch (retVal)
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

                throw new InvalidOperationException($"An unknown error occurred while creating the named semaphore. Error code: {retVal}");
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

        if (!ParseSemaphoreName(name, out string? semaphoreName))
            return OpenExistingResult.NameInvalid;

        int retVal = OpenExistingSemaphore(semaphoreName, out nint semaphoreHandle);

        switch (retVal)
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

    private static bool ParseSemaphoreName(string name, [NotNullWhen(true)] out string? semaphoreName)
    {
        return ParseSemaphoreName(name, out _, out semaphoreName).result;
    }

    private static (bool result, ArgumentException? ex) ParseSemaphoreName(string name, out string? namespaceName, out string? semaphoreName)
    {
        namespaceName = null;
        semaphoreName = null;

        int namespaceSeparatorIndex = name.IndexOf('\\');

        if (namespaceSeparatorIndex > 0)
        {
            namespaceName = name[..namespaceSeparatorIndex];

            if (namespaceName != "Global" && namespaceName != "Local")
                return (false, new ArgumentException("""When using a namespace, the name of the semaphore must be prefixed with either "Global\" or "Local\"."""));

            semaphoreName = name[(namespaceSeparatorIndex + 1)..];
        }
        else
        {
            semaphoreName = name;
        }

        if (semaphoreName.Length > 250)
            return (false, new ArgumentException("The name of the semaphore must be less than 251 characters."));

        if (semaphoreName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return (false, new ArgumentException("The name of the semaphore contains invalid characters."));

        // Linux named semaphores must start with a forward slash
        semaphoreName = $"/{semaphoreName}";

        return (true, null);
    }

    public int ReleaseCore(int releaseCount)
    {
        if (m_semaphore is null)
            throw new ObjectDisposedException(nameof(NamedSemaphoreUnix));

        if (releaseCount < 1)
            throw new ArgumentOutOfRangeException(nameof(releaseCount), "Non-negative number required.");

        int retVal = GetSemaphoreCount(m_semaphore, out int previousCount);

        if (retVal == ErrorNo.EINVAL)
            throw new InvalidOperationException("The named semaphore is invalid.");

        if (retVal != 0)
            throw new InvalidOperationException($"An unknown error occurred while getting current count for the named semaphore. Error code: {retVal}");

        if (previousCount >= m_maximumCount)
            throw new SemaphoreFullException("The semaphore count is already at the maximum value.");

        for (int i = 0; i < releaseCount; i++)
        {
            retVal = ReleaseSemaphore(m_semaphore);

            switch (retVal)
            {
                case 0:
                    continue;
                case ErrorNo.EOVERFLOW:
                    throw new SemaphoreFullException("The maximum count for the semaphore would be exceeded.");
                case ErrorNo.EINVAL:
                    throw new InvalidOperationException("The named semaphore is invalid.");
                default:
                    throw new InvalidOperationException($"An unknown error occurred while releasing the named semaphore. Error code: {retVal}");
            }
        }

        return previousCount;
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
        long totalMilliseconds = (long)timeout.TotalMilliseconds;

        int millisecondsTimeout = totalMilliseconds switch
        {
            < -1 => throw new ArgumentOutOfRangeException(nameof(timeout), "Argument milliseconds must be either non-negative and less than or equal to Int32.MaxValue or -1"),
            > int.MaxValue => throw new ArgumentOutOfRangeException(nameof(timeout), "Argument milliseconds must be less than or equal to Int32.MaxValue."),
            _ => (int)totalMilliseconds
        };

        return WaitOne(millisecondsTimeout);
    }

    public bool WaitOne(int millisecondsTimeout)
    {
        if (m_semaphore is null)
            return false;

        int retVal = WaitSemaphore(m_semaphore, millisecondsTimeout);

        return retVal switch
        {
            0 => true,
            ErrorNo.EINTR => false,
            ErrorNo.EAGAIN => false,
            ErrorNo.ETIMEDOUT => false,
            ErrorNo.EINVAL => throw new InvalidOperationException("The named semaphore is invalid."),
            _ => throw new InvalidOperationException($"An unknown error occurred while waiting on the named semaphore. Error code: {retVal}")
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

    public static void Unlink(string name)
    {
        (bool result, ArgumentException? ex) = ParseSemaphoreName(name, out _, out string? semaphoreName);

        if (!result)
            throw ex!;

        int retVal = UnlinkSemaphore(semaphoreName!);

        switch (retVal)
        {
            case 0:
                return;
            case ErrorNo.ENAMETOOLONG:
                throw new PathTooLongException("The 'name' is too long. Length restrictions may depend on the operating system or configuration.");
            case ErrorNo.ENOENT:
                throw new FileNotFoundException("There is no semaphore with the given 'name'.");
            case ErrorNo.EACCES:
                throw new UnauthorizedAccessException("The 'name' exists, but the user does not have the security access required to unlink it.");
            default:
                throw new InvalidOperationException($"An unknown error occurred while unlinking the named semaphore. Error code: {retVal}");
        }
    }

#if NET
    [LibraryImport(ImportFileName, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int CreateSemaphore(string name, [MarshalAs(UnmanagedType.I4)] bool useGlobalScope, int initialCount, [MarshalAs(UnmanagedType.I4)] out bool createdNew, out nint semaphoreHandle);

    [LibraryImport(ImportFileName, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenExistingSemaphore(string name, out nint semaphoreHandle);

    [LibraryImport(ImportFileName)]
    private static partial int GetSemaphoreCount(SemaphorePtr semaphore, out int count);

    [LibraryImport(ImportFileName)]
    private static partial int ReleaseSemaphore(SemaphorePtr semaphore);

    [LibraryImport(ImportFileName)]
    private static partial int WaitSemaphore(SemaphorePtr semaphore, int timeout);

    [LibraryImport(ImportFileName, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnlinkSemaphore(string name);
#else
    [DllImport(ImportFileName)]
    private static extern int CreateSemaphore(string name, bool useGlobalScope, int initialCount, out bool createdNew, out nint semaphoreHandle);

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
#endif
}
