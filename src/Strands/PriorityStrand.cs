//******************************************************************************************************
//  PriorityStrand.cs - Gbtc
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
//  10/04/2019 - Stephen C. Wills
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using gemstone.threading.synchronizedoperations;

namespace gemstone.threading.strands
{
    /// <summary>
    /// Schedules tasks in a collection of FIFO queues and executes them in priority order.
    /// </summary>
    public class PriorityStrand
    {
        #region [ Members ]

        // This class maintains the association between
        // a task and the scheduler that queued it.
        private class QueuedTask
        {
            public QueuedTask(Task task, Scheduler scheduler)
            {
                Task = task;
                Scheduler = scheduler;
            }

            public Task Task { get; }
            public Scheduler Scheduler { get; }
        }

        // The TaskScheduler interface doesn't provide a mechanism
        // for queuing individual tasks at a specific priority,
        // so this class provides a TaskScheduler associated with a specific
        // priority that simply feeds tasks into the PriorityStrand's queue.
        private class Scheduler : TaskScheduler
        {
            public Scheduler(PriorityStrand priorityStrand, int priority)
            {
                PriorityStrand = priorityStrand;
                Priority = priority;
            }

            public int Priority { get; }
            public override int MaximumConcurrencyLevel => 1;
            private PriorityStrand PriorityStrand { get; }

            public new bool TryExecuteTask(Task task) =>
                base.TryExecuteTask(task);

            protected override void QueueTask(Task task) =>
                PriorityStrand.QueueTask(task, this);

            protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) =>
                PriorityStrand.TryExecuteTaskInline(task, this, taskWasPreviouslyQueued);

            protected override bool TryDequeue(Task task) =>
                PriorityStrand.TryDequeue(task, this);

            protected override IEnumerable<Task> GetScheduledTasks() =>
                PriorityStrand.GetScheduledTasks(this);
        }

        // These days I implement all internal fields as properties,
        // but this needed to be Interlocked for resizing.
        private ConcurrentQueue<QueuedTask>[] m_queues;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new instance of the <see cref="PriorityStrand"/> class with a <see cref="ShortSynchronizedOperation"/>.
        /// </summary>
        public PriorityStrand()
            : this(ShortSynchronizedOperation.Factory)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="PriorityStrand"/> class.
        /// </summary>
        /// <param name="synchronizedOperationFactory">Factory function for creating the synchronized operation to be used for processing tasks.</param>
        public PriorityStrand(SynchronizedOperationFactory synchronizedOperationFactory)
        {
            SynchronizedOperation = synchronizedOperationFactory(Execute);
            m_queues = new ConcurrentQueue<QueuedTask>[1];
            Queues[0] = new ConcurrentQueue<QueuedTask>();
        }

        #endregion

        #region [ Properties ]

        private ISynchronizedOperation SynchronizedOperation { get; }
        private ConcurrentQueue<QueuedTask>[] Queues => Interlocked.CompareExchange(ref m_queues, null, null);
        private Thread ProcessingThread { get; set; }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Gets a <see cref="TaskScheduler"/> used to queue tasks at a specific priority.
        /// </summary>
        /// <param name="priority">The priority at which tasks should be queued by the returned <see cref="TaskScheduler"/>. Higher numbers are higher in priority!</param>
        /// <returns>A <see cref="TaskScheduler"/> that queues tasks into the strand at the given priority.</returns>
        /// <exception cref="ArgumentException"><paramref name="priority"/> is less than zero</exception>
        /// <remarks>For a strand with <c>n</c> priorities, it is recommended to use priority levels between <c>0</c> and <c>n-1</c> inclusive.</remarks>
        public TaskScheduler GetScheduler(int priority)
        {
            if (priority < 0)
                throw new ArgumentException("Priority must be a nonnegative integer.", nameof(priority));

            while (Queues.Length <= priority)
            {
                ConcurrentQueue<QueuedTask>[] queues = Queues;
                ConcurrentQueue<QueuedTask>[] resizedQueues = queues;
                Array.Resize(ref resizedQueues, priority + 1);

                for (int i = queues.Length; i < resizedQueues.Length; i++)
                    queues[i] = new ConcurrentQueue<QueuedTask>();

                // This prevents race conditions between threads
                // attempting to resize the array in parallel
                Interlocked.CompareExchange(ref m_queues, resizedQueues, queues);
            }

            return new Scheduler(this, priority);
        }

        private void QueueTask(Task task, Scheduler scheduler)
        {
            int priority = scheduler.Priority;
            ConcurrentQueue<QueuedTask> queue = Queues[priority];
            QueuedTask queuedTask = new QueuedTask(task, scheduler);
            queue.Enqueue(queuedTask);
            SynchronizedOperation.RunOnceAsync();
        }

        // Inline exeuction allows tasks to skip the line and run out of order.
        // The only reason inline execution is supported at all is to avoid a common
        // case of deadlocking where a task is queued in advance of another task that it
        // depends on (via Task.Wait(), for instance). However, deadlocks can still occur
        // when waiting on tasks scheduled by a different strand. To avoid out-of-order
        // execution and deadlocks, be very careful about using API calls that wait on tasks.
        private bool TryExecuteTaskInline(Task task, Scheduler scheduler, bool taskWasPreviouslyQueued)
        {
            if (ProcessingThread != Thread.CurrentThread)
                return false;

            // We don't need to dequeue the task in order
            // to execute it, but we may as well try it
            if (taskWasPreviouslyQueued)
                TryDequeue(task, scheduler);

            return scheduler.TryExecuteTask(task);
        }

        private bool TryDequeue(Task task, Scheduler scheduler)
        {
            // On any other thread other than the processing thread,
            // there would be a race condition between TryPeek and TryDequeue
            if (ProcessingThread != Thread.CurrentThread)
                return false;

            int priority = scheduler.Priority;
            ConcurrentQueue<QueuedTask> queue = Queues[priority];

            // We can only dequeue tasks from the head of the queue,
            // so this is really just a minimal-effort approach
            if (queue.TryPeek(out QueuedTask head) && task == head.Task)
                return queue.TryDequeue(out _);

            return false;
        }

        private IEnumerable<Task> GetScheduledTasks(TaskScheduler scheduler)
        {
            return Queues
                .SelectMany(queue => queue.ToArray())
                .Where(queuedTask => queuedTask.Scheduler == scheduler)
                .Select(queuedTask => queuedTask.Task);
        }

        // This method is called by the synchronized operation to
        // ensure that items are never processed in parallel.
        private void Execute()
        {
            try
            {
                ProcessingThread = Thread.CurrentThread;

                while (true)
                {
                    // Higher numbers are higher in priority!
                    ConcurrentQueue<QueuedTask> queue = Queues.LastOrDefault(q => !q.IsEmpty);

                    if (queue == null)
                        return;

                    // Dequeue should never fail since we just determined
                    // that the queue is not empty; but if it did fail,
                    // this seems like reasonable behavior
                    if (!queue.TryDequeue(out QueuedTask queuedTask))
                        continue;

                    Task task = queuedTask.Task;
                    Scheduler scheduler = queuedTask.Scheduler;

                    if (scheduler.TryExecuteTask(task))
                        break;
                }
            }
            finally
            {
                ProcessingThread = null;

                if (Queues.Any(queue => !queue.IsEmpty))
                    SynchronizedOperation.RunOnceAsync();
            }
        }

        #endregion
    }
}
