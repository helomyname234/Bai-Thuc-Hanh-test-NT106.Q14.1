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

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                try
                {
                    client = new TcpClient(txtServerIP.Text, int.Parse(txtPort.Text));
                    stream = client.GetStream();
                    isConnected = true;

                    // Authenticate as customer
                    SendMessage("AUTH CUSTOMER");
                    string authResponse = ReceiveMessage();

                    // Load menu
                    SendMessage("MENU");
                    string menuData = ReceiveMessage();
                    LoadMenu(menuData);

                    btnConnect.Text = "Disconnect";
                    btnConnect.BackColor = Color.FromArgb(231, 76, 60);
                    lblStatus.Text = "Status: Connected";
                    lblStatus.ForeColor = Color.Green;
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

        private void btnPlaceOrder_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                MessageBox.Show("Please connect to server first!", "Warning",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int tableNumber = (int)numTableNumber.Value;
            decimal totalAmount = 0;
            int itemsOrdered = 0;

            foreach (var item in menuItems)
            {
                if (item.Quantity > 0)
                {
                    try
                    {
                        string orderMsg = $"ORDER {tableNumber} {item.ID} {item.Quantity}";
                        SendMessage(orderMsg);
                        string response = ReceiveMessage();

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
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error ordering {item.Name}: {ex.Message}", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            if (itemsOrdered > 0)
            {
                MessageBox.Show($"Order placed successfully!\nTable: {tableNumber}\nTotal Amount: {totalAmount:N0} VND",
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

        private void CustomerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Disconnect();
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