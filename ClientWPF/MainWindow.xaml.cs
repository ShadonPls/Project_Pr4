using Common.Ftp;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

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

        public MainWindow()
        {
            InitializeComponent();

            // Получаем путь к папке Debug WPF клиента
            debugFolder = AppDomain.CurrentDomain.BaseDirectory;
            txtLog.Text = $"Папка Debug: {debugFolder}";
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IPAddress.TryParse(txtIpAddress.Text, out IPAddress ipAddress) &&
                    int.TryParse(txtPort.Text, out int port))
                {
                    UpdateLog("Подключение к серверу...");

                    tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync(ipAddress, port);

                    if (tcpClient.Connected)
                    {
                        stream = tcpClient.GetStream();
                        reader = new StreamReader(stream, Encoding.UTF8);
                        writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                        isConnected = true;
                        UpdateStatus("Подключено");
                        UpdateLog($"Подключено к {ipAddress}:{port}");

                        btnConnect.IsEnabled = false;
                        btnLogin.IsEnabled = true;
                        txtIpAddress.IsEnabled = false;
                        txtPort.IsEnabled = false;
                    }
                    else
                    {
                        UpdateLog("Не удалось подключиться к серверу");
                        MessageBox.Show("Не удалось подключиться к серверу");
                    }
                }
                else
                {
                    MessageBox.Show("Неверный IP адрес или порт");
                }
            }
            catch (Exception ex)
            {
                UpdateLog($"Ошибка подключения: {ex.Message}");
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

            try
            {
                string login = txtLogin.Text.Trim();
                string password = txtPassword.Password;

                if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                {
                    MessageBox.Show("Введите логин и пароль");
                    return;
                }

                UpdateLog("Авторизация...");

                string message = $"connect {login} {password}";
                var viewModel = new ViewModelSend(message, -1);
                string json = JsonConvert.SerializeObject(viewModel);

                // Отправляем запрос
                await writer.WriteLineAsync(json);
                await writer.FlushAsync();

                // Получаем ответ
                string responseJson = await reader.ReadLineAsync();

                var response = JsonConvert.DeserializeObject<ViewModelMessage>(responseJson);

                if (response.Command == "authorization")
                {
                    userId = int.Parse(response.Data);
                    UpdateStatus($"Авторизован как {login}");
                    UpdateLog($"Успешная авторизация: {login}");

                    btnLogin.IsEnabled = false;
                    txtLogin.IsEnabled = false;
                    txtPassword.IsEnabled = false;

                    // Активируем кнопки управления
                    btnGetFile.IsEnabled = true;
                    btnSetFile.IsEnabled = true;
                    btnRefreshList.IsEnabled = true;
                    cmbServerFiles.IsEnabled = true;

                    // Загружаем список файлов с сервера
                    await LoadServerFilesList();
                }
                else
                {
                    UpdateLog($"Ошибка авторизации: {response.Data}");
                    MessageBox.Show(response.Data);
                }
            }
            catch (Exception ex)
            {
                UpdateLog($"Ошибка авторизации: {ex.Message}");
                MessageBox.Show($"Ошибка авторизации: {ex.Message}");
            }
        }

        private async Task LoadServerFilesList()
        {
            if (!isConnected || userId == -1) return;

            try
            {
                UpdateLog("Загрузка списка файлов с сервера...");

                var viewModel = new ViewModelSend("list", userId);
                string json = JsonConvert.SerializeObject(viewModel);

                // Отправляем запрос
                await writer.WriteLineAsync(json);
                await writer.FlushAsync();

                // Получаем ответ
                string responseJson = await reader.ReadLineAsync();

                var response = JsonConvert.DeserializeObject<ViewModelMessage>(responseJson);

                if (response.Command == "list")
                {
                    var files = JsonConvert.DeserializeObject<List<string>>(response.Data);
                    UpdateComboBox(files);
                    UpdateLog($"Загружено {files.Count} файлов с сервера");
                }
                else
                {
                    UpdateLog($"Ошибка: {response.Data}");
                    MessageBox.Show(response.Data);
                }
            }
            catch (Exception ex)
            {
                UpdateLog($"Ошибка загрузки списка: {ex.Message}");
                MessageBox.Show($"Ошибка загрузки списка файлов: {ex.Message}");
            }
        }

        private void UpdateComboBox(List<string> files)
        {
            Dispatcher.Invoke(() =>
            {
                cmbServerFiles.Items.Clear();
                if (files != null && files.Any())
                {
                    foreach (var file in files)
                    {
                        cmbServerFiles.Items.Add(file);
                    }
                    cmbServerFiles.SelectedIndex = 0;
                }
                else
                {
                    cmbServerFiles.Items.Add("Нет файлов на сервере");
                }
            });
        }

        private async void BtnGetFile_Click(object sender, RoutedEventArgs e)
        {
            if (!isConnected || userId == -1)
            {
                MessageBox.Show("Сначала авторизуйтесь");
                return;
            }

            if (cmbServerFiles.SelectedItem == null ||
                cmbServerFiles.SelectedItem.ToString() == "Нет файлов на сервере")
            {
                MessageBox.Show("Выберите файл из списка");
                return;
            }

            string fileName = cmbServerFiles.SelectedItem.ToString();

            try
            {
                UpdateLog($"Запрос файла '{fileName}' с сервера...");

                // Отправляем команду GET
                var viewModel = new ViewModelSend($"get {fileName}", userId);
                string json = JsonConvert.SerializeObject(viewModel);

                await writer.WriteLineAsync(json);
                await writer.FlushAsync();

                // Получаем ответ
                string responseJson = await reader.ReadLineAsync();

                var response = JsonConvert.DeserializeObject<ViewModelMessage>(responseJson);

                if (response.Command == "file")
                {
                    var fileInfo = JsonConvert.DeserializeObject<FileInfoFTP>(response.Data);

                    // Сохраняем файл в папку Debug WPF клиента
                    string localFilePath = Path.Combine(debugFolder, fileInfo.Name);

                    // Если файл уже существует, добавляем timestamp
                    if (File.Exists(localFilePath))
                    {
                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileInfo.Name);
                        string extension = Path.GetExtension(fileInfo.Name);
                        localFilePath = Path.Combine(debugFolder, $"{nameWithoutExt}_{timestamp}{extension}");
                    }

                    File.WriteAllBytes(localFilePath, fileInfo.Data);

                    UpdateLog($"Файл '{fileInfo.Name}' сохранен в: {localFilePath}");
                    MessageBox.Show($"Файл '{fileInfo.Name}' успешно скачан!\nПуть: {localFilePath}");
                }
                else
                {
                    UpdateLog($"Ошибка: {response.Data}");
                    MessageBox.Show(response.Data);
                }
            }
            catch (Exception ex)
            {
                UpdateLog($"Ошибка при скачивании файла: {ex.Message}");
                MessageBox.Show($"Ошибка при скачивании файла: {ex.Message}");
            }
        }

        private async void BtnSetFile_Click(object sender, RoutedEventArgs e)
        {
            if (!isConnected || userId == -1)
            {
                MessageBox.Show("Сначала авторизуйтесь");
                return;
            }

            try
            {
                // Открываем диалог выбора файла
                var openDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Выберите файл для загрузки на сервер",
                    Filter = "Все файлы (*.*)|*.*",
                    Multiselect = false
                };

                if (openDialog.ShowDialog() == true)
                {
                    UpdateLog($"Подготовка файла '{Path.GetFileName(openDialog.FileName)}'...");

                    // Читаем файл с диска
                    byte[] fileBytes = File.ReadAllBytes(openDialog.FileName);
                    string fileName = Path.GetFileName(openDialog.FileName);

                    // Создаем объект файла
                    var fileInfo = new FileInfoFTP(fileBytes, fileName);
                    string fileInfoJson = JsonConvert.SerializeObject(fileInfo);

                    // Отправляем команду SET
                    UpdateLog($"Отправка файла '{fileName}' на сервер...");

                    var viewModel = new ViewModelSend($"set {fileInfoJson}", userId);
                    string json = JsonConvert.SerializeObject(viewModel);

                    await writer.WriteLineAsync(json);
                    await writer.FlushAsync();

                    // Получаем ответ
                    string responseJson = await reader.ReadLineAsync();

                    var response = JsonConvert.DeserializeObject<ViewModelMessage>(responseJson);

                    UpdateLog(response.Data);
                    MessageBox.Show(response.Data);

                    // Обновляем список файлов
                    await LoadServerFilesList();
                }
            }
            catch (Exception ex)
            {
                UpdateLog($"Ошибка при загрузке файла: {ex.Message}");
                MessageBox.Show($"Ошибка при загрузке файла: {ex.Message}");
            }
        }

        private async void BtnRefreshList_Click(object sender, RoutedEventArgs e)
        {
            await LoadServerFilesList();
        }

        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtStatus.Text = $"Статус: {message}";
            });
        }

        private void UpdateLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.Text = message;
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            try
            {
                if (tcpClient != null && tcpClient.Connected)
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