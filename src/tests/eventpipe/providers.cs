// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using EventPipe.UnitTests.Common;

// Use this test as an example of how to write tests for EventPipe in
// the dotnet/diagnostics repo

namespace EventPipe.UnitTests.ProviderValidation
{
    public sealed class MyEventSource : EventSource
    {
        private MyEventSource() {}
        public static MyEventSource Log = new MyEventSource();
        public void MyEvent() { WriteEvent(1, "MyEvent"); }
    }

    public class ProviderTests
    {
        private readonly ITestOutputHelper output;

        public ProviderTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public async void UserDefinedEventSource_ProducesEvents()
        {
            await RemoteTestExecutorHelper.RunTestCaseAsync(() => 
            {
                Dictionary<string, ExpectedEventCount> expectedEventCounts = new Dictionary<string, ExpectedEventCount>()
                {
                    { "MyEventSource", new ExpectedEventCount(100_000, 0.30f) },
                    { "Microsoft-Windows-DotNETRuntimeRundown", -1 },
                    { "Microsoft-DotNETCore-SampleProfiler", -1 }
                };

                var providers = new List<Provider>()
                {
                    new Provider("MyEventSource"),
                    new Provider("Microsoft-DotNETCore-SampleProfiler")
                };

                Action eventGeneratingAction = () => 
                {
                    for (int i = 0; i < 100_000; i++)
                    {
                        if (i % 10_000 == 0)
                            Logger.logger.Log($"Fired MyEvent {i:N0}/100,000 times...");
                        MyEventSource.Log.MyEvent();
                    }
                };

                var config = new SessionConfiguration(circularBufferSizeMB: (uint)Math.Pow(2, 10), format: EventPipeSerializationFormat.NetTrace,  providers: providers);

                var ret = IpcTraceTest.RunAndValidateEventCounts(expectedEventCounts, eventGeneratingAction, config);
                Assert.Equal(100, ret);
            }, output);
        }
    }
}