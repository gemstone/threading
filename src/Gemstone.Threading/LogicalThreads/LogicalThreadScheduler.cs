//******************************************************************************************************
//  LogicalThreadScheduler.cs - Gbtc
//
//  Copyright © 2015, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  09/23/2015 - Stephen C. Wills
//       Generated original version of source code.
//  11/16/2023 - Lillian Gensolin
//       Converted code to .NET core.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Gemstone.Threading.Cancellation;
using CancellationToken = Gemstone.Threading.Cancellation.CancellationToken;

namespace Gemstone.Threading.LogicalThreads;

/// <summary>
/// Manages synchronization of actions by dispatching actions
/// to logical threads to be processed synchronously.
/// </summary>
public class LogicalThreadScheduler
{
    #region [ Members ]

    // Events

    /// <summary>
    /// Triggered when an action managed by this
    /// synchronization manager throws an exception.
    /// </summary>
    public event EventHandler<EventArgs<Exception>>? UnhandledException;

    // Fields
    private readonly ConcurrentQueue<Func<LogicalThread>?>[] m_logicalThreadQueues;
    private int m_maxThreadCount;
    private int m_threadCount;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new instance of the <see cref="LogicalThreadScheduler"/> class.
    /// </summary>
    /// <param name="priorityLevels">The number of levels of priority supported by this logical thread scheduler.</param>
    /// <exception cref="ArgumentException"><paramref name="priorityLevels"/> is less than or equal to zero.</exception>
    public LogicalThreadScheduler(int priorityLevels = 1)
    {
        if (priorityLevels < 1)
            throw new ArgumentException("A logical thread scheduler must have at least one priority level.", nameof(priorityLevels));

        m_maxThreadCount = Environment.ProcessorCount;
        m_logicalThreadQueues = new ConcurrentQueue<Func<LogicalThread>?>[priorityLevels];
        UseBackgroundThreads = true;

        for (int i = 0; i < priorityLevels; i++)
            m_logicalThreadQueues[i] = new ConcurrentQueue<Func<LogicalThread>?>();
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets or sets the target for the maximum number of physical
    /// threads managed by this synchronization manager at any given time.
    /// </summary>
    public int MaxThreadCount
    {
        get => Interlocked.CompareExchange(ref m_maxThreadCount, 0, 0);
        set
        {
            if (value <= 0)
                throw new ArgumentException("Max thread count must be greater than zero", nameof(value));

            int diff = value - m_maxThreadCount;
            int inactiveThreads = Math.Min(diff, m_logicalThreadQueues.Sum(queue => queue.Count));

            Interlocked.Exchange(ref m_maxThreadCount, value);

            for (int i = 0; i < inactiveThreads; i++)
                ActivatePhysicalThread();
        }
    }

    /// <summary>
    /// Gets or sets the flag that determines whether the threads in
    /// the schedulers thread pool should be background threads.
    /// </summary>
    public bool UseBackgroundThreads { get; set; }

    /// <summary>
    /// Gets the number of levels of priority
    /// supported by this scheduler.
    /// </summary>
    public int PriorityLevels => m_logicalThreadQueues.Length;

    /// <summary>
    /// Gets the current number of active physical threads.
    /// </summary>
    private int ThreadCount => Interlocked.CompareExchange(ref m_threadCount, 0, 0);

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Creates a new logical thread whose
    /// execution is managed by this scheduler.
    /// </summary>
    /// <returns>A new logical thread managed by this scheduler.</returns>
    public LogicalThread CreateThread()
    {
        return new LogicalThread(this);
    }

    /// <summary>
    /// Signals the manager when a logical
    /// thread has new actions to be processed.
    /// </summary>
    /// <param name="thread">The thread with new actions to be processed.</param>
    /// <param name="priority">The priority at which the thread is being signaled.</param>
    internal void SignalItemHandler(LogicalThread thread, int priority)
    {
        if (thread.TryActivate(priority))
        {
            // Make sure the thread has an action to be processed because
            // another thread could have processed all remaining actions and
            // deactivated the thread right before the call to TryActivate()
            if (!thread.HasAction)
            {
                thread.Deactivate();
                return;
            }

            Enqueue(thread);
            ActivatePhysicalThread();
        }
    }

    /// <summary>
    /// Activates a new physical thread if the thread
    /// count has not yet reached its maximum limit.
    /// </summary>
    private void ActivatePhysicalThread()
    {
        int threadCount = ThreadCount;

        while (threadCount < Interlocked.CompareExchange(ref m_maxThreadCount, 0, 0))
        {
            int newThreadCount = Interlocked.CompareExchange(ref m_threadCount, threadCount + 1, threadCount);

            if (newThreadCount == threadCount)
            {
                StartNewPhysicalThread();
                break;
            }

            threadCount = newThreadCount;
        }
    }

    /// <summary>
    /// Starts a new physical thread to
    /// process actions from logical threads.
    /// </summary>
    private void StartNewPhysicalThread()
    {
        Thread thread = new(ProcessLogicalThreads) { IsBackground = UseBackgroundThreads };
        thread.Start();
    }

    /// <summary>
    /// Processes the next available task from the least recently
    /// processed logical thread that is available for processing.
    /// </summary>
    private void ProcessLogicalThreads()
    {
        Func<LogicalThread?>? accessor = null;
        Stopwatch stopwatch = new();

        while (ThreadCount <= MaxThreadCount && m_logicalThreadQueues.FirstOrDefault(queue => queue.TryDequeue(out accessor)) is not null)
        {
            LogicalThread? thread = accessor?.Invoke();

            if (thread is null)
                continue;

            Action? action = thread.Pull();

            if (action is not null)
            {
                LogicalThread.CurrentThread = thread;

                stopwatch.Restart();
                TryExecute(action);
                stopwatch.Stop();

                LogicalThread.CurrentThread = null;
                thread.UpdateStatistics(stopwatch.Elapsed);
            }

            if (thread.HasAction)
                Enqueue(thread);
            else
                thread.Deactivate();
        }

        DeactivatePhysicalThread();

        if (!m_logicalThreadQueues.All(queue => queue.IsEmpty))
            ActivatePhysicalThread();
    }

    /// <summary>
    /// Queues the given thread for execution.
    /// </summary>
    /// <param name="thread">The thread to be queued for execution.</param>
    private void Enqueue(LogicalThread thread)
    {
        ICancellationToken executionToken;
        int activePriority;
        int nextPriority;

        do
        {
            // Create the execution token to be used in the closure
            ICancellationToken nextExecutionToken = new CancellationToken();

            // Always update the thread's active priority before
            // the execution token to mitigate race conditions
            nextPriority = thread.NextPriority;
            thread.ActivePriority = nextPriority;
            thread.NextExecutionToken = nextExecutionToken;

            // Now that the action can be cancelled by another thread using the
            // new cancellation token, it should be safe to put it in the queue
            m_logicalThreadQueues[PriorityLevels - nextPriority].Enqueue(() => nextExecutionToken.Cancel() ? thread : null!);

            // Because en-queuing the thread is a multi-step process, we need to
            // double-check in case the thread's priority changed in the meantime
            activePriority = thread.ActivePriority;
            nextPriority = thread.NextPriority;

            // We can use the cancellation token we just created because we only
            // really need to double-check the work that was done on this thread;
            // in other words, if another thread changed the priority in the
            // meantime, it can double-check its own work
            executionToken = nextExecutionToken;
        }
        while (activePriority != nextPriority && executionToken.Cancel());
    }

    /// <summary>
    /// Attempts to execute the given action.
    /// </summary>
    /// <param name="action">The action to be executed.</param>
    private void TryExecute(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (!TryHandleException(ex))
                throw;
        }
    }

