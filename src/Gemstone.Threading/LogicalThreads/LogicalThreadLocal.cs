﻿//******************************************************************************************************
//  LogicalThreadLocal.cs - Gbtc
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
//  09/24/2015 - Stephen C. Wills
//       Generated original version of source code.
//  11/16/2023 - Lillian Gensolin
//       Converted code to .NET core.
//
//******************************************************************************************************

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Gemstone.EventHandlerExtensions;

namespace Gemstone.Threading.LogicalThreads;

/// <summary>
/// Represents a slot in the thread local memory space of each logical thread.
/// </summary>
public sealed class LogicalThreadLocal<T> : IDisposable
{
    #region [ Members ]

    // Nested Types
    private class Slot
    {
        public T? Value { get; set; }
    }

    // Events
    private event EventHandler? Disposed;

    // Fields
    private readonly Func<T>? m_newObjectFactory;
    private readonly ThreadLocal<T> m_threadLocal;

    private int m_accessCount;
    private int m_disposed;

    #endregion

    #region [ Constructors ]

    /// <summary>
    /// Creates a new instance of the <see cref="LogicalThreadLocal{T}"/> class.
    /// </summary>
    public LogicalThreadLocal()
        : this(() => default!)
    {
    }

    /// <summary>
    /// Creates a new instance of the <see cref="LogicalThreadLocal{T}"/> class.
    /// </summary>
    /// <param name="newObjectFactory">Factory to produce the initial value when accessing uninitialized values.</param>
    public LogicalThreadLocal(Func<T>? newObjectFactory)
    {
        m_newObjectFactory = newObjectFactory;
        m_threadLocal = new ThreadLocal<T>(newObjectFactory!);
    }

    /// <summary>
    /// Releases the unmanaged resources before the <see cref="LogicalThreadLocal{T}"/> object is reclaimed by <see cref="GC"/>.
    /// </summary>
    ~LogicalThreadLocal()
    {
        Dispose(false);
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets or sets the thread local object
    /// associated with the current logical thread.
    /// </summary>
    public T? Value
    {
        get
        {
            return Access(() =>
            {
                if (TryGetSlot(out Slot? slot))
                    return slot.Value;

                return IsDisposed ? default : m_threadLocal.Value;
            });
        }
        set
        {
            Access(() =>
            {
                if (TryGetSlot(out Slot? slot))
                    slot.Value = value;
                else if (!IsDisposed)
                    m_threadLocal.Value = value;
            });
        }
    }

    /// <summary>
    /// Gets a flag that indicates whether this object has been disposed.
    /// </summary>
    private bool IsDisposed
    {
        get
        {
            return Interlocked.CompareExchange(ref m_disposed, 0, 0) != 0;
        }
    }

    #endregion

    #region [ Methods ]

    /// <summary>
    /// Releases all the resources used by the <see cref="LogicalThreadLocal{T}"/> object.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Executes the given action in a protected code block that ensures all
    /// unmanaged resources get cleaned up after a call to <see cref="Dispose()"/>.
    /// </summary>
    /// <param name="action">The action to be executed.</param>
    private void Access(Action action)
    {
        try
        {
            Interlocked.Increment(ref m_accessCount);

            if (IsDisposed)
                return;

            action();
        }
        finally
        {
            Interlocked.Decrement(ref m_accessCount);

            if (IsDisposed && Interlocked.CompareExchange(ref m_accessCount, 0, 0) == 0)
                OnDisposed(true);
        }
    }

    /// <summary>
    /// Executes the given function in a protected code block that ensures all
    /// unmanaged resources get cleaned up after a call to <see cref="Dispose()"/>.
    /// </summary>
    /// <param name="func">The function to be executed.</param>
    private TResult? Access<TResult>(Func<TResult> func)
    {
        try
        {
            Interlocked.Increment(ref m_accessCount);
            return !IsDisposed ? func() : default;
        }
        finally
        {
            Interlocked.Decrement(ref m_accessCount);

            if (IsDisposed && Interlocked.CompareExchange(ref m_accessCount, 0, 0) == 0)
                OnDisposed(true);
        }
    }

    /// <summary>
    /// Attempts to get the instance of this slot from the current logical thread.
    /// </summary>
    /// <param name="slot">An instance of this slot.</param>
    /// <returns>True if an instance of this slot is successfully retrieved from the current logical thread.</returns>
    private bool TryGetSlot([NotNullWhen(true)] out Slot? slot)
    {
        if (IsDisposed)
        {
            slot = null;
            return false;
        }

        LogicalThread? currentThread = LogicalThread.CurrentThread;

        if (currentThread is null)
        {
            slot = null;
            return false;
        }

        slot = currentThread.GetThreadLocal(this) as Slot;

        if (slot is null)
        {
            slot = new Slot { Value = m_newObjectFactory!() };
            currentThread.SetThreadLocal(this, slot);
            AttachToDisposed();
        }

        return true;
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="LogicalThreadLocal{T}"/> object and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    private void Dispose(bool disposing)
    {
        Interlocked.CompareExchange(ref m_disposed, -1, 0);

        if (Interlocked.CompareExchange(ref m_accessCount, 0, 0) == 0)
            OnDisposed(disposing);
    }

    /// <summary>
    /// Attaches logic to dispose of the slot in
    /// the currently executing logical thread.
    /// </summary>
    private void AttachToDisposed()
    {
        WeakReference<LogicalThread> threadRef = new(LogicalThread.CurrentThread!);

        Disposed += (_, _) =>
        {
            if (threadRef.TryGetTarget(out LogicalThread? thread))
                thread.SetThreadLocal(this, null);
        };
    }

    /// <summary>
    /// Invokes the <see cref="Disposed"/> event handler.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    private void OnDisposed(bool disposing)
    {
        if (Interlocked.CompareExchange(ref m_disposed, 1, -1) != -1)
            return;

        Disposed?.SafeInvoke(this, EventArgs.Empty);

        if (disposing)
            m_threadLocal.Dispose();
    }

    #endregion
}
