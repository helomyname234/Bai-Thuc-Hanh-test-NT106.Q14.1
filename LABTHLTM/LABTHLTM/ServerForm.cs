using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LABTHLTM
{
    public partial class ServerForm : Form
    {
        private TcpListener server;
        private List<MenuItem> menu;
        private Dictionary<int, List<OrderItem>> orders;
        private CancellationTokenSource cancellationTokenSource;
        private const int PORT = 5000;
        private readonly object lockObject = new object();

        public ServerForm()
        {
            InitializeComponent();
            menu = new List<MenuItem>();
            orders = new Dictionary<int, List<OrderItem>>();
        }

        private async void ServerForm_Load(object sender, EventArgs e)
        {
            LoadMenu();
            lblStatus.Text = $"Server: {GetLocalIPAddress()}:{PORT}";
            await StartServerAsync();
        }

        private void LoadMenu()
        {
            try
            {
                string menuFile = "menu.txt";
                if (!File.Exists(menuFile))
                {
                    File.WriteAllLines(menuFile, new[]
                    {
                        "1;Phở Bò;50000",
                        "2;Cơm Tấm;40000",
                        "3;Gỏi Cuốn;30000",
                        "4;Bún Chả;50000",
                        "5;Bánh Mì;20000"
                    });
                }

                foreach (string line in File.ReadAllLines(menuFile))
                {
                    string[] parts = line.Split(';');
                    if (parts.Length == 3)
                    {
                        menu.Add(new MenuItem
                        {
                            ID = int.Parse(parts[0]),
                            Name = parts[1],
                            Price = decimal.Parse(parts[2])
                        });
                    }
                }
                LogMessage($"Loaded {menu.Count} items from menu");
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading menu: {ex.Message}");
            }
        }

        private async Task StartServerAsync()
        {
            try
            {
                server = new TcpListener(IPAddress.Any, PORT);
                server.Start();
                cancellationTokenSource = new CancellationTokenSource();

                LogMessage("=================================");
                LogMessage($"Server started successfully on port {PORT}");
                LogMessage($"Listening on: {GetLocalIPAddress()}:{PORT}");
                LogMessage("Waiting for clients...");
                LogMessage("=================================");

                // Accept clients asynchronously
                await AcceptClientsAsync(cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR starting server: {ex.Message}");
                MessageBox.Show($"Failed to start server!\n\nError: {ex.Message}",
                    "Server Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task AcceptClientsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await server.AcceptTcpClientAsync();

                    // Handle each client in separate task (fire and forget)
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    break; // Server stopped
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        LogMessage($"Error accepting client: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            NetworkStream stream = null;
            string clientIP = "";
            string clientType = "Unknown";

            try
            {
                stream = client.GetStream();
                clientIP = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                LogMessage($"=== NEW CLIENT CONNECTED: {clientIP} ===");

                byte[] buffer = new byte[4096];

                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead == 0)
                        break; // Client disconnected

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    LogMessage($"[{clientIP}] ← Received: {message}");

                    string response = ProcessCommand(message, ref clientType);

                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);

                    string logResponse = response.Length > 50 ?
                        response.Substring(0, 50) + "..." : response;
                    LogMessage($"[{clientIP}] → Sent: {logResponse}");

                    if (message == "QUIT")
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage($"[{clientIP}] Operation cancelled");
            }
            catch (IOException)
            {
                LogMessage($"[{clientIP}] Connection lost");
            }
            catch (Exception ex)
            {
                LogMessage($"[{clientIP}] ERROR: {ex.Message}");
            }
            finally
            {
                stream?.Close();
                client?.Close();
                LogMessage($"=== CLIENT DISCONNECTED: {clientIP} ({clientType}) ===");
            }
        }

        private string ProcessCommand(string command, ref string clientType)
        {
            string[] parts = command.Split(' ');
            string cmd = parts[0].ToUpper();

            switch (cmd)
            {
                case "AUTH":
                    if (parts.Length > 1)
                        clientType = parts[1];
                    return "OK AUTH";

                case "MENU":
                    return GetMenuString();

                case "ORDER":
                    if (parts.Length >= 4)
                    {
                        int table = int.Parse(parts[1]);
                        int itemId = int.Parse(parts[2]);
                        int quantity = int.Parse(parts[3]);
                        return ProcessOrder(table, itemId, quantity);
                    }
                    return "ERROR Invalid order format";

                case "GET_ORDERS":
                    return GetAllOrders();

                case "PAY":
                    if (parts.Length >= 2)
                    {
                        int table = int.Parse(parts[1]);
                        return ProcessPayment(table);
                    }
                    return "ERROR Invalid payment format";

                case "QUIT":
                    return "BYE";

                default:
                    return "ERROR Unknown command";
            }
        }

        private string GetMenuString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (var item in menu)
            {
                sb.AppendLine($"{item.ID};{item.Name};{item.Price}");
            }
            return sb.ToString();
        }

        private string ProcessOrder(int table, int itemId, int quantity)
        {
            lock (lockObject)
            {
                var menuItem = menu.FirstOrDefault(m => m.ID == itemId);
                if (menuItem == null)
                    return "ERROR Item not found";

                if (!orders.ContainsKey(table))
                    orders[table] = new List<OrderItem>();

                var existingOrder = orders[table].FirstOrDefault(o => o.ItemID == itemId);
                if (existingOrder != null)
                {
                    existingOrder.Quantity += quantity;
                }
                else
                {
                    orders[table].Add(new OrderItem
                    {
                        ItemID = itemId,
                        Name = menuItem.Name,
                        Price = menuItem.Price,
                        Quantity = quantity
                    });
                }

                decimal total = menuItem.Price * quantity;
                return $"OK {total}";
            }
        }

        private string GetAllOrders()
        {
            lock (lockObject)
            {
                if (orders.Count == 0)
                    return "EMPTY";

                StringBuilder sb = new StringBuilder();
                foreach (var table in orders.Keys.OrderBy(k => k))
                {
                    foreach (var order in orders[table])
                    {
                        sb.AppendLine($"{table};{order.ItemID};{order.Name};{order.Quantity};{order.Price};{order.Price * order.Quantity}");
                    }
                }
                return sb.ToString();
            }
        }

        private string ProcessPayment(int table)
        {
            lock (lockObject)
            {
                if (!orders.ContainsKey(table))
                    return "ERROR Table not found";

                decimal total = orders[table].Sum(o => o.Price * o.Quantity);

                StringBuilder bill = new StringBuilder();
                bill.AppendLine($"TABLE {table}");
                foreach (var order in orders[table])
                {
                    bill.AppendLine($"{order.Name};{order.Quantity};{order.Price};{order.Price * order.Quantity}");
                }
                bill.AppendLine($"TOTAL {total}");

                orders.Remove(table);
                return bill.ToString();
            }
        }

        private void LogMessage(string message)
        {
            if (rtbLog.InvokeRequired)
            {
                rtbLog.Invoke(new Action(() => LogMessage(message)));
            }
            else
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                rtbLog.AppendText($"[{timestamp}] {message}\n");
                rtbLog.ScrollToCaret();
            }
        }

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip.ToString();
            }
            return "127.0.0.1";
        }

        private void ServerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            cancellationTokenSource?.Cancel();
            server?.Stop();
        }
    }

    public class MenuItem
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
    }

    public class OrderItem
    {
        public int ItemID { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }
}