//******************************************************************************************************
//  SynchronizedOperationBase.cs - Gbtc
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
    /// Base class for operations that cannot run while they is already in progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class handles the synchronization between the methods defined in the <see cref="ISynchronizedOperation"/>
    /// interface. Implementers should only need to implement the <see cref="ExecuteActionAsync"/> method to provide a
    /// mechanism for executing the action on a separate thread.
    /// </para>
    /// <para>
    /// If subclass implementations get constructed without an exception handler, applications should attach to the
    /// static <see cref="UnhandledException"/> event so that any unhandled exceptions can be exposed to a log.
    /// </para>
    /// </remarks>
    public abstract class SynchronizedOperationBase : ISynchronizedOperation
    {
        #region [ Members ]

        // Constants
        private const int NotRunning = 0;
        private const int Running = 1;
        private const int Pending = 2;

        // Fields
        private readonly Action<CancellationToken> m_action;
        private readonly Action<Exception> m_exceptionAction;
        private int m_state;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new instance of the <see cref="SynchronizedOperationBase"/> class.
        /// </summary>
        /// <param name="action">The action to be performed during this operation.</param>
        protected SynchronizedOperationBase(Action action)
            : this(action, null)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="SynchronizedOperationBase"/> class.
        /// </summary>
        /// <param name="action">The cancellable action to be performed during this operation.</param>
        /// <remarks>
        /// Cancellable synchronized operation is useful in cases where actions should be terminated
        /// during dispose and/or shutdown operations.
        /// </remarks>
        protected SynchronizedOperationBase(Action<CancellationToken> action)
            : this(action, null)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="SynchronizedOperationBase"/> class.
        /// </summary>
        /// <param name="action">The action to be performed during this operation.</param>
        /// <param name="exceptionAction">The action to be performed if an exception is thrown from the action.</param>
        protected SynchronizedOperationBase(Action action, Action<Exception> exceptionAction)
            : this(_ => action(), exceptionAction)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="SynchronizedOperationBase"/> class.
        /// </summary>
        /// <param name="action">The cancellable action to be performed during this operation.</param>
        /// <param name="exceptionAction">The action to be performed if an exception is thrown from the action.</param>
        /// <remarks>
        /// Cancellable synchronized operation is useful in cases where actions should be terminated
        /// during dispose and/or shutdown operations.
        /// </remarks>
        protected SynchronizedOperationBase(Action<CancellationToken> action, Action<Exception> exceptionAction)
        {
            if ((object)action == null)
                throw new ArgumentNullException(nameof(action));

            m_action = action;
            m_exceptionAction = exceptionAction;
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets a value to indicate whether the synchronized operation is currently executing its action.
        /// </summary>
        public bool IsRunning => Interlocked.CompareExchange(ref m_state, NotRunning, NotRunning) != NotRunning;

        /// <summary>
        /// Gets a value to indicate whether the synchronized operation has an additional operation that is pending
        /// execution after the currently running action has completed.
        /// </summary>
        public bool IsPending => Interlocked.CompareExchange(ref m_state, NotRunning, NotRunning) == Pending;

        /// <summary>
        /// Gets or sets <see cref="System.Threading.CancellationToken"/> to use for cancelling actions.
        /// </summary>
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Executes the action on current thread or marks the operation as pending if the operation is already running.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When the operation is marked as pending, it will run again after the operation that is currently running
        /// has completed. This is useful if an update has invalidated the operation that is currently running and
        /// will therefore need to be run again.
        /// </para>
        /// <para>
        /// When <paramref name="runPendingAsync"/> is <c>false</c>, this method will not guarantee that control will
        /// be returned to the thread that called it; if other threads continuously mark the operation as pending,
        /// this thread will continue to run the operation indefinitely on the calling thread.
        /// </para>
        /// </remarks>
        public void Run(bool runPendingAsync = true)
        {
            // if (m_state == NotRunning)
            //     TryRun(runPendingAsync);
            // else if (m_state == Running)
            //     m_state = Pending;

            if (Interlocked.CompareExchange(ref m_state, Pending, Running) == NotRunning)
                TryRun(runPendingAsync);
        }

        /// <summary>
        /// Attempts to execute the action on current thread. Does nothing if the operation is already running.
        /// </summary>
        /// <remarks>
        /// When <paramref name="runPendingAsync"/> is <c>false</c>, this method will not guarantee that control will
        /// be returned to the thread that called it; if other threads continuously mark the operation as pending,
        /// this thread will continue to run the operation indefinitely on the calling thread.
        /// </remarks>
        public void TryRun(bool runPendingAsync = true)
        {
            // if (m_state == NotRunning)
            // {
            //     m_state = Running;
            //
            //     if (runPendingAsync)
            //     {
            //         if (ExecuteAction())
            //             ExecuteActionAsync();
            //     }
            //     else
            //     {
            //         while (ExecuteAction())
            //         {
            //         }
            //     }
            // }

            if (Interlocked.CompareExchange(ref m_state, Running, NotRunning) == NotRunning)
            {
                if (runPendingAsync)
                {
                    if (ExecuteAction())
                        ExecuteActionAsync();
                }
                else
                {
                    while (ExecuteAction())
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Executes the action on another thread or marks the operation as pending if the operation is already running.
        /// </summary>
        /// <remarks>
        /// When the operation is marked as pending, it will run again after the operation that is currently running
        /// has completed. This is useful if an update has invalidated the operation that is currently running and
        /// will therefore need to be run again.
        /// </remarks>
        public void RunAsync()
        {
            // if (m_state == NotRunning)
            //     TryRunOnceAsync();
            // else if (m_state == Running)
            //     m_state = Pending;

            if (Interlocked.CompareExchange(ref m_state, Pending, Running) == NotRunning)
                TryRunAsync();
        }

        /// <summary>
        /// Attempts to execute the action on another thread. Does nothing if the operation is already running.
        /// </summary>
        public void TryRunAsync()
        {
            // if (m_state == NotRunning)
            // {
            //     m_state = Running;
            //     ExecuteActionAsync();
            // }

            if (Interlocked.CompareExchange(ref m_state, Running, NotRunning) == NotRunning)
                ExecuteActionAsync();
        }

        /// <summary>
        /// Executes the action once on the current thread.
        /// </summary>
        /// <returns><c>true</c> if the action was pending and needs to run again; otherwise, <c>false</c>.</returns>
        protected bool ExecuteAction()
        {
            try
            {
                if (!CancellationToken.IsCancellationRequested)
                    m_action(CancellationToken);
            }
            catch (Exception ex)
            {
                ProcessException(ex);
            }

            // if (m_state == Pending)
            // {
            //     m_state = Running;
            //     return true;
            // }
            // else if (m_state == Running)
            // {
            //     m_state = NotRunning;
            // }

            if (Interlocked.CompareExchange(ref m_state, NotRunning, Running) == Pending)
            {
                // There is no race condition here because if m_state is Pending,
                // then it cannot be changed by any other line of code except this one
                Interlocked.Exchange(ref m_state, Running);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Executes the action on a separate thread.
        /// </summary>
        /// <remarks>
        /// Implementers should call <see cref="ExecuteAction"/> on a separate thread and check the return value.
        /// If it returns true, that means it needs to run again. The following is a sample implementation using
        /// a regular dedicated thread.
        /// 
        /// <code>
        /// protected override void ExecuteActionAsync()
        /// {
        ///     Thread actionThread = new Thread(() =>
        ///     {
        ///         while (ExecuteAction())
        ///         {
        ///         }
        ///     });
        ///
        ///     actionThread.Start();
        /// }
        /// </code>
        /// </remarks>
        protected abstract void ExecuteActionAsync();

        /// <summary>
        /// Processes an exception thrown by an operation.
        /// </summary>
        /// <param name="ex"><see cref="Exception"/> to be processed.</param>
        protected void ProcessException(Exception ex)
        {
            if (m_exceptionAction == null)
            {
                OnUnhandledException(this, ex);
            }
            else
            {
                try
                {
                    m_exceptionAction(ex);
                }
                catch (Exception handlerEx)
                {
                    OnUnhandledException(this, new AggregateException(handlerEx, ex));
                }
            }
        }

        #endregion

        #region [ Static ]

        // Static Events

        /// <summary>
        /// Exposes exceptions that were suppressed but otherwise unhandled by a synchronized operation.
        /// </summary>
        /// <remarks>
        /// End users should attach to this event so that unhandled exceptions can be exposed to a log.
        /// </remarks>
        public static event EventHandler<UnhandledExceptionEventArgs> UnhandledException;

        // Static Methods

        private static void OnUnhandledException(object sender, Exception ex) =>
            UnhandledException?.Invoke(sender, new UnhandledExceptionEventArgs(ex));

        #endregion
    }
}
