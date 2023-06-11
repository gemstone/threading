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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gemstone.Threading.Collections;
using Gemstone.Threading.SynchronizedOperations;

namespace Gemstone.Threading.Strands;

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

        public new bool TryExecuteTask(Task task) => base.TryExecuteTask(task);

        protected override void QueueTask(Task task) => PriorityStrand.QueueTask(task, this);

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => PriorityStrand.TryExecuteTaskInline(task, this, taskWasPreviouslyQueued);

        protected override bool TryDequeue(Task task) => PriorityStrand.TryDequeue(task, this);

        protected override IEnumerable<Task> GetScheduledTasks() => PriorityStrand.GetScheduledTasks(this);
    }

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new instance of the <see cref="PriorityStrand"/> class with a <see cref="ShortSynchronizedOperation"/>.
    /// </summary>
    public PriorityStrand() : this(ShortSynchronizedOperation.Factory)
    {
    }

    /// <summary>
    /// Creates a new instance of the <see cref="PriorityStrand"/> class with a <see cref="ShortSynchronizedOperation"/>.
    /// </summary>
    /// <param name="priorityLevels">The number of priority levels to be preallocated by the priority queue.</param>
    public PriorityStrand(int priorityLevels) : this(ShortSynchronizedOperation.Factory, priorityLevels)
    {
    }

    /// <summary>
    /// Creates a new instance of the <see cref="PriorityStrand"/> class.
    /// </summary>
    /// <param name="synchronizedOperationFactory">Factory function for creating the synchronized operation to be used for processing tasks.</param>
    public PriorityStrand(SynchronizedOperationFactory synchronizedOperationFactory) : this(synchronizedOperationFactory, 1)
    {
    }

    /// <summary>
    /// Creates a new instance of the <see cref="PriorityStrand"/> class.
    /// </summary>
    /// <param name="synchronizedOperationFactory">Factory function for creating the synchronized operation to be used for processing tasks.</param>
    /// <param name="priorityLevels">The number of priority levels to be preallocated by the priority queue.</param>
    public PriorityStrand(SynchronizedOperationFactory synchronizedOperationFactory, int priorityLevels)
    {
        SynchronizedOperation = synchronizedOperationFactory(Execute);
        Queue = new PriorityQueue<QueuedTask>(priorityLevels);
    }

    #endregion

    #region [ Properties ]

    private ISynchronizedOperation SynchronizedOperation { get; }

    private PriorityQueue<QueuedTask> Queue { get; }

    private Thread? ProcessingThread { get; set; }

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

        return new Scheduler(this, priority);
    }

    private void QueueTask(Task task, Scheduler scheduler)
    {
        int priority = scheduler.Priority;
        QueuedTask queuedTask = new(task, scheduler);
        Queue.Enqueue(queuedTask, priority);
        SynchronizedOperation.RunAsync();
    }

    // Inline execution allows tasks to skip the line and run out of order.
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

        // We can only dequeue tasks from the head of the queue at a given
        // priority, so this is really just a minimal-effort approach
        if (Queue.TryPeek(priority, out QueuedTask? head) && task == head!.Task)
            return Queue.TryDequeue(priority, out _);

        return false;
    }

    private IEnumerable<Task> GetScheduledTasks(Scheduler scheduler)
    {
        return Queue
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
                if (!Queue.TryDequeue(out QueuedTask? queuedTask))
                    break;

                Task task = queuedTask!.Task;
                Scheduler scheduler = queuedTask.Scheduler;

                if (scheduler.TryExecuteTask(task))
                    break;
            }
        }
        finally
        {
            ProcessingThread = null;

            if (!Queue.IsEmpty)
                SynchronizedOperation.RunAsync();
        }
    }

    #endregion
}
