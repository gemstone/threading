//******************************************************************************************************
//  TaskSynchronizedOperation.cs - Gbtc
//
//  Copyright © 2021, Grid Protection Alliance.  All Rights Reserved.
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
//  02/26/2021 - Stephen C. Wills
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Gemstone.Threading.SynchronizedOperations
{
    /// <summary>
    /// Represents a task-based synchronized operation
    /// that cannot run while it is already in progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The action performed by the <see cref="TaskSynchronizedOperation"/> is executed using
    /// <see cref="Task.Run(System.Func{System.Threading.Tasks.Task}())"/>. Pending actions run when the task returned by the
    /// asynchronous action is completed. This synchronized operation only supports the async
    /// methods on the <see cref="ISynchronizedOperation"/> interface because the async action
    /// cannot be executed synchronously.
    /// </para>
    /// 
    /// <para>
    /// The following example shows how to use <see cref="TaskSynchronizedOperation"/> to
    /// implement a notifier that receives notification requests ad-hoc but sends notifications
    /// no more than once every 15 seconds.
    /// </para>
    /// 
    /// <code>
    /// public ExampleClass() =>
    ///     SynchronizedOperation = new TaskSynchronizedOperation(NotifyAsync);
    ///     
    /// public void SendNotification() =>
    ///     SynchronizedOperation.RunOnceAsync();
    ///     
    /// private async Task NotifyAsync()
    /// {
    ///     Notify();
    ///     await Task.Delay(15000);
    /// }
    /// </code>
    /// </remarks>
    public class TaskSynchronizedOperation : ISynchronizedOperation
    {
        #region [ Members ]

        // Nested Types
        private class SynchronizedOperation : SynchronizedOperationBase
        {
            private Func<CancellationToken, Task> AsyncAction { get; }
            private Action<Exception>? ExceptionHandler { get; }

            public SynchronizedOperation(Func<CancellationToken, Task> asyncAction, Action<Exception>? exceptionHandler)
                : base(() => { })
            {
                if (asyncAction is null)
                    throw new ArgumentNullException(nameof(asyncAction));

                AsyncAction = asyncAction;
                ExceptionHandler = exceptionHandler;
            }

            protected override void ExecuteActionAsync()
            {
                _ = Task.Run(async () =>
                {
                    try { await AsyncAction(CancellationToken); }
                    catch (Exception ex) { TryHandleException(ex); }

                    if (ExecuteAction())
                        ExecuteActionAsync();
                });
            }

            private void TryHandleException(Exception ex)
            {
                void SuppressException(Exception suppressedException)
                {
                    string message = $"Synchronized operation exception handler exception: {suppressedException.Message}";
                    AggregateException aggregateException = new(message, suppressedException, ex);
                    LibraryEvents.OnSuppressedException(this, aggregateException);
                }

                try { ExceptionHandler?.Invoke(ex); }
                catch (Exception handlerException) { SuppressException(handlerException); }
            }
        }

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new instance of the <see cref="TaskSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="asyncAction">The action to be performed during this operation.</param>
        public TaskSynchronizedOperation(Func<Task> asyncAction)
            : this(asyncAction, null)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="TaskSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="asyncAction">The action to be performed during this operation.</param>
        public TaskSynchronizedOperation(Func<CancellationToken, Task> asyncAction)
            : this(asyncAction, null)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="TaskSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="asyncAction">The action to be performed during this operation.</param>
        /// <param name="exceptionAction">The action to be performed if an exception is thrown from the action.</param>
        public TaskSynchronizedOperation(Func<Task> asyncAction, Action<Exception>? exceptionAction)
            : this(_ => asyncAction(), exceptionAction)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="TaskSynchronizedOperation"/> class.
        /// </summary>
        /// <param name="asyncAction">The action to be performed during this operation.</param>
        /// <param name="exceptionAction">The action to be performed if an exception is thrown from the action.</param>
        public TaskSynchronizedOperation(Func<CancellationToken, Task> asyncAction, Action<Exception>? exceptionAction) =>
            InternalSynchronizedOperation = new SynchronizedOperation(asyncAction, exceptionAction);

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets a value to indicate whether the synchronized
        /// operation is currently executing its action.
        /// </summary>
        public bool IsRunning => InternalSynchronizedOperation.IsRunning;

        /// <summary>
        /// Gets a value to indiate whether the synchronized operation
        /// has an additional operation that is pending execution after
        /// the currently running action has completed.
        /// </summary>
        public bool IsPending => InternalSynchronizedOperation.IsPending;

        /// <summary>
        /// Gets or sets <see cref="System.Threading.CancellationToken"/> to use for canceling actions.
        /// </summary>
        public CancellationToken CancellationToken
        {
            get => InternalSynchronizedOperation.CancellationToken;
            set => InternalSynchronizedOperation.CancellationToken = value;
        }

        private SynchronizedOperation InternalSynchronizedOperation { get; }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Executes the action on another thread or marks the
        /// operation as pending if the operation is already running.
        /// </summary>
        /// <remarks>
        /// When the operation is marked as pending, it will run again after the
        /// operation that is currently running has completed. This is useful if
        /// an update has invalidated the operation that is currently running and
        /// will therefore need to be run again.
        /// </remarks>
        public void RunAsync() =>
            InternalSynchronizedOperation.RunAsync();

        /// <summary>
        /// Attempts to execute the action on another thread.
        /// Does nothing if the operation is already running.
        /// </summary>
        public void TryRunAsync() =>
            InternalSynchronizedOperation.TryRunAsync();

        void ISynchronizedOperation.Run(bool runPendingSynchronously) => RunAsync();
        void ISynchronizedOperation.TryRun(bool runPendingSynchronously) => TryRunAsync();

        #endregion
    }
}