    /// <summary>
    /// Decrements the thread count upon deactivation of a physical thread.
    /// </summary>
    private void DeactivatePhysicalThread()
    {
        Interlocked.Decrement(ref m_threadCount);
    }

    /// <summary>
    /// Attempts to handle the exception via either the logical thread's
    /// exception handler or the schedulers exception handler.
    /// </summary>
    /// <param name="unhandledException">The unhandled exception thrown by an action on the logical thread.</param>
    /// <returns>True if the exception could be handled; false otherwise.</returns>
    private bool TryHandleException(Exception unhandledException)
    {
        bool handled;

        StringBuilder message = new();
        message.AppendFormat("Logical thread action threw an exception of type {0}: {1}", unhandledException.GetType().FullName, unhandledException.Message);
        AggregateException aggregateException = new(message.ToString(), unhandledException);

        try
        {
            // Attempt to handle the exception via the logical thread's exception handler
            handled = LogicalThread.CurrentThread?.OnUnhandledException(unhandledException) ?? false;
        }
        catch (Exception handlerException)
        {
            // If the handler throws an exception,
            // make a note of it in the exception's exception message
            message.AppendLine();
            message.AppendFormat("Logical thread exception handler threw an exception of type {0}: {1}", handlerException.GetType().FullName, handlerException.Message);
            aggregateException = new AggregateException(message.ToString(), aggregateException.InnerExceptions.Concat([
                handlerException
            ]));
            handled = false;
        }

        try
        {
            // If the logical thread's exception handler was not able to handle the exception,
            // attempt to handle the exception via the thread schedulers exception handler
            Exception ex = aggregateException.InnerExceptions.Count > 1 ? aggregateException : unhandledException;
            handled = handled || OnUnhandledException(ex);
        }
        catch (Exception handlerException)
        {
            // If the handler throws an exception,
            // make a note of it in the exception's exception message
            message.AppendLine();
            message.AppendFormat("Scheduler exception handler threw an exception of type {0}: {1}", handlerException.GetType().FullName, handlerException.Message);
            aggregateException = new AggregateException(message.ToString(), aggregateException.InnerExceptions.Concat([
                handlerException
            ]));
            handled = false;
        }

        if (handled)
            return true;

        // If the exception could not be handled by either the
        // logical thread's exception handler or the schedulers
        // exception handler, throw it as an unhandled exception
        if (aggregateException.InnerExceptions.Count > 1)
            throw aggregateException;

        return false;
    }

    /// <summary>
    /// Raises the <see cref="UnhandledException"/> event.
    /// </summary>
    /// <param name="ex">The unhandled exception.</param>
    /// <returns>True if there are any handlers attached to this event; false otherwise.</returns>
    private bool OnUnhandledException(Exception ex)
    {
        EventHandler<EventArgs<Exception>>? unhandledException = UnhandledException;

        if (unhandledException is null)
            return false;

        unhandledException(this, new EventArgs<Exception>(ex));

        return true;
    }

    #endregion
}
