//******************************************************************************************************
//  Strand.cs - Gbtc
//
//  Copyright © 2019, Grid Protection Alliance.  All Rights Reserved.
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
//  10/03/2019 - Stephen C. Wills
//       Generated original version of source code.
//
//******************************************************************************************************

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using gemstone.threading.synchronizedoperations;

namespace gemstone.threading.strands
{
    /// <summary>
    /// Schedules tasks in a FIFO queue and executes them in a synchronized asynchronous loop.
    /// </summary>
    public class Strand : TaskScheduler
    {
        #region [ Constructors ]

        /// <summary>
        /// Creates a new instance of the <see cref="Strand"/> class with a <see cref="ShortSynchronizedOperation"/>.
        /// </summary>
        public Strand()
            : this(ShortSynchronizedOperation.Factory)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="Strand"/> class.
        /// </summary>
        /// <param name="synchronizedOperationFactory">Factory function for creating the synchronized operation to be used for processing tasks.</param>
        public Strand(SynchronizedOperationFactory synchronizedOperationFactory)
        {
            SynchronizedOperation = synchronizedOperationFactory(Execute);
            Queue = new ConcurrentQueue<Task>();
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Indicates the maximum concurrency level this <see cref="TaskScheduler"/> is able to support.
        /// </summary>
        public override int MaximumConcurrencyLevel => 1;

        private ISynchronizedOperation SynchronizedOperation { get; }
        private ConcurrentQueue<Task> Queue { get; }
        private Thread ProcessingThread { get; set; }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Queues a <see cref="Task"/> to the scheduler.
        /// </summary>
        /// <param name="task">The <see cref="Task"/> to be queued.</param>
        protected override void QueueTask(Task task)
        {
            Queue.Enqueue(task);
            SynchronizedOperation.RunOnceAsync();
        }

        /// <summary>
        /// Attempts to executes a task inline, but only if this method is
        /// called on the processing thread to avoid parallel execution of tasks.
        /// </summary>
        /// <param name="task">The <see cref="Task"/> to be executed.</param>
        /// <param name="taskWasPreviouslyQueued">
        /// A Boolean denoting whether or not task has previously been queued.
        /// If this parameter is True, then the task may have been previously queued (scheduled);
        /// if False, then the task is known not to have been queued,
        /// and this call is being made in order to execute the task inline without queuing it.
        /// </param>
        /// <returns>A Boolean value indicating whether the task was executed inline.</returns>
        /// <remarks>
        /// Inline exeuction allows tasks to skip the line and run out of order.
        /// The only reason inline execution is supported at all is to avoid a common
        /// case of deadlocking where a task is queued in advance of another task that it
        /// depends on (via <see cref="Task.Wait"/>, for instance). However, deadlocks can
        /// still occur when waiting on tasks scheduled by a different strand. To avoid
        /// out-of-order execution and deaclocks, be very careful about using API calls
        /// that wait on tasks.
        /// </remarks>
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (ProcessingThread != Thread.CurrentThread)
                return false;

            // We don't need to dequeue the task in order
            // to execute it, but we may as well try it
            if (taskWasPreviouslyQueued)
                TryDequeue(task);

            return TryExecuteTask(task);
        }

        /// <summary>
        /// Attempts to dequeue a <see cref="Task"/> that was previously queued to this scheduler.
        /// </summary>
        /// <param name="task">The <see cref="Task"/> to be dequeued.</param>
        /// <returns>A Boolean denoting whether the task argument was successfully dequeued.</returns>
        protected override bool TryDequeue(Task task)
        {
            // On any other thread other than the processing thread,
            // there would be a race condition between TryPeek and TryDequeue
            if (ProcessingThread != Thread.CurrentThread)
                return false;

            // We can only dequeue tasks from the head of the queue,
            // so this is really just a minimal-effort approach
            if (Queue.TryPeek(out Task head) && task == head)
                return Queue.TryDequeue(out _);

            return false;
        }

        /// <summary>
        /// For debugger support only, generates an enumerable of <see cref="Task"/>
        /// instances currently queued to the scheduler waiting to be executed.
        /// </summary>
        /// <returns>An enumerable that allows a debugger to traverse the tasks currently queued to this scheduler.</returns>
        protected override IEnumerable<Task> GetScheduledTasks() =>
            Queue.ToArray();

        // This method is called by the synchronized operation to
        // ensure that items are never processed in parallel.
        private void Execute()
        {
            try
            {
                ProcessingThread = Thread.CurrentThread;

                // This while loop ensures that this method does its
                // best to execute a task before exiting, thus reducing
                // unnecessary iterations of the async loop
                while (true)
                {
                    if (!Queue.TryDequeue(out Task task))
                        return;

                    // A task could be cancelled or inlined after it was queued,
                    // causing this to return false without doing anything
                    if (TryExecuteTask(task))
                        break;
                }
            }
            finally
            {
                ProcessingThread = null;

                if (!Queue.IsEmpty)
                    SynchronizedOperation.RunOnceAsync();
            }
        }

        #endregion
    }
}
