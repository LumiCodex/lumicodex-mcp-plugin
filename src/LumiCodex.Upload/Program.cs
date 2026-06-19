using LumiCodex.Upload;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await UploaderApplication.RunAsync(args, cancellation.Token);
