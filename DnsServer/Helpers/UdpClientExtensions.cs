using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using NLog;

namespace DnsServer.Helpers
{
    public static class UdpClientExtensions
    {
        private static readonly Logger Logger = LogManager.GetLogger("DnsServer");

        public static async Task StartProcessingRequestsAsync(this UdpClient client,
            Func<UdpReceiveResult, Task<byte[]>> callback, Action quitHandler)
        {
            Logger.Info("Server started");
            while (true)
            {
                try
                {
                    var receiveTask = client.ReceiveAsync().ConfigureAwait(false);
                    UdpReceiveResult recvresult;
                    try
                    {
                        recvresult = await receiveTask;
                    }
                    catch (ObjectDisposedException)
                    {
                        quitHandler();
                        return;
                    }
                    Logger.Info("Query received");
                    var result = await callback(recvresult);
                    Logger.Info("Query handled");
                    var sendTask = client.SendAsync(result, result.Length, recvresult.RemoteEndPoint);
                    var sendRes = await Task.WhenAny(sendTask, Task.Delay(2000));
                    if (sendRes == sendTask)
                        await sendTask;
                    Logger.Info("Answer sent");
                }
                catch (Exception e)
                {
                    Logger.Error(e.Message);
                }
            }
        }
    }
}