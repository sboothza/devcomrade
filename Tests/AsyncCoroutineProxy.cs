// Copyright (C) 2020 by Postprintum Pty Ltd (https://www.postprintum.com),
// which licenses this file to you under Apache License 2.0,
// see the LICENSE file in the project root for more information. 
// Author: Andrew Nosenko (@noseratio)

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Tests
{
    public interface ICoroutineProxy<T>
    {
        public Task<IAsyncEnumerable<T>> AsAsyncEnumerable(CancellationToken token = default);
    }

    public static class CoroutineProxyExt
    {
        public static async Task<IAsyncEnumerator<T>> AsAsyncEnumerator<T>(this ICoroutineProxy<T> @this, CancellationToken token = default)
        {
            return (await @this.AsAsyncEnumerable(token)).GetAsyncEnumerator(token);
        }

        public static async ValueTask<T> GetNextAsync<T>(this IAsyncEnumerator<T> @this)
        {
            if (!await @this.MoveNextAsync())
                throw new IndexOutOfRangeException(nameof(GetNextAsync));

            return @this.Current;
        }

        public static Task<T> GetNextAsync<T>(this IAsyncEnumerator<T> @this, CancellationToken token)
        {
            return @this.GetNextAsync()
                        .AsTask()
                        .ContinueWith(ante => ante,
                                      token,
                                      TaskContinuationOptions.ExecuteSynchronously,
                                      TaskScheduler.Default)
                        .Unwrap();
        }

        public static Task Run<T>(this AsyncCoroutineProxy<T> @this,
                                  IAsyncApartment apartment,
                                  Func<CancellationToken, IAsyncEnumerable<T>> routine,
                                  CancellationToken token)
        {
            return apartment.Run(() => @this.Run(routine, token), token);
        }
    }

    public class AsyncCoroutineProxy<T> : ICoroutineProxy<T>
    {
        private readonly TaskCompletionSource<IAsyncEnumerable<T>> _proxyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<IAsyncEnumerable<T>> ICoroutineProxy<T>.AsAsyncEnumerable(CancellationToken token)
        {
            await using var _ = token.Register(() => _proxyTcs.TrySetCanceled(), false);
            return await _proxyTcs.Task;
        }

        public async Task Run(Func<CancellationToken, IAsyncEnumerable<T>> routine, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var channel = Channel.CreateUnbounded<T>();
            var writer = channel.Writer;
            var proxy = channel.Reader.ReadAllAsync(token);
            _proxyTcs.SetResult(proxy); // throw if already set

            try
            {
                await foreach (var item in routine(token)
                    .WithCancellation(token))
                    await writer.WriteAsync(item, token);

                writer.Complete();
            }
            catch (Exception ex)
            {
                writer.Complete(ex);
                throw;
            }
        }
    }
}