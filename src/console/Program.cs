using System.Diagnostics;
using System.IO.Pipes;


var dotnetCoreFolder = @"C:\source\jmarolf\xunit-discover\artifacts\bin\worker\Debug\net6.0";
var dotnetCoreTestAssembly = @"C:\source\dotnet\roslyn\artifacts\bin\Microsoft.CodeAnalysis.UnitTests\Debug\net6.0\Microsoft.CodeAnalysis.UnitTests.dll";
RunWorker(Path.Combine(dotnetCoreFolder, "worker.exe"), dotnetCoreTestAssembly);

var dotnetFrameworkFolder = @"C:\source\jmarolf\xunit-discover\artifacts\bin\worker\Debug\net472";
var dotnetFrameworkTestAssembly = @"C:\source\dotnet\roslyn\artifacts\bin\Microsoft.CodeAnalysis.UnitTests\Debug\net472\Microsoft.CodeAnalysis.UnitTests.dll";
RunWorker(Path.Combine(dotnetFrameworkFolder, "worker.exe"), dotnetFrameworkTestAssembly);


static void RunWorker(string pathToWorker, string pathToAssembly)
{
    var pipeClient = new Process();

    pipeClient.StartInfo.FileName = pathToWorker;

    using (var pipeServer =
               new AnonymousPipeServerStream(PipeDirection.Out,
                                             HandleInheritability.Inheritable))
    {
        // Pass the client process a handle to the server.
        pipeClient.StartInfo.Arguments = pipeServer.GetClientHandleAsString();
        pipeClient.StartInfo.UseShellExecute = false;
        pipeClient.Start();

        pipeServer.DisposeLocalCopyOfClientHandle();

        try
        {
            // Read user input and send that to the client process.
            using var sw = new StreamWriter(pipeServer);
            sw.AutoFlush = true;
            // Send a 'sync message' and wait for client to receive it.
            sw.WriteLine("SYNC");
            pipeServer.WaitForPipeDrain();
            // Send the console input to the client process.
            sw.WriteLine(pathToAssembly);
        }
        // Catch the IOException that is raised if the pipe is broken
        // or disconnected.
        catch (IOException e)
        {
            Console.WriteLine("[SERVER] Error: {0}", e.Message);
        }
    }

    pipeClient.WaitForExit();
    pipeClient.Close();
}