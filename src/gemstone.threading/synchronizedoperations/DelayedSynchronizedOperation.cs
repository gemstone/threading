//******************************************************************************************************
//  DelayedSynchronizedOperation.cs - Gbtc
//
//  Copyright © 2012, Grid Protection Alliance.  All Rights Reserved.
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
//  02/06/2012 - Stephen C. Wills
//       Generated original version of source code.
//
//******************************************************************************************************

using gemstone.threading.extensions;
using System;
using System.ComponentModel;
using System.Threading;

namespace gemstone.threading.synchronizedoperations
{
    /// <summary>
    /// Represents a short-running synchronized operation that cannot run while it is already
    /// in progress. Async operations will execute on the thread-pool after the specified
    /// <see cref="Delay"/> in milliseconds.
    /// </summary>
    /// <remarks>
    /// By default, the action performed by the <see cref="DelayedSynchronizedOperation"/>
    /// is executed on the <see cref="ThreadPool"/> when running the operation asynchronously.
    /// When the operation is set to pending, the action is executed in an asynchronous loop on
    /// the thread pool until all pending operations have been completed. Since the action is
    /// executed on the thread pool, it is best if it can be executed quickly, without
    /// blocking the thread or putting it to sleep. If completion of the operation is
    /// critical, such as when saving data to a file, this type of operation should not
    /// be used since thread pool threads are background threads and will not prevent the
    /// program from ending before the operation is complete.
    /// </remarks>
    public class DelayedSynchronizedOperation : SynchronizedOperationBase, ISynchronizedOperation
    {
        #region [ Members ]

        // Constants

        /// <summary>
        /// Defines the default value for the <see cref="Delay"/> property.
        /// </summary>
        public const int DefaultDelay = 1000;

        // Fields
        private readonly Action<CancellationToken> m_delayedAction;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new instance of the <see cref="DelayedSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="action">The action to be performed during this operation.</param>
        public DelayedSynchronizedOperation(Action action)
            : this(action, null)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="DelayedSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="action">The cancellable action to be performed during this operation.</param>
        /// <remarks>
        /// Cancellable synchronized operation is useful in cases where actions should be terminated
        /// during dispose and/or shutdown operations.
        /// </remarks>
        public DelayedSynchronizedOperation(Action<CancellationToken> action)
            : this(action, null)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="DelayedSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="action">The action to be performed during this operation.</param>
        /// <param name="exceptionAction">The action to be performed if an exception is thrown from the action.</param>
        public DelayedSynchronizedOperation(Action action, Action<Exception> exceptionAction)
            : this(_ => action(), exceptionAction)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="DelayedSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="action">The cancellable action to be performed during this operation.</param>
        /// <param name="exceptionAction">The action to be performed if an exception is thrown from the action.</param>
        /// <remarks>
        /// Cancellable synchronized operation is useful in cases where actions should be terminated
        /// during dispose and/or shutdown operations.
        /// </remarks>
        public DelayedSynchronizedOperation(Action<CancellationToken> action, Action<Exception> exceptionAction)
            : base(action, exceptionAction)
        {
            m_delayedAction = _ =>
            {
                if (ExecuteAction())
                    ExecuteActionAsync();
            };
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets the amount of time to wait before execution, in milliseconds,
        /// for any asynchronous calls. Zero value will execute immediately.
        /// </summary>
        /// <remarks>
        /// Non asynchronous calls will not be delayed.
        /// </remarks>
        public int Delay { get; set; } = DefaultDelay;

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Executes the action on another thread after the specified <see cref="Delay"/> in milliseconds or marks
        /// the operation as pending if the operation is already running. Method same as <see cref="RunAsync"/> for
        /// <see cref="DelayedSynchronizedOperation"/>.
        /// </summary>
        /// <param name="runPendingSynchronously">
        /// Defines synchronization mode for running any pending operation; must be <c>false</c> for
        /// <see cref="DelayedSynchronizedOperation"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// For <see cref="DelayedSynchronizedOperation"/>, actions will always run on another thread so this method is
        /// hidden from intellisense.
        /// </para>
        /// <para>
        /// When the operation is marked as pending, it will run again after the operation that is currently running
        /// has completed. This is useful if an update has invalidated the operation that is currently running and
        /// will therefore need to be run again.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="runPendingSynchronously"/> must be <c>false</c> for <see cref="DelayedSynchronizedOperation"/>.
        /// </exception>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public new void Run(bool runPendingSynchronously = false) // Method shadowed to provide updated documentation and hide from intellisense
        {
            if (runPendingSynchronously)
                throw new InvalidOperationException($"{nameof(runPendingSynchronously)} must be false for {nameof(DelayedSynchronizedOperation)}");

            base.Run();
        }

        void ISynchronizedOperation.Run(bool runPendingSynchronously) => Run(runPendingSynchronously);

        /// <summary>
        /// Attempts to execute the action on another thread after the specified <see cref="Delay"/> in milliseconds.
        /// Does nothing if the operation is already running. Method same as <see cref="TryRunAsync"/> for
        /// <see cref="DelayedSynchronizedOperation"/>.
        /// </summary>
        /// <param name="runPendingSynchronously">
        /// Defines synchronization mode for running any pending operation; must be <c>false</c> for
        /// <see cref="DelayedSynchronizedOperation"/>.
        /// </param>
        /// <remarks>
        /// For <see cref="DelayedSynchronizedOperation"/>, actions will always run on another thread so this method is
        /// hidden from intellisense.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="runPendingSynchronously"/> must be <c>false</c> for <see cref="DelayedSynchronizedOperation"/>.
        /// </exception>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public new void TryRun(bool runPendingSynchronously = false) // Method shadowed to provide updated documentation and hide from intellisense
        {
            if (runPendingSynchronously)
                throw new InvalidOperationException($"{nameof(runPendingSynchronously)} must be false for {nameof(DelayedSynchronizedOperation)}");

            base.TryRun();
        }

        void ISynchronizedOperation.TryRun(bool runPendingSynchronously) => TryRun(runPendingSynchronously);

        /// <summary>
        /// Executes the action on another thread after the specified <see cref="Delay"/> in milliseconds or marks
        /// the operation as pending if the operation is already running
        /// </summary>
        /// <remarks>
        /// When the operation is marked as pending, operation will run again after currently running operation has
        /// completed. This is useful if an update has invalidated the operation that is currently running and will
        /// therefore need to be run again.
        /// </remarks>
        public new void RunAsync() => // Method shadowed to provide updated documentation
            base.RunAsync();

        /// <summary>
        /// Attempts to execute the action on another thread after the specified <see cref="Delay"/> in milliseconds.
        /// Does nothing if the operation is already running.
        /// </summary>
        public new void TryRunAsync() => // Method shadowed to provide updated documentation
            base.TryRunAsync();

        /// <summary>
        /// Executes the action on a separate thread after the specified <see cref="Delay"/>.
        /// </summary>
        protected override void ExecuteActionAsync() =>
            m_delayedAction.DelayAndExecute(Delay, CancellationToken, ProcessException);

        #endregion

        #region [ Static ]

        // Static Methods

        /// <summary>
        /// Factory method to match the <see cref="SynchronizedOperationFactory"/> signature.
        /// </summary>
        /// <param name="action">The action to be performed by the <see cref="DelayedSynchronizedOperation"/>.</param>
        /// <returns>A new instance of <see cref="DelayedSynchronizedOperation"/> with <see cref="DefaultDelay"/> of 1000 milliseconds.</returns>
        public static ISynchronizedOperation Factory(Action action) =>
            new DelayedSynchronizedOperation(action);

        #endregion
    }
}