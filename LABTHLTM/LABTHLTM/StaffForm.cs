using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LABTHLTM
{
    public partial class StaffForm : Form
    {
        private TcpClient client;
        private NetworkStream stream;
        private bool isConnected;
        private List<OrderView> orders;
        private System.Windows.Forms.Timer refreshTimer;

        public StaffForm()
        {
            InitializeComponent();
            orders = new List<OrderView>();

            // Auto-refresh timer
            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 3000; // 3 seconds
            refreshTimer.Tick += async (s, e) => await RefreshOrdersAsync();
        }

        private void StaffForm_Load(object sender, EventArgs e)
        {
            dgvOrders.AllowUserToAddRows = false;
            dgvOrders.ReadOnly = true;
            dgvOrders.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                await ConnectToServerAsync();
            }
            else
            {
                await DisconnectAsync();
            }
        }

        private async Task ConnectToServerAsync()
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(txtServerIP.Text))
            {
                MessageBox.Show("Please enter Server IP!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(txtPort.Text, out int port))
            {
                MessageBox.Show("Invalid port number!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            lblStatus.Text = "Status: Connecting...";
            lblStatus.ForeColor = Color.Orange;
            btnConnect.Enabled = false;

            try
            {
                client = new TcpClient();
                await client.ConnectAsync(txtServerIP.Text, port);
                stream = client.GetStream();
                isConnected = true;

                // Authenticate as staff
                await SendMessageAsync("AUTH STAFF");
                string authResponse = await ReceiveMessageAsync();

                btnConnect.Text = "Disconnect";
                btnConnect.BackColor = Color.FromArgb(231, 76, 60);
                lblStatus.Text = "Status: Connected";
                lblStatus.ForeColor = Color.Green;

                // Start auto-refresh
                refreshTimer.Start();
                await RefreshOrdersAsync();

                MessageBox.Show($"Connected to {txtServerIP.Text}:{port} successfully!", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (SocketException sockEx)
            {
                lblStatus.Text = "Status: Connection Failed";
                lblStatus.ForeColor = Color.Red;

                string errorMsg = "Cannot connect to server!\n\n";
                errorMsg += "Possible causes:\n";
                errorMsg += "• Server is not running\n";
                errorMsg += $"• Wrong IP: {txtServerIP.Text}\n";
                errorMsg += $"• Wrong port: {txtPort.Text}\n";
                errorMsg += "• Firewall blocking\n\n";
                errorMsg += $"Error: {sockEx.Message}";

                MessageBox.Show(errorMsg, "Connection Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Status: Error";
                lblStatus.ForeColor = Color.Red;
                MessageBox.Show($"Connection error!\n\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }

        private async void btnRefresh_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                MessageBox.Show("Please connect to server first!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            await RefreshOrdersAsync();
        }

        private async Task RefreshOrdersAsync()
        {
            if (!isConnected)
                return;

            try
            {
                await SendMessageAsync("GET_ORDERS");
                string orderData = await ReceiveMessageAsync();
                LoadOrders(orderData);
            }
            catch
            {
                // Silently fail on refresh errors
            }
        }

        private void LoadOrders(string orderData)
        {
            if (dgvOrders.InvokeRequired)
            {
                dgvOrders.Invoke(new Action(() => LoadOrders(orderData)));
                return;
            }

            orders.Clear();

            if (orderData == "EMPTY")
            {
                dgvOrders.DataSource = null;
                return;
            }

            string[] lines = orderData.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string[] parts = line.Split(';');
                if (parts.Length == 6)
                {
                    orders.Add(new OrderView
                    {
                        Table = int.Parse(parts[0]),
                        ItemID = int.Parse(parts[1]),
                        ItemName = parts[2],
                        Quantity = int.Parse(parts[3]),
                        Price = decimal.Parse(parts[4]),
                        Total = decimal.Parse(parts[5])
                    });
                }
            }

            dgvOrders.DataSource = null;
            dgvOrders.DataSource = orders;

            if (dgvOrders.Columns.Count > 0)
            {
                dgvOrders.Columns["ItemID"].Visible = false;
                dgvOrders.Columns["Price"].DefaultCellStyle.Format = "N0";
                dgvOrders.Columns["Total"].DefaultCellStyle.Format = "N0";
            }
        }

        private async void btnCharge_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                MessageBox.Show("Please connect to server first!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtTableNumber.Text))
            {
                MessageBox.Show("Please enter table number!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await ProcessPaymentAsync();
        }

        private async Task ProcessPaymentAsync()
        {
            btnCharge.Enabled = false;

            try
            {
                int tableNumber = int.Parse(txtTableNumber.Text);
                await SendMessageAsync($"PAY {tableNumber}");
                string response = await ReceiveMessageAsync();

                if (response.StartsWith("ERROR"))
                {
                    MessageBox.Show(response, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Parse bill details
                string[] lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                decimal total = 0;
                StringBuilder billText = new StringBuilder();

                billText.AppendLine("=================================");
                billText.AppendLine("     RESTAURANT BILL");
                billText.AppendLine("=================================");
                billText.AppendLine($"Date: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                billText.AppendLine();

                foreach (string line in lines)
                {
                    if (line.StartsWith("TABLE"))
                    {
                        billText.AppendLine($"Table Number: {tableNumber}");
                        billText.AppendLine("---------------------------------");
                    }
                    else if (line.StartsWith("TOTAL"))
                    {
                        total = decimal.Parse(line.Split(' ')[1]);
                    }
                    else
                    {
                        string[] parts = line.Split(';');
                        if (parts.Length == 4)
                        {
                            string name = parts[0];
                            int qty = int.Parse(parts[1]);
                            decimal price = decimal.Parse(parts[2]);
                            decimal itemTotal = decimal.Parse(parts[3]);

                            billText.AppendLine($"{name}");
                            billText.AppendLine($"  {qty} x {price:N0} = {itemTotal:N0} VND");
                        }
                    }
                }

                billText.AppendLine("---------------------------------");
                billText.AppendLine($"TOTAL: {total:N0} VND");
                billText.AppendLine("=================================");
                billText.AppendLine("Thank you for dining with us!");
                billText.AppendLine("=================================");

                lblTotalAmount.Text = $"{total:N0} VND";
                lblTotalAmount.ForeColor = Color.Green;

                // Save to file
                string fileName = $"bill_Table{tableNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                await Task.Run(() => File.WriteAllText(fileName, billText.ToString()));

                MessageBox.Show($"Payment processed successfully!\n\nTotal: {total:N0} VND\n\nBill saved to: {fileName}",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                await RefreshOrdersAsync();
                txtTableNumber.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing payment: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnCharge.Enabled = true;
            }
        }

        private async Task SendMessageAsync(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(data, 0, data.Length);
        }

        private async Task<string> ReceiveMessageAsync()
        {
            byte[] buffer = new byte[4096];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }

        private async Task DisconnectAsync()
        {
            try
            {
                refreshTimer.Stop();
                if (isConnected)
                {
                    await SendMessageAsync("QUIT");
                    await Task.Delay(100);
                    stream?.Close();
                    client?.Close();
                }
            }
            catch { }
            finally
            {
                isConnected = false;
                btnConnect.Text = "Connect";
                btnConnect.BackColor = Color.FromArgb(46, 204, 113);
                lblStatus.Text = "Status: Disconnected";
                lblStatus.ForeColor = Color.Red;
            }
        }

        private async void StaffForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            await DisconnectAsync();
        }
    }

    public class OrderView
    {
        public int Table { get; set; }
        public int ItemID { get; set; }
        public string ItemName { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Total { get; set; }
    }
}