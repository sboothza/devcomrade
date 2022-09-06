// Copyright (C) 2020 by Postprintum Pty Ltd (https://www.postprintum.com),
// which licenses this file to you under Apache License 2.0,
// see the LICENSE file in the project root for more information. 
// Author: Andrew Nosenko (@noseratio)

#nullable enable

using System;
using System.Reflection;

namespace AppLogic.Helpers
{
    /// <summary>
    ///     Reflection extensions
    /// </summary>
    internal static class ReflectionExtensions
    {
        public static T CreateDelegate<T>(this MethodInfo @this, object? target) where T : Delegate
        {
            return (T)@this.CreateDelegate(typeof(T), target);
        }
    }
}