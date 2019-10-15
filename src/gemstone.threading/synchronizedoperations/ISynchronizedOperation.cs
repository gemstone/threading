//******************************************************************************************************
//  ISynchronizedOperation.cs - Gbtc
//
//  Copyright © 2014, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://www.opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  03/21/2014 - Stephen C. Wills
//       Generated original version of source code.
//  10/14/2019 - J. Ritchie Carroll
//       Simplified calling model to Run, TryRun, RunAsync, and TryRunAsync.
//
//******************************************************************************************************

using System;
using System.Threading;

namespace gemstone.threading.synchronizedoperations
{
    /// <summary>
    /// Factory method for creating synchronized operations.
    /// </summary>
    /// <param name="action">The action to be synchronized by the operation.</param>
    /// <returns>The operation that synchronizes the given action.</returns>
    public delegate ISynchronizedOperation SynchronizedOperationFactory(Action action);

    /// <summary>
    /// Represents an operation that cannot run while it is already in progress.
    /// </summary>
    public interface ISynchronizedOperation
    {
        /// <summary>
        /// Gets flag indicating if the synchronized operation is currently executing its action.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Gets flag indicating if the synchronized operation has an additional operation that is pending
        /// execution after the currently running action has completed.
        /// </summary>
        bool IsPending { get; }

        /// <summary>
        /// Gets or sets <see cref="System.Threading.CancellationToken"/> to use for cancelling actions.
        /// </summary>
        CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Executes the action on current thread or marks the operation as pending if the operation is already running.
        /// </summary>
        /// <param name="runPendingSynchronously">Defines synchronization mode for running any pending operation.</param>
        /// <remarks>
        /// <para>
        /// When the operation is marked as pending, operation will run again after currently running operation has
        /// completed. This is useful if an update has invalidated the operation that is currently running and will
        /// therefore need to be run again.
        /// </para>
        /// <para>
        /// When <paramref name="runPendingSynchronously"/> is <c>true</c>, this method will not guarantee that control
        /// will be returned to the thread that called it; if other threads continuously mark the operation as pending,
        /// this thread will continue to run the operation indefinitely on the calling thread.
        /// </para>
        /// </remarks>
        void Run(bool runPendingSynchronously = false);

        /// <summary>
        /// Attempts to execute the action on current thread. Does nothing if the operation is already running.
        /// </summary>
        /// <param name="runPendingSynchronously">Defines synchronization mode for running any pending operation.</param>
        /// <remarks>
        /// When <paramref name="runPendingSynchronously"/> is <c>true</c>, this method will not guarantee that control
        /// will be returned to the thread that called it; if other threads continuously mark the operation as pending,
        /// this thread will continue to run the operation indefinitely on the calling thread.
        /// </remarks>
        void TryRun(bool runPendingSynchronously = false);

        /// <summary>
        /// Executes the action on another thread or marks the operation as pending if the operation is already running.
        /// </summary>
        /// <remarks>
        /// When the operation is marked as pending, it will run again after the operation that is currently running
        /// has completed. This is useful if an update has invalidated the operation that is currently running and
        /// will therefore need to be run again.
        /// </remarks>
        void RunAsync();

        /// <summary>
        /// Attempts to execute the action on another thread. Does nothing if the operation is already running.
        /// </summary>
        void TryRunAsync();
    }
}
