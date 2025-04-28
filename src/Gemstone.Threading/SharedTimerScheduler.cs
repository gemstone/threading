//******************************************************************************************************
//  SharedTimerScheduler.cs - Gbtc
//
//  Copyright © 2016, Grid Protection Alliance.  All Rights Reserved.
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
//  11/10/2016 - Stephen C. Wills
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Gemstone.Threading;

/// <summary>
/// Represents a timer manager which is the scheduler of <see cref="SharedTimer"/>.
/// </summary>
/// <remarks>
/// A <see cref="SharedTimer"/> with the same scheduler will use the same ThreadPool thread to process
/// all of the <see cref="SharedTimer"/> instances in series when they have a common interval. Call
/// order, based on registration sequence, will be preserved.
/// </remarks>
public sealed class SharedTimerScheduler : IDisposable
{
    #region [ Members ]

    // Represents a timer scheduler with of a common fire rate
    private class SharedTimerInstance : IDisposable
    {
        #region [ Members ]

        // Fields
        private readonly ConcurrentQueue<WeakAction<DateTime>> m_additionalQueueItems;
        private readonly LinkedList<WeakAction<DateTime>> m_callbacks;
        private readonly Timer m_timer;
        private readonly int m_interval;
        private readonly SharedTimerScheduler m_parentTimer;
        private readonly Lock m_syncRunning;
        private readonly Lock m_syncStats;
        private bool m_disposed;

        // Counter of the number of times an interval has been skipped
        // because the callbacks did not complete before the timer fired
        // again since the last time it was reset
        private long m_skippedIntervals;

        // Total CPU time spent processing the timer events.
        // since the last time it was reset
        private double m_elapsedWorkerTime;

        // Total number of times the timer events have fired 
        // since the last time it was reset
        private int m_elapsedIntervals;

        /// Total number of callbacks that have occurred
        private int m_sumOfCallbacks;

        /// Count of the number of timer callbacks that exists in this factory
        private int m_sharedTimersCount;

        #endregion

        #region [ Constructors ]

        public SharedTimerInstance(SharedTimerScheduler parentTimer, int interval)
        {
            if (interval <= 0)
                throw new ArgumentOutOfRangeException(nameof(interval));

            m_parentTimer = parentTimer ?? throw new ArgumentNullException(nameof(parentTimer));
            m_additionalQueueItems = new ConcurrentQueue<WeakAction<DateTime>>();
            m_syncRunning = new Lock();
            m_syncStats = new Lock();
            m_interval = interval;
            m_callbacks = new LinkedList<WeakAction<DateTime>>();
            m_timer = new Timer(Callback!, null, interval, interval);
        }

        #endregion

        #region [ Methods ]

        public WeakAction<DateTime> RegisterCallback(Action<DateTime> callback)
        {
            if (m_disposed)
                throw new ObjectDisposedException(GetType().FullName);

            WeakAction<DateTime> weakAction = new(callback);
            m_additionalQueueItems.Enqueue(weakAction);

            return weakAction;
        }

        public WeakAction<DateTime> RegisterCallback(WeakAction<DateTime> weakAction)
        {
            if (m_disposed)
                throw new ObjectDisposedException(GetType().FullName);

            m_additionalQueueItems.Enqueue(weakAction);

            return weakAction;
        }

        private void Callback(object state)
        {
            if (m_disposed)
                return;

            ShortTime fireTime = ShortTime.Now;
            bool lockTaken = false;

            try
            {
                lockTaken = m_syncRunning.TryEnter();

                if (!lockTaken)
                {
                    lock (m_syncStats)
                        m_skippedIntervals++;

                    return;
                }

                DateTime fireTimeDatetime = fireTime.UtcTime;
                int loopCount = 0;

                LinkedListNode<WeakAction<DateTime>>? timerAction = m_callbacks.First;

                while (timerAction is not null)
                {
                    if (m_disposed)
                        return;

                    // Removing the linked list item will invalidate the "Next" property, so we store it
                    LinkedListNode<WeakAction<DateTime>>? nextNode = timerAction.Next;

                    try
                    {
                        if (!timerAction.Value.TryInvoke(fireTimeDatetime))
                            m_callbacks.Remove(timerAction);
                    }
                    catch (Exception ex)
                    {
                        LibraryEvents.OnSuppressedException(this, ex);
                    }

                    loopCount++;
                    timerAction = nextNode;
                }

                lock (m_syncStats)
                {
                    m_sharedTimersCount = m_callbacks.Count;
                    m_sumOfCallbacks += loopCount;
                    m_elapsedIntervals++;
                    m_elapsedWorkerTime += fireTime.ElapsedMilliseconds();
                }

                WeakAction<DateTime>? newCallbacks;

                while (m_additionalQueueItems.TryDequeue(out newCallbacks))
                    m_callbacks.AddLast(newCallbacks);

                if (m_callbacks.Count == 0)
                {
                    lock (m_parentTimer.m_syncRoot)
                    {
                        while (m_additionalQueueItems.TryDequeue(out newCallbacks))
                            m_parentTimer.RegisterCallback(m_interval, newCallbacks);

                        if (m_callbacks.Count == 0)
                        {
                            Dispose();
                            m_parentTimer.m_schedulesByInterval.Remove(m_interval);
                        }
                    }
                }
            }
            finally
            {
                if (lockTaken)
                    m_syncRunning.Exit();
            }
        }

