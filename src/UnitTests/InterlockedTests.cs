//******************************************************************************************************
//  Tests.cs - Gbtc
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
//  11/04/2019 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gemstone.Threading.UnitTests
{
    [TestClass]
    public class InterlockedTests
    {
        [TestMethod]
        public void AssemblyGuidAttributeValue()
        {
            // Validate that GuidAttribute
            Assembly entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            GuidAttribute attribute = entryAssembly.GetCustomAttributes(typeof(GuidAttribute), true).FirstOrDefault() as GuidAttribute;
            string name = attribute?.Value ?? entryAssembly.GetName().Name;

            System.Console.WriteLine(name);

            Assert.IsTrue(!string.IsNullOrWhiteSpace(name));
        }
    }
}
