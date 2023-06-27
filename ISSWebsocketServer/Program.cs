using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System;

namespace BCSServer
{
    public class Program
    {
        private static ConcurrentDictionary<string, WebSocket> _webSockets = new ConcurrentDictionary<string, WebSocket>();
        private static readonly HttpClient client = new HttpClient();

        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls("http://localhost:5000");
                    webBuilder.Configure(app =>
                    {
                        app.UseWebSockets();

                        app.Use(async (context, next) =>
                        {
                            if (context.Request.Path == "/websocket-endpoint")
                            {
                                if (context.WebSockets.IsWebSocketRequest)
                                {
                                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                                    string connectionId = Guid.NewGuid().ToString();
                                    _webSockets.TryAdd(connectionId, webSocket);
                                    await HandleWebSocket(connectionId);
                                }
                                else
                                {
                                    context.Response.StatusCode = 400;
                                }
                            }
                            else
                            {
                                await next();
                            }
                        });

                        app.UseRouting();

                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/", async context =>
                            {
                                await context.Response.WriteAsync("Hello World!");
                            });
                        });
                    });
                });

        private static async Task HandleWebSocket(string connectionId)
        {
            WebSocket webSocket = _webSockets[connectionId];
            byte[] buffer = new byte[1024];

            while (webSocket.State == WebSocketState.Open)
            {
                // Fetch and broadcast ISS location
                var issData = await FetchISSDataAsync();
                await BroadcastMessage(connectionId, issData);

                await Task.Delay(2000);  // Wait for 2 seconds before the next update
            }

            _webSockets.TryRemove(connectionId, out _);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
        }

        private static async Task<string> FetchISSDataAsync()
        {
            var response = await client.GetAsync("http://api.open-notify.org/iss-now.json");
            var response2 = await client.GetAsync("http://api.open-notify.org/astros.json");
            if (response.IsSuccessStatusCode && response2.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var issData = JObject.Parse(json);

                var json2 = await response2.Content.ReadAsStringAsync();
                var humanData = JObject.Parse(json2);

                var latitude = issData["iss_position"]["latitude"].ToString();
                var longitude = issData["iss_position"]["longitude"].ToString();
                var timestamp = issData["timestamp"].ToString();
                var numberPeople = humanData["number"].ToString();

                var message = $"Timestamp: {timestamp}, Latitude: {latitude}, Longitude: {longitude} \n And there are {numberPeople} people currently in space.";
                return message;
            }
            else
            {
                return "Error: unable to fetch ISS data";
            }

            

        }

        private static async Task BroadcastMessage(string senderId, string message)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            foreach (var pair in _webSockets)
            {
                WebSocket webSocket = pair.Value;
                if (webSocket.State == WebSocketState.Open)

                {
                    await webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                    Console.WriteLine($"Sent message to {pair.Key}: {message}");
                }
            }
        }
    }
}

