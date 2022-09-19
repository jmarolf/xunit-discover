using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading.Channels;

using Xunit;
using Xunit.Abstractions;

if (args.Length > 0)
{
    using var pipeClient = new AnonymousPipeClientStream(PipeDirection.In, args[0]);
    using var sr = new StreamReader(pipeClient);
    // Display the read text to the console
    string? output;

    // Wait for 'sync message' from the server.
    do
    {
        output = sr.ReadLine();
    }
    while (!(output?.StartsWith("SYNC") == true));

    if ((output = sr.ReadLine()) is not null)
    {
        Console.WriteLine("Discovering Tests");
        var assemblyFileName = output;

#if NET6_0_OR_GREATER   
        var resolver = new System.Runtime.Loader.AssemblyDependencyResolver(assemblyFileName);
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            var assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return Assembly.LoadFrom(assemblyPath);
            }

            return null;
        };
#endif


        using var xunit = new XunitFrontController(AppDomainSupport.IfAvailable, assemblyFileName, shadowCopy: false);
        var configuration = ConfigReader.Load(assemblyFileName);
        var sink = new Sink();
        xunit.Find(includeSourceInformation: false, messageSink: sink,
                   discoveryOptions: TestFrameworkOptions.ForDiscovery(configuration));

        var builder = ImmutableArray.CreateBuilder<TestCaseData>();
        await foreach (var testCase in sink.GetTestCases())
        {
            builder.Add(testCase);
        }

        Console.WriteLine($"Discovered '{builder.Count}' tests for '{assemblyFileName}'");
    }
}

internal class Sink : IMessageSink
{
    public Sink()
    {
        _channel = Channel.CreateUnbounded<TestCaseData>();
    }


    private readonly Channel<TestCaseData> _channel;

    public async IAsyncEnumerable<TestCaseData> GetTestCases()
    {
        while (await _channel.Reader.WaitToReadAsync(default).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var item))
            {
                yield return item;
            }
        }
    }

    public bool OnMessage(IMessageSinkMessage message)
    {
        if (message is ITestCaseDiscoveryMessage discoveryMessage)
        {
            OnTestDiscovered(discoveryMessage);
        }

        if (message is IDiscoveryCompleteMessage)
        {
            _channel.Writer.Complete();
        }

        return true;
    }

    private void OnTestDiscovered(ITestCaseDiscoveryMessage testCaseDiscovered)
    {
        var testCase = testCaseDiscovered.TestCase;
        var testCaseData = new TestCaseData(
            testCase.DisplayName,
            testCase.UniqueID,
            testCase.SkipReason,
            testCaseDiscovered.TestAssembly.Assembly.AssemblyPath,
            testCase.Traits);

        _ = _channel.Writer.WriteAsync(testCaseData);
    }
}

internal record TestCaseData(string DisplayName, string UniqueID, string SkipReason, string AssemblyPath, Dictionary<string, List<string>> Traits);
