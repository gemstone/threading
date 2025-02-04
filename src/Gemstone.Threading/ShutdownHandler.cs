﻿//******************************************************************************************************
//  ShutdownHandler.cs - Gbtc
//
//  Copyright © 2016, Grid Protection Alliance.  All Rights Reserved.
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
//  11/03/2016 - Steven E. Chisholm
//       Generated original version of source code. 
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Gemstone.Collections.CollectionExtensions;

namespace Gemstone.Threading;

/// <summary>
/// The order in which the specified callback should occur when shutting down.
/// </summary>
public enum ShutdownHandlerOrder
{
    /// <summary>
    /// This queue is processed first. Unless there is a compelling reason to execute first, select the Default one. 
    /// </summary>
    First,

    /// <summary>
    /// This shutdown order occurs after First, but before Last. 
    /// </summary>
    Default,

    /// <summary>
    /// This queue is processed last. Items such as flushing application logs should go here.
    /// </summary>
    Last
}

/// <summary>
/// This class will monitor the state to the application and raise events when it detects that the application is about to shut down.
/// </summary>
/// <remarks>
/// This class is duplicated here from the Gemstone.Diagnostics project as an internal class to avoid a circular dependency.
/// </remarks>
public static class ShutdownHandler
{
    /// <summary>
    /// Gets if this process is shutting down.
    /// </summary>
    public static bool IsShuttingDown { get; private set; }

    /// <summary>
    /// Gets if this process has already shut down.
    /// </summary>
    public static bool HasShutdown { get; private set; }

    private static readonly List<WeakAction> s_onShutdownCallbackFirst;
    private static readonly List<WeakAction> s_onShutdownCallbackDefault;
    private static readonly List<WeakAction> s_onShutdownCallbackLast;
    private static readonly object s_syncRoot;

    static ShutdownHandler()
    {
        s_syncRoot = new object();
        s_onShutdownCallbackFirst = [];
        s_onShutdownCallbackDefault = [];
        s_onShutdownCallbackLast = [];

        if (AppDomain.CurrentDomain.IsDefaultAppDomain())
            AppDomain.CurrentDomain.ProcessExit += InitiateSafeShutdown;
        else
            AppDomain.CurrentDomain.DomainUnload += InitiateSafeShutdown;

    }

    /// <summary>
    /// Initializes the shutdown handler. This is recommended to put in main loop of the program, but it is not critical.
    /// </summary>
    public static void Initialize()
    {
        // This is handled through the static constructor.
    }

    /// <summary>
    /// Attempts Registers a callback that will be called
    /// when the application is shutdown.
    /// </summary>
    /// <param name="callback">the callback when the shutdown occurs</param>
    /// <param name="shutdownOrder">the order that the callback will occur.</param>
    /// <returns></returns>
    public static bool TryRegisterCallback(Action callback, ShutdownHandlerOrder shutdownOrder = ShutdownHandlerOrder.Default)
    {
        List<WeakAction> list = shutdownOrder switch
        {
            ShutdownHandlerOrder.First => s_onShutdownCallbackFirst,
            ShutdownHandlerOrder.Default => s_onShutdownCallbackDefault,
            ShutdownHandlerOrder.Last => s_onShutdownCallbackLast,
            _ => throw new ArgumentOutOfRangeException(nameof(shutdownOrder), shutdownOrder, null)
        };

        if (IsShuttingDown)
            return false;

        lock (s_syncRoot)
        {
            if (IsShuttingDown)
                return false;

            list.RemoveWhere(x => !x.IsAlive);
            list.Add(new WeakAction(callback));

            return true;
        }
    }

    private static void InitiateSafeShutdown(object? sender, EventArgs? e)
    {
        List<WeakAction> shutdownList = [];

        lock (s_syncRoot)
        {
            if (IsShuttingDown)
                return;

            IsShuttingDown = true;
            shutdownList.AddRange(s_onShutdownCallbackFirst);
            shutdownList.AddRange(s_onShutdownCallbackDefault);
            shutdownList.AddRange(s_onShutdownCallbackLast);
        }

        foreach (WeakAction weakAction in shutdownList)
        {
            try
            {
                weakAction.TryInvoke();
            }
            catch (Exception ex)
            {
                LibraryEvents.OnSuppressedException(typeof(ShutdownHandler), ex);
            }
        }

        HasShutdown = true;
    }

    /// <summary>
    /// Requests that certain components initiate a safe shutdown.
    /// </summary>
    /// <remarks>
    /// This method should only be called when the main thread exits. Calling this outside
    /// of the application exiting could result in unpredictable behavior.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public static void InitiateSafeShutdown()
    {
        InitiateSafeShutdown(null, null);
    }
}
