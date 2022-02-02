// Copyright (C) 2020 by Postprintum Pty Ltd (https://www.postprintum.com),
// which licenses this file to you under Apache License 2.0,
// see the LICENSE file in the project root for more information. 
// Author: Andrew Nosenko (@noseratio)

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AppLogic.Helpers
{
    /// <summary>
    ///     Exception extensions
    /// </summary>
    internal static class ExceptionExtensions
    {
        /// <summary>
        ///     Unwrap a parent-child chain of single instance AggregateException exceptions
        /// </summary>
        public static Exception Unwrap(this Exception @this)
        {
            var inner = @this;
            while (true)
            {
                if (inner is not AggregateException aggregate || aggregate.InnerExceptions.Count != 1)
                    return inner;

                inner = aggregate.InnerExceptions[0];
            }
        }

        /// <summary>
        ///     Exception as enumerable, with recursive flattening of any nested AggregateException
        /// </summary>
        public static IEnumerable<Exception> AsEnumerable(this Exception? @this)
        {
            switch (@this)
            {
                case null:
                    yield break;

                // uncommon but possible: AggregateException without inner exceptions
                case AggregateException aggregate when aggregate.InnerExceptions.Count == 0:
                    yield return aggregate;

                    break;

                // the most common case: one wrapped exception which is not AggregateException 
                case AggregateException aggregate when aggregate.InnerExceptions.Count == 1 && aggregate.InnerException is not AggregateException:
                    yield return aggregate.InnerExceptions[0];

                    break;

                // yield all of inner exceptions recursively
                case AggregateException aggregate:
                {
                    foreach (var child in aggregate.InnerExceptions.SelectMany(inner => inner.AsEnumerable()))
                        yield return child;

                    break;
                }

                default:
                    yield return @this;

                    break;
            }
        }

        /// <summary>
        ///     Suppress "is never used" warning, use it only if you really mean it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Unused(this Exception _) {}

        /// <summary>
        ///     True if the exception is an instance of OperationCanceledException (or a derived class)
        /// </summary>
        public static bool IsOperationCanceled(this Exception? @this)
        {
            switch (@this)
            {
                case null:
                    return false;

                case OperationCanceledException:
                case AggregateException aggregate when aggregate.InnerExceptions.Count == 1 && aggregate.InnerExceptions[0] is OperationCanceledException:
                    return true;

                case AggregateException aggregate:
                    return aggregate.AsEnumerable()
                                    .All(ex => (ex is OperationCanceledException));

                default:
                    return false;
            }
        }
    }
}