#region Copyright notice and license
// Protocol Buffers - Google's data interchange format
// Copyright 2008 Google Inc.  All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file or at
// https://developers.google.com/open-source/licenses/bsd
#endregion

using System;

namespace LightProto
{
    /// <summary>
    /// Helper methods for throwing exceptions when preconditions are not met.
    /// </summary>
    /// <remarks>
    /// This class is used internally and by generated code; it is not particularly
    /// expected to be used from application code, although nothing prevents it
    /// from being used that way.
    /// </remarks>
    internal static class ProtoPreconditions
    {
        /// <summary>
        /// Throws an ArgumentNullException if the given value is null, otherwise
        /// return the value to the caller.
        /// </summary>
        public static T CheckNotNull<T>(T value, string name)
            where T : class
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }
            return value;
        }
    }
}
