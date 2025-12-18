using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LABTHLTM
{
    public partial class CustomerForm : Form
    {
        private TcpClient client;
        private NetworkStream stream;
        private bool isConnected;
        private List<MenuItemView> menuItems;

        public CustomerForm()
        {
            InitializeComponent();
            menuItems = new List<MenuItemView>();
        }

        private void CustomerForm_Load(object sender, EventArgs e)
        {
            dgvMenu.AllowUserToAddRows = false;
            dgvMenu.ReadOnly = false;
            dgvMenu.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            numTableNumber.Minimum = 1;
            numTableNumber.Maximum = 50;
            numTableNumber.Value = 1;
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
            try
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
                Application.DoEvents();

                client = new TcpClient();

                // Connect with timeout
                await client.ConnectAsync(txtServerIP.Text, port);
                stream = client.GetStream();
                isConnected = true;

                // Authenticate as customer
                await SendMessageAsync("AUTH CUSTOMER");
                string authResponse = await ReceiveMessageAsync();

                // Load menu
                await SendMessageAsync("MENU");
                string menuData = await ReceiveMessageAsync();
                LoadMenu(menuData);

                btnConnect.Text = "Disconnect";
                btnConnect.BackColor = Color.FromArgb(231, 76, 60);
                btnConnect.Enabled = true;
                lblStatus.Text = "Status: Connected";
                lblStatus.ForeColor = Color.Green;

                MessageBox.Show($"Connected to {txtServerIP.Text}:{port} successfully!",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (SocketException sockEx)
            {
                lblStatus.Text = "Status: Connection Failed";
                lblStatus.ForeColor = Color.Red;
                btnConnect.Enabled = true;

                string errorMsg = "Cannot connect to server!\n\n";
                errorMsg += "Possible causes:\n";
                errorMsg += "• Server is not running\n";
                errorMsg += $"• Wrong IP address ({txtServerIP.Text})\n";
                errorMsg += $"• Wrong port ({txtPort.Text})\n";
                errorMsg += "• Firewall blocking connection\n\n";
                errorMsg += $"Technical error: {sockEx.Message}";

                MessageBox.Show(errorMsg, "Connection Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Status: Error";
                lblStatus.ForeColor = Color.Red;
                btnConnect.Enabled = true;

                MessageBox.Show($"Connection error!\n\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadMenu(string menuData)
        {
            menuItems.Clear();
            string[] lines = menuData.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string[] parts = line.Split(';');
                if (parts.Length == 3)
                {
                    menuItems.Add(new MenuItemView
                    {
                        ID = int.Parse(parts[0]),
                        Name = parts[1],
                        Price = decimal.Parse(parts[2]),
                        Quantity = 0
                    });
                }
            }

            dgvMenu.DataSource = null;
            dgvMenu.DataSource = menuItems;
            dgvMenu.Columns["ID"].ReadOnly = true;
            dgvMenu.Columns["Name"].ReadOnly = true;
            dgvMenu.Columns["Price"].ReadOnly = true;
            dgvMenu.Columns["Price"].DefaultCellStyle.Format = "N0";
            dgvMenu.Columns["Quantity"].ReadOnly = false;
        }

        private async void btnPlaceOrder_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                MessageBox.Show("Please connect to server first!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await PlaceOrderAsync();
        }

        private async Task PlaceOrderAsync()
        {
            int tableNumber = (int)numTableNumber.Value;
            decimal totalAmount = 0;
            int itemsOrdered = 0;

            btnPlaceOrder.Enabled = false;

            try
            {
                foreach (var item in menuItems)
                {
                    if (item.Quantity > 0)
                    {
                        string orderMsg = $"ORDER {tableNumber} {item.ID} {item.Quantity}";
                        await SendMessageAsync(orderMsg);
                        string response = await ReceiveMessageAsync();

                        if (response.StartsWith("OK"))
                        {
                            string[] parts = response.Split(' ');
                            if (parts.Length > 1)
                            {
                                totalAmount += decimal.Parse(parts[1]);
                                itemsOrdered++;
                            }
                        }
                    }
                }

                if (itemsOrdered > 0)
                {
                    MessageBox.Show($"Order placed successfully!\n\nTable: {tableNumber}\nTotal Amount: {totalAmount:N0} VND",
                        "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    // Reset quantities
                    foreach (var item in menuItems)
                        item.Quantity = 0;
                    dgvMenu.Refresh();
                }
                else
                {
                    MessageBox.Show("Please select at least one item with quantity > 0!", "Warning",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error placing order: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnPlaceOrder.Enabled = true;
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
                if (isConnected)
                {
                    await SendMessageAsync("QUIT");
                    await Task.Delay(100); // Give time for message to send
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

        private async void CustomerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            await DisconnectAsync();
        }
    }

    public class MenuItemView
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }
}