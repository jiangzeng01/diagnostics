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
using Microsoft.Diagnostics.Tracing;

namespace EventPipe.UnitTests.GCEventsValidation
{

    public class TestClass
    {
        public int a;
        public string b;

        public TestClass()
        {
            a = 0;
            b = "";
        }
    }
    public class ProviderTests
    {
        private readonly ITestOutputHelper output;

        public ProviderTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public async void GCCollect_ProducesEvents()
        {
            await RemoteTestExecutorHelper.RunTestCaseAsync(() => 
            {
                Dictionary<string, ExpectedEventCount> _expectedEventCounts = new Dictionary<string, ExpectedEventCount>()
                {
                    { "Microsoft-Windows-DotNETRuntime", -1 },
                    { "Microsoft-Windows-DotNETRuntimeRundown", -1 },
                    { "Microsoft-DotNETCore-SampleProfiler", -1 }
                };

                var providers = new List<Provider>()
                {
                    new Provider("Microsoft-DotNETCore-SampleProfiler"),
                    //GCKeyword (0x1): 0b1
                    new Provider("Microsoft-Windows-DotNETRuntime", 0b1, EventLevel.Informational)
                };

                Action _eventGeneratingAction = () => 
                {
                    for (int i = 0; i < 50; i++)
                    {
                        if (i % 10 == 0)
                            Logger.logger.Log($"Called GC.Collect() {i} times...");
                        TestClass testClass = new TestClass();
                        testClass = null;
                        GC.Collect();
                    }
                };

                Func<EventPipeEventSource, Func<int>> _DoesTraceContainEvents = (source) => 
                {
                    int GCStartEvents = 0;
                    int GCEndEvents = 0;
                    source.Clr.GCStart += (eventData) => GCStartEvents += 1;
                    source.Clr.GCStop += (eventData) => GCEndEvents += 1;

                    int GCRestartEEStartEvents = 0;
                    int GCRestartEEStopEvents = 0;           
                    source.Clr.GCRestartEEStart += (eventData) => GCRestartEEStartEvents += 1;
                    source.Clr.GCRestartEEStop += (eventData) => GCRestartEEStopEvents += 1; 

                    int GCSuspendEEEvents = 0;
                    int GCSuspendEEEndEvents = 0;
                    source.Clr.GCSuspendEEStart += (eventData) => GCSuspendEEEvents += 1;
                    source.Clr.GCSuspendEEStop += (eventData) => GCSuspendEEEndEvents += 1;

                    int GCHeapStatsEvents =0;
                    source.Clr.GCHeapStats += (eventData) => GCHeapStatsEvents +=1;

                    return () => {
                        Logger.logger.Log("Event counts validation");

                        Logger.logger.Log("GCStartEvents: " + GCStartEvents);
                        Logger.logger.Log("GCEndEvents: " + GCEndEvents);
                        bool GCStartStopResult = GCStartEvents >= 50 && GCEndEvents >= 50 && Math.Abs(GCStartEvents - GCEndEvents) <=2;
                        Logger.logger.Log("GCStartStopResult check: " + GCStartStopResult);

                        Logger.logger.Log("GCRestartEEStartEvents: " + GCRestartEEStartEvents);
                        Logger.logger.Log("GCRestartEEStopEvents: " + GCRestartEEStopEvents);
                        bool GCRestartEEStartStopResult = GCRestartEEStartEvents >= 50 && GCRestartEEStopEvents >= 50;
                        Logger.logger.Log("GCRestartEEStartStopResult check: " + GCRestartEEStartStopResult);

                        Logger.logger.Log("GCSuspendEEEvents: " + GCSuspendEEEvents);
                        Logger.logger.Log("GCSuspendEEEndEvents: " + GCSuspendEEEndEvents);
                        bool GCSuspendEEStartStopResult = GCSuspendEEEvents >= 50 && GCSuspendEEEndEvents >= 50;
                        Logger.logger.Log("GCSuspendEEStartStopResult check: " + GCSuspendEEStartStopResult);

                        Logger.logger.Log("GCHeapStatsEvents: " + GCHeapStatsEvents);
                        bool GCHeapStatsEventsResult = GCHeapStatsEvents >= 50 && GCHeapStatsEvents >= 50;
                        Logger.logger.Log("GCHeapStatsEventsResult check: " + GCHeapStatsEventsResult);

                        return GCStartStopResult && GCRestartEEStartStopResult && GCSuspendEEStartStopResult && GCHeapStatsEventsResult ? 100 : -1;
                    };
                };

                var config = new SessionConfiguration(circularBufferSizeMB: (uint)Math.Pow(2, 10), format: EventPipeSerializationFormat.NetTrace,  providers: providers);

                var ret = IpcTraceTest.RunAndValidateEventCounts(_expectedEventCounts, _eventGeneratingAction, config, _DoesTraceContainEvents);
                Assert.Equal(100, ret);
            }, output);
        }
    }
}