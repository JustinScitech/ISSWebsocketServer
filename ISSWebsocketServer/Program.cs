using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Data.SqlClient;

namespace BCSServer
{
    public class Program
    {
        public List<StationInfo> stationData = new List<StationInfo>();
        
        public void OnGet()
        {
            try
            {
                DotNetEnv.Env.Load();
                var password = Environment.GetEnvironmentVariable("your_password");
                String connectionString = "Data Source=scmsbcs.database.windows.net,1433;Initial Catalog=rawdatadb;User ID=CloudSA1760a6ce;Password=" + password;

                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    String sql = "SELECT * FROM clients";
                    using (SqlCommand command = new SqlCommand(sql,connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while(reader.Read())
                            {
                                StationInfo stationInfo = new StationInfo();
                                stationInfo.num_of_people = reader.GetInt32(0);
                                stationInfo.longitude = reader.GetDouble(1);
                                stationInfo.latitude = reader.GetDouble(2);
                                stationData.Add(stationInfo);

                            }
                        }
                    }
                }
            }
            catch (Exception e) { 
                Console.WriteLine(e);
                return;
            }
        }

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

        // Parse JSON data
        string latitude = issData["iss_position"]["latitude"].ToString();
        string longitude = issData["iss_position"]["longitude"].ToString();
        string num_of_people = humanData["number"].ToString();

        // Get most recent entry from the station_table
        StationInfo mostRecentEntry = GetMostRecentEntry();
        // If the new data is different from the most recent entry in the table, then insert the new data
        if (mostRecentEntry == null || mostRecentEntry.latitude != double.Parse(latitude) || mostRecentEntry.longitude != double.Parse(longitude) || mostRecentEntry.num_of_people != int.Parse(num_of_people))
        {
            InsertDataIntoStationTable(double.Parse(latitude), double.Parse(longitude), int.Parse(num_of_people));
        }

        string peopleInfo = "";

        foreach (var person in humanData["people"]) { 
             peopleInfo += person["name"] + " | ";
        }

        string message = $"Latitude: {latitude}, Longitude: {longitude} \nAnd there are {num_of_people} people currently in space. \nThe people include: {peopleInfo}";
        return message;
    }
    else
    {
        return "Error: unable to fetch ISS data";
    }
}

private static StationInfo GetMostRecentEntry()
{
            DotNetEnv.Env.Load();
            var password = Environment.GetEnvironmentVariable("your_password");
            String connectionString = "Data Source=scmsbcs.database.windows.net,1433;Initial Catalog=rawdatadb;User ID=CloudSA1760a6ce;Password=" + password;

            using (SqlConnection connection = new SqlConnection(connectionString))
    {
        connection.Open();
        String sql = "SELECT TOP 1 * FROM station_table ORDER BY id DESC"; // Assuming that there's an id column acting as a primary key
        using (SqlCommand command = new SqlCommand(sql,connection))
        {
            using (SqlDataReader reader = command.ExecuteReader())
            {
                while(reader.Read())
                {
                    StationInfo stationInfo = new StationInfo();
                    stationInfo.num_of_people = reader.GetInt32(0);
                    stationInfo.longitude = reader.GetDouble(1);
                    stationInfo.latitude = reader.GetDouble(2);
                    return stationInfo;
                }
            }
        }
    }
    return null;
}

private static void InsertDataIntoStationTable(double latitude, double longitude, int numberOfPeople)
{
    DotNetEnv.Env.Load();
    var password = Environment.GetEnvironmentVariable("your_password");
    String connectionString = "Data Source=scmsbcs.database.windows.net,1433;Initial Catalog=rawdatadb;User ID=CloudSA1760a6ce;Password=" + password;

            using (SqlConnection connection = new SqlConnection(connectionString))
    {
        connection.Open();
        String sql = "INSERT INTO station_table (latitude, longitude, num_of_people) VALUES (@lat, @long, @num)"; // Column names in station_table
        using (SqlCommand command = new SqlCommand(sql,connection))
        {
            command.Parameters.AddWithValue("@lat", latitude);
            command.Parameters.AddWithValue("@long", longitude);
            command.Parameters.AddWithValue("@num", numberOfPeople);
            command.ExecuteNonQuery();
        }
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
                    Console.WriteLine($"Server sent message to {pair.Key}: {message}");
                }
            }
        }
    }

    public class StationInfo
    {
        public double longitude;
        public double latitude;
        public int num_of_people;
    }

}

