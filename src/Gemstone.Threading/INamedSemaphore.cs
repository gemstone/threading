//******************************************************************************************************
//  INamedSemaphore.cs - Gbtc
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
// ReSharper disable UnusedMember.Global

using System;
using Microsoft.Win32.SafeHandles;

namespace Gemstone.Threading
{
    internal enum OpenExistingResult
    {
        Success,
        NameNotFound,
        NameInvalid,
        PathTooLong,
        AccessDenied
    }

    internal interface INamedSemaphore : IDisposable
    {
        SafeWaitHandle? SafeWaitHandle { get; set; }

        void CreateSemaphoreCore(int initialCount, int maximumCount, string name, out bool createdNew);

        int ReleaseCore(int releaseCount);

        void Close();

        bool WaitOne();

        bool WaitOne(TimeSpan timeout);

        bool WaitOne(int millisecondsTimeout);

        bool WaitOne(TimeSpan timeout, bool exitContext);

        bool WaitOne(int millisecondsTimeout, bool exitContext);
    }
}
