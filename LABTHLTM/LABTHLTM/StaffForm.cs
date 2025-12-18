using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
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
            refreshTimer.Tick += RefreshTimer_Tick;
        }

        private void StaffForm_Load(object sender, EventArgs e)
        {
            dgvOrders.AllowUserToAddRows = false;
            dgvOrders.ReadOnly = true;
            dgvOrders.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                try
                {
                    client = new TcpClient(txtServerIP.Text, int.Parse(txtPort.Text));
                    stream = client.GetStream();
                    isConnected = true;

                    // Authenticate as staff
                    SendMessage("AUTH STAFF");
                    string authResponse = ReceiveMessage();

                    btnConnect.Text = "Disconnect";
                    btnConnect.BackColor = Color.FromArgb(231, 76, 60);
                    lblStatus.Text = "Status: Connected";
                    lblStatus.ForeColor = Color.Green;

                    // Start auto-refresh
                    refreshTimer.Start();
                    RefreshOrders();

                    MessageBox.Show("Connected to server successfully!", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Connection failed: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                Disconnect();
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (isConnected)
                RefreshOrders();
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                MessageBox.Show("Please connect to server first!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RefreshOrders();
        }

        private void RefreshOrders()
        {
            try
            {
                SendMessage("GET_ORDERS");
                string orderData = ReceiveMessage();
                LoadOrders(orderData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing orders: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadOrders(string orderData)
        {
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

        private void btnCharge_Click(object sender, EventArgs e)
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

            try
            {
                int tableNumber = int.Parse(txtTableNumber.Text);
                SendMessage($"PAY {tableNumber}");
                string response = ReceiveMessage();

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
                File.WriteAllText(fileName, billText.ToString());

                MessageBox.Show($"Payment processed successfully!\nTotal: {total:N0} VND\n\nBill saved to: {fileName}",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                RefreshOrders();
                txtTableNumber.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing payment: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SendMessage(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            stream.Write(data, 0, data.Length);
        }

        private string ReceiveMessage()
        {
            byte[] buffer = new byte[4096];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }

        private void Disconnect()
        {
            try
            {
                refreshTimer.Stop();
                if (isConnected)
                {
                    SendMessage("QUIT");
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

        private void StaffForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Disconnect();
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