using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BCSClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            using (ClientWebSocket client = new ClientWebSocket())
            {
                Uri serviceUri = new Uri("ws://localhost:5000/websocket-endpoint");
                var cTs = new CancellationTokenSource();
                try
                {
                    await client.ConnectAsync(serviceUri, cTs.Token);
                    var receiveTask = ReceiveMessagesAsync(client, cTs.Token); // Start the receive loop

                    await receiveTask; // Wait for the receive loop to finish
                }
                catch (WebSocketException e)
                {
                    Console.WriteLine(e.Message);
                }
            }
            Console.ReadLine();
        }

        static async Task ReceiveMessagesAsync(ClientWebSocket client, CancellationToken ct)
        {
            var buffer = new byte[1024];
            try
            {
                while (client.State == WebSocketState.Open)
                {
                    var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine("\nMessage received:\n" + message);
                }
            }
            catch (WebSocketException e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}
