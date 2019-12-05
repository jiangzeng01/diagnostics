// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Xunit;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using EventPipe.UnitTests.Common;
using Microsoft.Diagnostics.Tracing;

namespace EventPipe.UnitTests.ThreadPoolValidation
{
    public class ProviderTests
    {
        private readonly ITestOutputHelper output;

        public ProviderTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public async void ThreadPool_ProducesEvents()
        {
            await RemoteTestExecutorHelper.RunTestCaseAsync(() => 
            {
                Dictionary<string, ExpectedEventCount> _expectedEventCounts = new Dictionary<string, ExpectedEventCount>()
                {
                    { "Microsoft-Windows-DotNETRuntime", -1 },
                    { "Microsoft-Windows-DotNETRuntimeRundown", -1 }
                };

                var providers = new List<Provider>()
                {
                    //ThreadingKeyword (0x10000): 0b10000_0000_0000_0000
                    new Provider("Microsoft-Windows-DotNETRuntime", 0b10000_0000_0000_0000, EventLevel.Informational)
                };

                Action _eventGeneratingAction = () => 
                {
                    Task[] taskArray = new Task[1000];
                    for (int i = 0; i < 1000; i++)
                    {
                        if (i % 10 == 0)
                            Logger.logger.Log($"Create new task {i} times...");
                        Task task = new Task(() => TestTask());
                        taskArray[i] = task;
                        taskArray[i].Start();
                    }
                    Task.WaitAll(taskArray);
                };

                void TestTask()
                {
                    Thread.Sleep(100);
                }

                Func<EventPipeEventSource, Func<int>> _DoesTraceContainEvents = (source) => 
                {
                    int ThreadStartEvents = 0;
                    //int ThreadStopEvents = 0;
                    source.Clr.ThreadPoolWorkerThreadStart += (eventData) => ThreadStartEvents += 1;
                    //source.Clr.ThreadPoolWorkerThreadStop += (eventData) => ThreadStopEvents += 1;

                    int ThreadPoolWorkerThreadAdjustmentSampleEvents = 0;
                    int ThreadPoolWorkerThreadAdjustmentAdjustmentEvents = 0;
                    source.Clr.ThreadPoolWorkerThreadAdjustmentSample += (eventData) => ThreadPoolWorkerThreadAdjustmentSampleEvents += 1;
                    source.Clr.ThreadPoolWorkerThreadAdjustmentAdjustment += (eventData) => ThreadPoolWorkerThreadAdjustmentAdjustmentEvents += 1;

                    return () => {
                        Logger.logger.Log("Event counts validation");

                        Logger.logger.Log("ThreadStartEvents: " + ThreadStartEvents);
                        //Logger.logger.Log("ThreadStopEvents: " + ThreadStopEvents);
                        //bool ThreadStartStopResult = ThreadStartEvents >= 1 && ThreadStopEvents >= 1;
                        bool ThreadStartStopResult = ThreadStartEvents >= 1;
                        Logger.logger.Log("ThreadStartStopResult check: " + ThreadStartStopResult);

                        Logger.logger.Log("ThreadPoolWorkerThreadAdjustmentSampleEvents: " + ThreadPoolWorkerThreadAdjustmentSampleEvents);
                        Logger.logger.Log("ThreadPoolWorkerThreadAdjustmentAdjustmentEvents: " + ThreadPoolWorkerThreadAdjustmentAdjustmentEvents);
                        bool ThreadAdjustmentResult = ThreadPoolWorkerThreadAdjustmentSampleEvents >= 1 && ThreadPoolWorkerThreadAdjustmentAdjustmentEvents >= 1;
                        Logger.logger.Log("ThreadAdjustmentResult check: " + ThreadAdjustmentResult);
                        
                        return ThreadStartStopResult && ThreadAdjustmentResult ? 100 : -1;
                    };
                };

                var config = new SessionConfiguration(circularBufferSizeMB: (uint)Math.Pow(2, 10), format: EventPipeSerializationFormat.NetTrace,  providers: providers);

                var ret = IpcTraceTest.RunAndValidateEventCounts(_expectedEventCounts, _eventGeneratingAction, config, _DoesTraceContainEvents);
                Assert.Equal(100, ret);
            }, output);
        }
        [Fact]
        public async void IOThread_ProducesEvents()
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
                    new Provider("Microsoft-Windows-DotNETRuntime", 0b10000_0000_0000_0000, EventLevel.Informational)
                };

                string filePath = Path.Combine(Path.GetTempPath(), "Temp.txt");
                Action _eventGeneratingAction = () => 
                {
                    for(int i=0; i<50; i++)
                    {
                        if (i % 10 == 0)
                            Logger.logger.Log($"Create file stream {i} times...");

                        FileStream fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, 1024, true);
                        byte[] bytes= new byte[1024 * 1024];
                        
                        fileStream.BeginWrite(bytes, 0, bytes.Length, new AsyncCallback(asyncCallback), fileStream);
                        Thread.Sleep(1000);
                    }
                };
                void asyncCallback(IAsyncResult result)
                {
                    FileStream fileStream = (FileStream)result.AsyncState;
                    fileStream.EndWrite(result);
                    fileStream.Close();
                    File.Delete(filePath);
                } 

                Func<EventPipeEventSource, Func<int>> _DoesTraceContainEvents = (source) => 
                {
                    int IOThreadCreationStartEvents = 0;
                    int IOThreadCreationStopEvents = 0;
                    source.Clr.IOThreadCreationStart += (eventData) => IOThreadCreationStartEvents += 1;
                    source.Clr.IOThreadCreationStop += (eventData) => IOThreadCreationStopEvents += 1;

                    return () => {
                        Logger.logger.Log("Event counts validation");

                        Logger.logger.Log("IOThreadCreationStartEvents: " + IOThreadCreationStartEvents);
                        Logger.logger.Log("IOThreadCreationStopEvents: " + IOThreadCreationStopEvents);
                        bool IOThreadCreationStartStopResult = IOThreadCreationStartEvents >= 1 && IOThreadCreationStopEvents >= 1;
                        Logger.logger.Log("IOThreadCreationStartStopResult check: " + IOThreadCreationStartStopResult);

                        return IOThreadCreationStartStopResult ? 100 : -1;
                    };
                };

                var config = new SessionConfiguration(circularBufferSizeMB: (uint)Math.Pow(2, 10), format: EventPipeSerializationFormat.NetTrace,  providers: providers);

                var ret = IpcTraceTest.RunAndValidateEventCounts(_expectedEventCounts, _eventGeneratingAction, config, _DoesTraceContainEvents);
                Assert.Equal(100, ret);
            }, output);
        }
    }
}