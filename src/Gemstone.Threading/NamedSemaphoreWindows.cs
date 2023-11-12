//******************************************************************************************************
//  NamedSemaphoreWindows.cs - Gbtc
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
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Gemstone.Threading
{
    internal class NamedSemaphoreWindows : INamedSemaphore
    {
        private Semaphore? m_semaphore;

        [AllowNull]
        public SafeWaitHandle SafeWaitHandle
        {
            get => m_semaphore?.SafeWaitHandle ?? new SafeWaitHandle(NamedSemaphore.InvalidHandle, false);
            set
            {
                if (m_semaphore is null)
                    return;

                m_semaphore.SafeWaitHandle = value;
            }
        }

        public void CreateSemaphoreCore(int initialCount, int maximumCount, string name, out bool createdNew)
        {
            m_semaphore = new Semaphore(initialCount, maximumCount, name, out createdNew);
        }

        public void Dispose()
        {
            m_semaphore?.Dispose();
        }

        [SupportedOSPlatform("windows")]
        public static OpenExistingResult OpenExistingWorker(string name, out INamedSemaphore? semaphore)
        {
            semaphore = null;

            try
            {
                semaphore = new NamedSemaphoreWindows { m_semaphore = Semaphore.OpenExisting(name) };

                return OpenExistingResult.Success;
            }
            catch (ArgumentException)
            {
                return OpenExistingResult.NameInvalid;
            }
            catch (PathTooLongException)
            {
                return OpenExistingResult.PathTooLong;
            }
            catch (IOException)
            {
                return OpenExistingResult.NameInvalid;
            }
            catch (WaitHandleCannotBeOpenedException ex)
            {
                return string.IsNullOrWhiteSpace(ex.Message)
                    ? OpenExistingResult.NameNotFound
                    : OpenExistingResult.NameInvalid;
            }
            catch (Exception)
            {
                return OpenExistingResult.NameNotFound;
            }
        }

        public int ReleaseCore(int releaseCount)
        {
            return m_semaphore?.Release(releaseCount) ?? 0;
        }

        public void Close()
        {
            m_semaphore?.Close();
        }

        public bool WaitOne()
        {
            return m_semaphore?.WaitOne() ?? false;
        }

        public bool WaitOne(TimeSpan timeout)
        {
            return m_semaphore?.WaitOne(timeout) ?? false;
        }

        public bool WaitOne(int millisecondsTimeout)
        {
            return m_semaphore?.WaitOne(millisecondsTimeout) ?? false;
        }

        public bool WaitOne(TimeSpan timeout, bool exitContext)
        {
            return m_semaphore?.WaitOne(timeout, exitContext) ?? false;
        }

        public bool WaitOne(int millisecondsTimeout, bool exitContext)
        {
            return m_semaphore?.WaitOne(millisecondsTimeout, exitContext) ?? false;
        }
    }
}
