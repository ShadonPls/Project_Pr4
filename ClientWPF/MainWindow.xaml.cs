using Common.Ftp;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClientWPF
{
    public partial class MainWindow : Window
    {
        private TcpClient tcpClient;
        private NetworkStream stream;
        private StreamReader reader;
        private StreamWriter writer;
        private int userId = -1;
        private bool isConnected = false;
        private string debugFolder;
        private ServerItem selectedItem;
        private ObservableCollection<ServerItem> serverItems;

        public MainWindow()
        {
            InitializeComponent();
            
            debugFolder = AppDomain.CurrentDomain.BaseDirectory;
            txtDebugFolder.Text = debugFolder;

            serverItems = new ObservableCollection<ServerItem>();
            treeServerFiles.ItemsSource = serverItems;
            treeServerFiles.SelectedItemChanged += TreeServerFiles_SelectedItemChanged;
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!IPAddress.TryParse(txtIpAddress.Text, out IPAddress ipAddress) || 
                !int.TryParse(txtPort.Text, out int port))
            {
                MessageBox.Show("Неверный IP адрес или порт");
                return;
            }

            try
            {
                UpdateStatus("Подключение к серверу...", "#FF9800");

                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(ipAddress, port);

                if (tcpClient.Connected)
                {
                    stream = tcpClient.GetStream();
                    reader = new StreamReader(stream, Encoding.UTF8);
                    writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                    isConnected = true;
                    UpdateStatus("Подключено к серверу", "#4CAF50");

                    UpdateConnectionUI(true);
                }
                else
                {
                    UpdateStatus("Не удалось подключиться", "#F44336");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка подключения: {ex.Message}", "#F44336");
                MessageBox.Show($"Ошибка подключения: {ex.Message}");
            }
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (!isConnected)
            {
                MessageBox.Show("Сначала подключитесь к серверу");
                return;
            }

            string login = txtLogin.Text.Trim();
            string password = txtPassword.Password;

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Введите логин и пароль");
                return;
            }

            try
            {
                UpdateStatus("Авторизация...", "#FF9800");

                string response = await SendCommand($"connect {login} {password}");
                var viewModelResponse = JsonConvert.DeserializeObject<ViewModelMessage>(response);

                if (viewModelResponse.Command == "authorization")
                {
                    userId = int.Parse(viewModelResponse.Data);
                    UpdateStatus($"Авторизован как {login}", "#4CAF50");

                    UpdateLoginUI(true);
                    UpdateFileOperationsUI(true);

                    await LoadServerStructure();
                }
                else
                {
                    UpdateStatus($"Ошибка авторизации: {viewModelResponse.Data}", "#F44336");
                    MessageBox.Show(viewModelResponse.Data);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка авторизации: {ex.Message}", "#F44336");
                MessageBox.Show($"Ошибка авторизации: {ex.Message}");
            }
        }

        private async Task LoadServerStructure()
        {
            if (!isConnected || userId == -1) return;

            try
            {
                UpdateStatus("Загрузка структуры файлов...", "#FF9800");

                Dispatcher.Invoke(() => serverItems.Clear());

                var rootItem = new ServerItem
                {
                    Name = "Корневая папка",
                    FullPath = "",
                    IsDirectory = true,
                    HasChildren = true
                };

                await LoadDirectoryContent(rootItem);

                Dispatcher.Invoke(() =>
                {
                    serverItems.Add(rootItem);
                    txtCurrentPath.Text = "/ (корневая папка)";
                });

                UpdateStatus("Структура файлов загружена", "#4CAF50");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка загрузки структуры: {ex.Message}", "#F44336");
                MessageBox.Show($"Ошибка загрузки структуры файлов: {ex.Message}");
            }
        }

        private async Task LoadDirectoryContent(ServerItem parentItem)
        {
            try
            {
                string response = await SendCommand($"list {parentItem.FullPath}");
                var viewModelResponse = JsonConvert.DeserializeObject<ViewModelMessage>(response);

                if (viewModelResponse.Command == "list")
                {
                    var items = JsonConvert.DeserializeObject<List<FileSystemItem>>(viewModelResponse.Data);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        parentItem.Children.Clear();

                        if (items != null)
                        {
                            AddFoldersToTree(parentItem, items);
                            AddFilesToTree(parentItem, items);
                            parentItem.HasChildren = parentItem.Children.Count > 0;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки директории: {ex.Message}");
            }
        }

        private void AddFoldersToTree(ServerItem parent, List<FileSystemItem> items)
        {
            foreach (var item in items.Where(i => i.Type == "folder"))
            {
                parent.Children.Add(new ServerItem
                {
                    Name = item.Name,
                    FullPath = Path.Combine(parent.FullPath, item.Name).Replace("\\", "/"),
                    IsDirectory = true,
                    Size = "",
                    HasChildren = true
                });
            }
        }

        private void AddFilesToTree(ServerItem parent, List<FileSystemItem> items)
        {
            foreach (var item in items.Where(i => i.Type == "file"))
            {
                parent.Children.Add(new ServerItem
                {
                    Name = item.Name,
                    FullPath = Path.Combine(parent.FullPath, item.Name).Replace("\\", "/"),
                    IsDirectory = false,
                    Size = item.Size,
                    HasChildren = false
                });
            }
        }

        private async void TreeServerFiles_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            selectedItem = e.NewValue as ServerItem;

            if (selectedItem != null)
            {
                UpdateSelectedItemInfo(selectedItem);

                if (selectedItem.IsDirectory && selectedItem.Children.Count == 0 && selectedItem.HasChildren)
                {
                    await LoadDirectoryContent(selectedItem);
                    selectedItem.IsExpanded = true;
                }
            }
        }

        private void UpdateSelectedItemInfo(ServerItem item)
        {
            Dispatcher.Invoke(() =>
            {
                txtSelectedItem.Text = item.IsDirectory ? 
                    $"{item.Name} (папка)" : 
                    $"{item.Name} ({item.Size})";
                    
                txtPathInfo.Text = $"Путь: {item.FullPath}";
                btnGetFile.IsEnabled = !item.IsDirectory;
            });
        }

        private async void BtnGetFile_Click(object sender, RoutedEventArgs e)
        {
            if (selectedItem == null || selectedItem.IsDirectory)
            {
                MessageBox.Show("Выберите файл для скачивания", "Внимание", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                UpdateStatus($"Скачивание файла '{selectedItem.Name}'...", "#FF9800");

                string response = await SendCommand($"get {selectedItem.FullPath}");
                var viewModelResponse = JsonConvert.DeserializeObject<ViewModelMessage>(response);

                if (viewModelResponse.Command == "file")
                {
                    var fileInfo = JsonConvert.DeserializeObject<FileInfoFTP>(viewModelResponse.Data);
                    string localFilePath = GetUniqueFilePath(fileInfo.Name);

                    File.WriteAllBytes(localFilePath, fileInfo.Data);
                    
                    UpdateStatus($"Файл '{fileInfo.Name}' успешно скачан", "#4CAF50");
                    ShowDownloadSuccess(fileInfo.Name, localFilePath);
                }
                else
                {
                    UpdateStatus($"Ошибка: {viewModelResponse.Data}", "#F44336");
                    MessageBox.Show(viewModelResponse.Data, "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка при скачивании: {ex.Message}", "#F44336");
                MessageBox.Show($"Ошибка при скачивании: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnSetFile_Click(object sender, RoutedEventArgs e)
        {
            if (!isConnected || userId == -1)
            {
                MessageBox.Show("Сначала авторизуйтесь", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите файл для загрузки",
                Filter = "Все файлы (*.*)|*.*",
                Multiselect = false
            };

            if (openDialog.ShowDialog() != true) return;

            try
            {
                UpdateStatus($"Загрузка файла '{Path.GetFileName(openDialog.FileName)}'...", "#FF9800");

                byte[] fileBytes = File.ReadAllBytes(openDialog.FileName);
                var fileInfo = new FileInfoFTP(fileBytes, Path.GetFileName(openDialog.FileName));
                
                string response = await SendCommand($"set {JsonConvert.SerializeObject(fileInfo)}");
                var viewModelResponse = JsonConvert.DeserializeObject<ViewModelMessage>(response);

                if (viewModelResponse.Command == "success")
                {
                    UpdateStatus(viewModelResponse.Data, "#4CAF50");
                    MessageBox.Show(viewModelResponse.Data, "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    await LoadServerStructure();
                }
                else
                {
                    UpdateStatus($"Ошибка: {viewModelResponse.Data}", "#F44336");
                    MessageBox.Show(viewModelResponse.Data, "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка при загрузке: {ex.Message}", "#F44336");
                MessageBox.Show($"Ошибка при загрузке: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadServerStructure();
        }

        private async Task<string> SendCommand(string message)
        {
            var viewModel = new ViewModelSend(message, userId);
            string json = JsonConvert.SerializeObject(viewModel);

            await writer.WriteLineAsync(json);
            await writer.FlushAsync();

            return await reader.ReadLineAsync();
        }

        private string GetUniqueFilePath(string fileName)
        {
            string localFilePath = Path.Combine(debugFolder, fileName);

            if (File.Exists(localFilePath))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                localFilePath = Path.Combine(debugFolder, $"{nameWithoutExt}_{timestamp}{extension}");
            }

            return localFilePath;
        }

        private void ShowDownloadSuccess(string fileName, string filePath)
        {
            var result = MessageBox.Show(
                $"Файл '{fileName}' успешно скачан!\n\nПуть: {filePath}\n\nОткрыть папку с файлом?",
                "Файл скачан",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
        }

        private void UpdateStatus(string message, string color = "#2196F3")
        {
            Dispatcher.Invoke(() =>
            {
                txtStatus.Text = message;
                statusIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            });
        }

        private void UpdateConnectionUI(bool isConnected)
        {
            Dispatcher.Invoke(() =>
            {
                btnConnect.IsEnabled = !isConnected;
                btnLogin.IsEnabled = isConnected;
                txtIpAddress.IsEnabled = !isConnected;
                txtPort.IsEnabled = !isConnected;
                statusIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isConnected ? "#4CAF50" : "#F44336"));
            });
        }

        private void UpdateLoginUI(bool isLoggedIn)
        {
            Dispatcher.Invoke(() =>
            {
                btnLogin.IsEnabled = !isLoggedIn;
                txtLogin.IsEnabled = !isLoggedIn;
                txtPassword.IsEnabled = !isLoggedIn;
            });
        }

        private void UpdateFileOperationsUI(bool enable)
        {
            Dispatcher.Invoke(() =>
            {
                btnGetFile.IsEnabled = enable;
                btnSetFile.IsEnabled = enable;
                btnRefresh.IsEnabled = enable;
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            try
            {
                if (tcpClient?.Connected == true)
                {
                    writer?.Close();
                    reader?.Close();
                    stream?.Close();
                    tcpClient.Close();
                }
            }
            catch { }
        }
    }
}