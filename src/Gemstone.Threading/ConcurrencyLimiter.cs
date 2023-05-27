//******************************************************************************************************
//  ConcurrencyLimiter.cs - Gbtc
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
//  10/10/2019 - Stephen C. Wills
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gemstone.Threading.SynchronizedOperations;

namespace Gemstone.Threading
{
    /// <summary>
    /// Task scheduler that limits the number of tasks that can execute in parallel at any given time.
    /// </summary>
    public class ConcurrencyLimiter : TaskScheduler
    {
        #region [ Members ]

        // Fields
        private int m_currentConcurrencyLevel;
        private int m_maximumConcurrencyLevel;

        [ThreadStatic]
        private static bool s_currentlyExecutingTask;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new instance of the <see cref="ConcurrencyLimiter"/> class
        /// with a <see cref="ShortSynchronizedOperation"/> and a maximum concurrency
        /// level equal to the number of processors on the current machine.
        /// </summary>
        public ConcurrencyLimiter() : this(ShortSynchronizedOperation.Factory, Environment.ProcessorCount)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ConcurrencyLimiter"/> class with a
        /// maximum concurrency level equal to the number of processors on the current machine.
        /// </summary>
        /// <param name="synchronizedOperationFactory">Factory function for creating the synchronized operations to be used for processing tasks.</param>
        public ConcurrencyLimiter(SynchronizedOperationFactory synchronizedOperationFactory) : this(synchronizedOperationFactory, Environment.ProcessorCount)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ConcurrencyLimiter"/> class
        /// with a <see cref="ShortSynchronizedOperation"/>.
        /// </summary>
        /// <param name="maximumConcurrencyLevel">The initial value for <see cref="MaximumConcurrencyLevel"/>.</param>
        public ConcurrencyLimiter(int maximumConcurrencyLevel) : this(ShortSynchronizedOperation.Factory, maximumConcurrencyLevel)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="ConcurrencyLimiter"/> class.
        /// </summary>
        /// <param name="synchronizedOperationFactory">Factory function for creating the synchronized operations to be used for processing tasks.</param>
        /// <param name="maximumConcurrencyLevel">The initial value for <see cref="MaximumConcurrencyLevel"/>.</param>
        public ConcurrencyLimiter(SynchronizedOperationFactory synchronizedOperationFactory, int maximumConcurrencyLevel)
        {
            SynchronizedOperationFactory = synchronizedOperationFactory;
            TaskProcessors = new ConcurrentBag<ISynchronizedOperation>();
            Queue = new ConcurrentQueue<Task>();
            SetMaximumConcurrencyLevel(maximumConcurrencyLevel);
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets the number of threads that are currently executing tasks concurrently.
        /// </summary>
        public int CurrentConcurrencyLevel => Interlocked.CompareExchange(ref m_currentConcurrencyLevel, 0, 0);

        /// <summary>
        /// Gets the maximum number of threads that can be executing tasks concurrently.
        /// </summary>
        public override int MaximumConcurrencyLevel => Interlocked.CompareExchange(ref m_maximumConcurrencyLevel, 0, 0);

        private SynchronizedOperationFactory SynchronizedOperationFactory { get; }

        private ConcurrentBag<ISynchronizedOperation> TaskProcessors { get; }

        private ConcurrentQueue<Task> Queue { get; }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Sets the maximum number of threads that can be executing tasks concurrently.
        /// </summary>
        /// <param name="maximumConcurrencyLevel">The maximum concurrency level.</param>
        public void SetMaximumConcurrencyLevel(int maximumConcurrencyLevel)
        {
            if (maximumConcurrencyLevel <= 0)
                throw new ArgumentException("Maximum concurrency level must be greater than zero.", nameof(maximumConcurrencyLevel));

            int oldMaximumConcurrencyLevel = Interlocked.Exchange(ref m_maximumConcurrencyLevel, maximumConcurrencyLevel);

            if (maximumConcurrencyLevel > oldMaximumConcurrencyLevel)
                CreateNewTaskProcessors(maximumConcurrencyLevel - oldMaximumConcurrencyLevel);

            // The complexity required to remove task processors from
            // the pool at this point exceeds the value of doing so;
            // it is both adequate and much simpler to let the pool reduce
            // size automatically via TryActivateCurrentTaskProcessor()
            // and TryDeactivate() methods
        }

        /// <summary>
        /// Queues a <see cref="Task"/> to the scheduler.
        /// </summary>
        /// <param name="task">The <see cref="Task"/> to be queued.</param>
        protected override void QueueTask(Task task)
        {
            Queue.Enqueue(task);

            if (TaskProcessors.TryTake(out ISynchronizedOperation? taskProcessor) && TryActivateCurrentTaskProcessor())
                taskProcessor.RunAsync();
        }

        /// <summary>
        /// Determines whether the provided <see cref="Task"/> can be executed synchronously
        /// in this call, and if it can, executes it.
        /// </summary>
        /// <param name="task">The <see cref="Task"/> to be executed.</param>
        /// <param name="taskWasPreviouslyQueued">
        /// A <see cref="bool"/> denoting whether or not task has previously been queued.
        /// If this parameter is True, then the task may have been previously queued (scheduled);
        /// if False, then the task is known not to have been queued,
        /// and this call is being made in order to execute the task inline without queuing it.
        /// </param>
        /// <returns>A <see cref="bool"/> value indicating whether the task was executed inline.</returns>
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (s_currentlyExecutingTask)
                return TryExecuteTask(task);

            if (!TaskProcessors.TryTake(out ISynchronizedOperation? taskProcessor))
                return false;

            if (!TryActivateCurrentTaskProcessor())
                return false;

            bool result = TryExecuteTask(task);

            if (TryDeactivate(taskProcessor))
                return result;

            taskProcessor.RunAsync();

            return result;
        }

