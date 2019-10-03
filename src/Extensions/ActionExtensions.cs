//******************************************************************************************************
//  ActionExtensions.cs - Gbtc
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
//  02/02/2016 - Stephen C. Wills
//       Generated original version of source code.
//  10/01/2019 - Stephen C. Wills
//       Updated implementation of DelayAndExecute to use TPL instead of ThreadPool.
//
//******************************************************************************************************

using System;
using System.Threading;
using System.Threading.Tasks;

namespace gemstone.threading.Extensions
{
    /// <summary>
    /// Defines extension methods for actions.
    /// </summary>
    public static class ActionExtensions
    {
        /// <summary>
        /// Execute an action on the thread pool after a specified number of milliseconds.
        /// </summary>
        /// <param name="action">The action to be executed.</param>
        /// <param name="delay">The amount of time to wait before execution, in milliseconds.</param>
        /// <param name="cancellationToken">The token used to cancel execution.</param>
        /// <returns>
        /// A function to call which will cancel the operation.
        /// Cancel function returns true if <paramref name="action"/> is cancelled in time, false if not.
        /// </returns>
        public static void DelayAndExecute(this Action action, int delay, CancellationToken cancellationToken) =>
            new Action<CancellationToken>(_ => action()).DelayAndExecute(delay, cancellationToken);

        /// <summary>
        /// Execute a cancellable action on the thread pool after a specified number of milliseconds.
        /// </summary>
        /// <param name="action">The action to be executed.</param>
        /// <param name="delay">The amount of time to wait before execution, in milliseconds.</param>
        /// <param name="cancellationToken">The token used to cancel execution.</param>
        public static void DelayAndExecute(this Action<CancellationToken> action, int delay, CancellationToken cancellationToken) =>
            Task.Delay(delay, cancellationToken).ContinueWith(task => action(cancellationToken));

        /// <summary>
        /// Execute an action on the thread pool after a specified number of milliseconds.
        /// </summary>
        /// <param name="action">The action to be executed.</param>
        /// <param name="delay">The amount of time to wait before execution, in milliseconds.</param>
        /// <returns>
        /// A function to call which will cancel the operation.
        /// Cancel function returns true if <paramref name="action"/> is cancelled in time, false if not.
        /// </returns>
        public static Func<bool> DelayAndExecute(this Action action, int delay) =>
            new Action<CancellationToken>(_ => action()).DelayAndExecute(delay);

        /// <summary>
        /// Execute a cancellable action on the thread pool after a specified number of milliseconds.
        /// </summary>
        /// <param name="action">The action to be executed.</param>
        /// <param name="delay">The amount of time to wait before execution, in milliseconds.</param>
        /// <returns>
        /// A function to call which will cancel the operation.
        /// Cancel function returns true if <paramref name="action"/> is cancelled, false if not.
        /// </returns>
        public static Func<bool> DelayAndExecute(this Action<CancellationToken> action, int delay)
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;

            Func<bool> cancelFunc = () =>
            {
                CancellationTokenSource tokenSourceRef = Interlocked.Exchange(ref tokenSource, null);
                tokenSourceRef?.Cancel();
                tokenSourceRef?.Dispose();
                return tokenSourceRef != null;
            };

            Action<CancellationToken> executeAction = _ =>
            {
                CancellationTokenSource tokenSourceRef = Interlocked.Exchange(ref tokenSource, null);

                try
                {
                    if (!(tokenSourceRef?.IsCancellationRequested ?? true))
                        action(token);
                }
                finally
                {
                    tokenSourceRef?.Dispose();
                }
            };

            executeAction.DelayAndExecute(delay, token);

            return cancelFunc;
        }
    }
}
