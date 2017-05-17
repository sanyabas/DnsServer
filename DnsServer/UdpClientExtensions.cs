using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NLog;

namespace DnsServer
{
    public static class UdpClientExtensions
    {
        private static Logger logger = LogManager.GetLogger("DnsServer");
        public static async Task StartProcessingRequestsAsync(this UdpClient client,
            Func<UdpReceiveResult, Task<byte[]>> callback)
        {
            while (true)
            {
                logger.Info("Server started");
                try
                {
                    Console.WriteLine("asdsd");
                    UdpReceiveResult query;
                    var receiveTask = client.ReceiveAsync().ConfigureAwait(false);
                    //var res = await Task.WhenAny(receiveTask, Task.Delay(1500));
                    UdpReceiveResult recvresult = new UdpReceiveResult();
                    await receiveTask;
                    //if (res == receiveTask)
                    //{
                        recvresult = await receiveTask;
                    //}
                    //else
                    //    continue;
                    Console.WriteLine("bbb");
                    logger.Info("Query received");
                    var result = await callback(recvresult);
                    logger.Info("Query handled");
                    var sendTask = client.SendAsync(result, result.Length, recvresult.RemoteEndPoint);
                    var sendRes = await Task.WhenAny(sendTask, Task.Delay(2000));
                    int sent;
                    if (sendRes == sendTask)
                        sent = await sendTask;
                    logger.Info("Answer sent");
                }
                catch (Exception e)
                {
                    logger.Error(e.Message);
                }
            }
        }
    }
}