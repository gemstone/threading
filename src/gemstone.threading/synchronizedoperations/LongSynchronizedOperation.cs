//******************************************************************************************************
//  LongSynchronizedOperation.cs - Gbtc
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
//
//******************************************************************************************************

using System;
using System.Threading;
using System.Threading.Tasks;

namespace gemstone.threading.synchronizedoperations
{
    /// <summary>
    /// Represents a long-running synchronized operation
    /// that cannot run while it is already in progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The action performed by the <see cref="LongSynchronizedOperation"/> is executed on
    /// its own dedicated thread when running the operation in the foreground asynchronously.
    /// When running on its own thread, the action is executed in a tight loop until all
    /// pending operations have been completed. This type of synchronized operation should
    /// be preferred if operations may take a long time, block the thread, or put it to sleep.
    /// It is also recommended to prefer this type of operation if the speed of the operation
    /// is not critical or if completion of the operation is critical, such as when saving data
    /// to a file.
    /// </para>
    /// <para>
    /// If the <see cref="IsBackground"/> property is changed while the synchronized operation
    /// is running, behavior is undefined.
    /// </para>
    /// </remarks>
    public class LongSynchronizedOperation : SynchronizedOperationBase
    {
        #region [ Constructors ]

        /// <summary>
        /// Creates a new instance of the <see cref="LongSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="action">The action to be performed during this operation.</param>
        public LongSynchronizedOperation(Action action)
            : base(action)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="LongSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="action">The cancellable action to be performed during this operation.</param>
        /// <remarks>
        /// Cancellable synchronized operation is useful in cases where actions should be terminated
        /// during dispose and/or shutdown operations.
        /// </remarks>
        public LongSynchronizedOperation(Action<CancellationToken> action)
            : base(action)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="LongSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="action">The action to be performed during this operation.</param>
        /// <param name="exceptionAction">The action to be performed if an exception is thrown from the action.</param>
        public LongSynchronizedOperation(Action action, Action<Exception> exceptionAction)
            : base(action, exceptionAction)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="LongSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="action">The action to be performed during this operation.</param>
        /// <param name="exceptionAction">The cancellable action to be performed if an exception is thrown from the action.</param>
        /// <remarks>
        /// Cancellable synchronized operation is useful in cases where actions should be terminated
        /// during dispose and/or shutdown operations.
        /// </remarks>
        public LongSynchronizedOperation(Action<CancellationToken> action, Action<Exception> exceptionAction)
            : base(action, exceptionAction)
        {
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets whether or not the thread executing the action is a background thread.
        /// </summary>
        /// <remarks>
        /// This defaults to <c>false</c>, be aware that foreground thread will prevent shutdown
        /// while task is running. If a task keeps getting marked as pending, application will not
        /// shutdown; consider a cancellable action for <see cref="LongSynchronizedOperation"/>
        /// instances that use a foreground thread.
        /// </remarks>
        public bool IsBackground { get; set; }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Executes the action on a separate thread.
        /// </summary>
        protected override void ExecuteActionAsync()
        {
            if (IsBackground)
                ExecuteActionAsyncBackground();
            else
                ExecuteActionAsyncForeground();
        }

        private void ExecuteActionAsyncBackground()
        {
            void taskAction()
            {
                if (ExecuteAction())
                    ExecuteActionAsync();
            }

            Task.Factory.StartNew(taskAction, TaskCreationOptions.LongRunning);
        }

        private void ExecuteActionAsyncForeground()
        {
            void threadAction()
            {
                while (ExecuteAction())
                {
                }
            }

            new Thread(threadAction).Start();
        }

        #endregion

        #region [ Static ]

        // Static Methods

        /// <summary>
        /// Factory method to match the <see cref="SynchronizedOperationFactory"/> signature.
        /// </summary>
        /// <param name="action">The action to be performed by the <see cref="LongSynchronizedOperation"/>.</param>
        /// <returns>A new instance of <see cref="LongSynchronizedOperation"/>.</returns>
        public static ISynchronizedOperation Factory(Action action) =>
            new LongSynchronizedOperation(action);

        #endregion
    }
}
