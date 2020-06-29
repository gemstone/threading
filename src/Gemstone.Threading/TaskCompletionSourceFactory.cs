//******************************************************************************************************
//  TaskCompletionSourceFactory.cs - Gbtc
//
//  Copyright © 2020, Grid Protection Alliance.  All Rights Reserved.
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
//  06/29/2020 - Stephen C. Wills
//       Generated original version of source code.
//
//******************************************************************************************************

using System.Threading.Tasks;

namespace Gemstone.Threading
{
    /// <summary>
    /// Provides factory functions for creating new
    /// <see cref="TaskCompletionSource{TResult}"/> objects.
    /// </summary>
    public static class TaskCompletionSourceFactory
    {
        /// <summary>
        /// The default <see cref="TaskCreationOptions"/> used by this factory
        /// for creating new <see cref="TaskCompletionSource{TResult}"/> objects.
        /// </summary>
        public const TaskCreationOptions DefaultTaskCreationOptions = TaskCreationOptions.RunContinuationsAsynchronously;

        /// <summary>
        /// Creates a new instance of the <see cref="TaskCompletionSource{TResult}"/> class.
        /// </summary>
        /// <typeparam name="T">The type of the result value associated with the <see cref="TaskCompletionSource{TResult}"/>.</typeparam>
        /// <returns>A new object of type <see cref="TaskCompletionSource{TResult}"/>.</returns>
        public static TaskCompletionSource<T> CreateNew<T>() =>
            new TaskCompletionSource<T>(DefaultTaskCreationOptions);

        /// <summary>
        /// Creates a new instance of the <see cref="TaskCompletionSource{TResult}"/> class.
        /// </summary>
        /// <typeparam name="T">The type of the result value associated with the <see cref="TaskCompletionSource{TResult}"/>.</typeparam>
        /// <param name="state">The state to use as the underlying <see cref="Task"/>'s <see cref="Task.AsyncState"/>.</param>
        /// <returns>A new object of type <see cref="TaskCompletionSource{TResult}"/>.</returns>
        public static TaskCompletionSource<T> CreateNew<T>(object state) =>
            new TaskCompletionSource<T>(state, DefaultTaskCreationOptions);
    }
}
