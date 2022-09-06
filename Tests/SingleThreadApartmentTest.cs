﻿// Copyright (C) 2020 by Postprintum Pty Ltd (https://www.postprintum.com),
// which licenses this file to you under Apache License 2.0,
// see the LICENSE file in the project root for more information. 
// Author: Andrew Nosenko (@noseratio)

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using AppLogic.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    [TestClass]
    public class SingleThreadApartmentTest
    {
        [TestMethod]
        public async Task Test_async_void_exceptions_are_captured()
        {
            await using var apartment = new SingleThreadApartment();
            await apartment.Run(async () =>
                                {
                                    await Task.Yield();

                                    Assert.IsTrue(SynchronizationContext.Current is SingleThreadApartment.SingleThreadSyncContext);

                                    async void asyncVoidMethod0()
                                    {
                                        await Task.Delay(400);
                                        throw new InvalidOperationException();
                                    }

                                    async void asyncVoidMethod1()
                                    {
                                        await Task.Delay(600);
                                        throw new NotSupportedException();
                                    }

                                    asyncVoidMethod0();
                                    asyncVoidMethod1();
                                });

            apartment.Complete();

            try
            {
                await apartment.Completion.WithAggregatedExceptions();
                Assert.Fail("Must not reach here, expecting exceptions.");
            }
            catch (AssertFailedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                apartment.ClearExceptions();

                // we expect to see 2 exceptions wrapped as AggregateException
                Assert.IsTrue(apartment.AnyBackgroundOperation == false);

                var aggregate = ex as AggregateException;
                Assert.IsNotNull(aggregate);
                Assert.IsTrue(aggregate!.InnerExceptions.Count == 2);
                Assert.IsTrue(aggregate.InnerExceptions[0] is InvalidOperationException);
                Assert.IsTrue(aggregate.InnerExceptions[1] is NotSupportedException);
            }
        }
    }
}