        /// <summary>
        /// For debugger support only, generates an enumerable of <see cref="Task"/>
        /// instances currently queued to the scheduler waiting to be executed.
        /// </summary>
        /// <returns>An enumerable that allows a debugger to traverse the tasks currently queued to this scheduler.</returns>
        protected override IEnumerable<Task> GetScheduledTasks() => Queue.ToArray();

        private void ProcessTask(ISynchronizedOperation taskProcessor)
        {
            bool tryExecuteTask(Task task)
            {
                s_currentlyExecutingTask = true;
                bool result = TryExecuteTask(task);
                s_currentlyExecutingTask = false;

                return result;
            }

            // This loop ensures that we execute exactly
            // one task unless there are no tasks to execute
            while (true)
            {
                if (!Queue.TryDequeue(out Task? task))
                    break;

                if (tryExecuteTask(task))
                    break;
            }

            if (TryDeactivate(taskProcessor))
                return;

            taskProcessor.RunAsync();
        }

        // Instantiates new task processors and puts them in the pool.
        private void CreateNewTaskProcessors(int count)
        {
            for (int i = 0; i < count; i++)
            {
                ISynchronizedOperation taskProcessor = default!;

                // ReSharper disable once AccessToModifiedClosure
                taskProcessor = SynchronizedOperationFactory(() => ProcessTask(taskProcessor));

                TaskProcessors.Add(taskProcessor);
            }

            // Since we just increased the number of task processors,
            // we may also need to increase the current concurrency
            // level to process queued tasks more efficiently
            for (int i = 0; i < Queue.Count; i++)
            {
                if (!TaskProcessors.TryTake(out ISynchronizedOperation? taskProcessor))
                    return;

                if (!TryActivateCurrentTaskProcessor())
                    return;

                taskProcessor.RunAsync();
            }
        }

        // Returns true if the current task processor was successfully activated
        // or false if the current task processor must be retired.
        private bool TryActivateCurrentTaskProcessor()
        {
            while (true)
            {
                // The current thread has a reference to a task processor and therefore
                // can nearly always safely increment the counter and return true;
                // the only exception is when the MaximumConcurrencyLevel changes
                if (Interlocked.Increment(ref m_currentConcurrencyLevel) < MaximumConcurrencyLevel)
                    return true;

                if (Interlocked.Decrement(ref m_currentConcurrencyLevel) > MaximumConcurrencyLevel)
                    return false;
            }
        }

        // Returns false if the task processor has more work to do; otherwise returns true.
        private bool TryDeactivate(ISynchronizedOperation taskProcessor)
        {
            // Returns true only if the task processor needs to be retired and false otherwise.
            bool mustRetireTaskProcessor()
            {
                // The current thread has a reference to a task processor and would
                // nearly always waste time attempting to decrement the counter;
                // the only exception is when the MaximumConcurrencyLevel changes
                if (CurrentConcurrencyLevel <= MaximumConcurrencyLevel)
                    return false;

                while (true)
                {
                    if (Interlocked.Decrement(ref m_currentConcurrencyLevel) > MaximumConcurrencyLevel)
                        return true;

                    if (Interlocked.Increment(ref m_currentConcurrencyLevel) < MaximumConcurrencyLevel)
                        return false;
                }
            }

            if (mustRetireTaskProcessor())
                return true;

            if (!Queue.IsEmpty)
                return false;

            // It's always safe to decrement the counter here because it was
            // previously incremented when the task processor was activated
            Interlocked.Decrement(ref m_currentConcurrencyLevel);

            // Check again because of race conditions;
            // don't want to leave tasks dangling in the queue
            if (Queue.IsEmpty)
            {
                TaskProcessors.Add(taskProcessor);

                return true;
            }

            return !TryActivateCurrentTaskProcessor();
        }

        #endregion
    }
}