        public void Dispose()
        {
            m_disposed = true;
            m_timer.Dispose();
        }

        public void ResetStats()
        {
            lock (m_syncStats)
            {
                m_skippedIntervals = 0;
                m_elapsedIntervals = 0;
                m_elapsedWorkerTime = 0;
                m_elapsedIntervals = 0;
                m_sumOfCallbacks = 0;
            }
        }

        public string Status
        {
            get
            {
                try
                {
                    lock (m_syncStats)
                    {
                        StringBuilder status = new();
                        double averageCpuTime = 0.0D;

                        if (m_elapsedIntervals > 0)
                            averageCpuTime = m_elapsedWorkerTime / m_elapsedIntervals;

                        status.AppendLine($"     Shared Timer Interval: {m_interval:N0}");
                        status.AppendLine($"         Skipped Intervals: {m_skippedIntervals:N0}");
                        status.AppendLine($"         Elapsed Intervals: {m_elapsedIntervals:N0}");
                        status.AppendLine($"          Average CPU Time: {averageCpuTime:N2}ms");
                        status.AppendLine($"          Sum of Callbacks: {m_sumOfCallbacks:N0}");
                        status.AppendLine($"    Shared Timer Callbacks: {m_sharedTimersCount:N0}");
                        status.AppendLine();

                        return status.ToString();
                    }
                }
                catch (Exception ex)
                {
                    LibraryEvents.OnSuppressedException(this, ex);
                    return Environment.NewLine;
                }
            }
        }

        #endregion
    }

    // Fields
    private readonly Dictionary<int, SharedTimerInstance> m_schedulesByInterval;
    private readonly Lock m_syncRoot;
    private bool m_disposed;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new instance of the <see cref="SharedTimerScheduler"/> class.
    /// </summary>
    public SharedTimerScheduler()
    {
        m_syncRoot = new Lock();
        m_schedulesByInterval = new Dictionary<int, SharedTimerInstance>();
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets flag that determines if this <see cref="SharedTimerScheduler"/> instance has been disposed.
    /// </summary>
    public bool IsDisposed => m_disposed;

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Creates a <see cref="SharedTimer"/> using the current <see cref="SharedTimerScheduler"/>.
    /// </summary>
    /// <param name="interval">The interval of the timer, default is 100.</param>
    /// <returns>A shared timer instance that fires at the given interval.</returns>
    public SharedTimer CreateTimer(int interval = 100) => new(this, interval);

    /// <summary>
    /// Attempts to get status of <see cref="SharedTimerInstance"/> for specified <paramref name="interval"/>.
    /// </summary>
    /// <param name="interval">The interval of the timer.</param>
    /// <returns>Timer status.</returns>
    internal string GetStatus(int interval)
    {
        if (interval <= 0)
            return "";

        lock (m_syncRoot)
        {
            if (m_disposed)
                return "";

            if (m_schedulesByInterval.TryGetValue(interval, out SharedTimerInstance? instance))
                return instance.Status ?? "";
        }

        return "";
    }

    /// <summary>
    /// Registers the given callback with the timer running at the given interval.
    /// </summary>
    /// <param name="interval">The interval at which to run the timer.</param>
    /// <param name="callback">The action to be performed when the timer is triggered.</param>
    /// <returns>
    /// The weak reference callback that will be executed when this timer fires. To unregister
    /// the callback, call <see cref="WeakAction.Clear"/>.
    /// </returns>
    internal WeakAction<DateTime> RegisterCallback(int interval, Action<DateTime> callback)
    {
        if (callback is null)
            throw new ArgumentNullException(nameof(callback));

        if (interval <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval));

        if (m_disposed)
            throw new ObjectDisposedException(GetType().FullName);

        lock (m_syncRoot)
        {
            if (m_disposed)
                throw new ObjectDisposedException(GetType().FullName);

            if (!m_schedulesByInterval.TryGetValue(interval, out SharedTimerInstance? instance))
            {
                instance = new SharedTimerInstance(this, interval);
                m_schedulesByInterval.Add(interval, instance);
            }

            return instance.RegisterCallback(callback);
        }
    }

    // ReSharper disable once UnusedMethodReturnValue.Local
    private WeakAction<DateTime> RegisterCallback(int interval, WeakAction<DateTime> weakAction)
    {
        if (weakAction is null)
            throw new ArgumentNullException(nameof(weakAction));

        if (interval <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval));

        if (m_disposed)
            throw new ObjectDisposedException(GetType().FullName);

        lock (m_syncRoot)
        {
            if (m_disposed)
                throw new ObjectDisposedException(GetType().FullName);

            if (!m_schedulesByInterval.TryGetValue(interval, out SharedTimerInstance? instance))
            {
                instance = new SharedTimerInstance(this, interval);
                m_schedulesByInterval.Add(interval, instance);
            }

            return instance.RegisterCallback(weakAction);
        }
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        lock (m_syncRoot)
        {
            m_disposed = true;

            foreach (SharedTimerInstance instance in m_schedulesByInterval.Values)
                instance.Dispose();

            m_schedulesByInterval.Clear();
        }
    }

    #endregion
}
