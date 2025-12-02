using Common.Ftp;
using Newtonsoft.Json;
using Server.Data;
using Server.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public class Program
    {
        private static DBContext dbContext = new DBContext();
        private static Dictionary<int, string> userCurrentPaths = new Dictionary<int, string>();

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var ipAddress = GetServerIp();
            var port = GetServerPort();

            StartServer(ipAddress, port);

            Console.ReadKey();
        }

        private static IPAddress GetServerIp()
        {
            Console.Write("Введите IP сервера: ");

            string input = Console.ReadLine();

            return IPAddress.Parse("127.0.0.1");
        }

        private static int GetServerPort()
        {
            Console.Write("Введите порт (по умолчанию 8888): ");
            string input = Console.ReadLine();

            if (int.TryParse(input, out int port) && port > 0 && port < 65536)
                return port;
            return -1;
        }

        private static void CreateTestFiles(string directory)
        {
            string[] testFiles = {
                "readme.txt",
                "test_document.docx",
                "image.jpg",
                "data.csv"
            };

            foreach (var fileName in testFiles)
            {
                string filePath = Path.Combine(directory, fileName);
                if (!File.Exists(filePath))
                {
                    File.WriteAllText(filePath, $"Тестовый файл: {fileName}\nСоздан: {DateTime.Now}");
                }
            }
        }

        static void StartServer(IPAddress ipAddress, int port)
        {
            var endPoint = new IPEndPoint(ipAddress, port);
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(endPoint);
                listener.Listen(10);

                Console.WriteLine($"Сервер запущен. Ожидание подключений...\n");
                while (true)
                {
                    var clientSocket = listener.Accept();
                    Task.Run(() => HandleClient(clientSocket));
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Ошибка сокета: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка запуска сервера: {ex.Message}");
            }
        }

        static async void HandleClient(Socket clientSocket)
        {
            string clientInfo = clientSocket.RemoteEndPoint.ToString();

            try
            {
                using (var stream = new NetworkStream(clientSocket))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                {
                    while (clientSocket.Connected)
                    {
                        string jsonData = await reader.ReadLineAsync();
                        if (string.IsNullOrEmpty(jsonData))
                            break;

                        string response = ProcessCommand(jsonData, clientInfo);
                        await writer.WriteLineAsync(response);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Close();
                }
                catch { }
            }
        }

        static string ProcessCommand(string jsonData, string clientInfo)
        {
            try
            {
                var request = JsonConvert.DeserializeObject<ViewModelSend>(jsonData);
                if (request == null)
                    return CreateErrorResponse("Неверный формат запроса");

                string[] parts = request.Message.Split(new char[] { ' ' }, 2);
                string command = parts[0].ToLower();
                string arguments = parts.Length > 1 ? parts[1] : "";

                return command switch
                {
                    "connect" => HandleConnect(arguments, clientInfo, request.Id),
                    "get" => HandleGet(request.Id, arguments),
                    "set" => HandleSet(request.Id, arguments),
                    "list" => HandleList(request.Id, arguments),
                    _ => CreateErrorResponse($"Неизвестная команда: {command}")
                };
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Ошибка сервера: {ex.Message}");
            }
        }

        static string HandleConnect(string arguments, string clientInfo, int userId)
        {
            string[] credentials = arguments.Split(' ');
            if (credentials.Length != 2)
                return CreateErrorResponse("Неверный формат. Используйте: connect логин пароль");

            var user = dbContext.Users.FirstOrDefault(u =>
                u.Login == credentials[0] && u.Password == credentials[1]);

            if (user != null)
            {
                userCurrentPaths[user.Id] = user.Src;
                LogCommand(user.Id, $"Подключение с {clientInfo}");
                return JsonConvert.SerializeObject(new ViewModelMessage("authorization", user.Id.ToString()));
            }

            return CreateErrorResponse("Неверный логин или пароль");
        }

        static string HandleGet(int userId, string fileName)
        {
            if (!userCurrentPaths.ContainsKey(userId))
                return CreateErrorResponse("Пользователь не авторизован");

            string filePath = Path.Combine(userCurrentPaths[userId], fileName);

            if (!File.Exists(filePath))
                return CreateErrorResponse($"Файл '{fileName}' не найден");

            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                var fileInfo = new FileInfoFTP(fileBytes, Path.GetFileName(filePath));

                LogCommand(userId, $"get {fileName}");
                return JsonConvert.SerializeObject(new ViewModelMessage("file", JsonConvert.SerializeObject(fileInfo)));
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Ошибка чтения файла: {ex.Message}");
            }
        }

        static string HandleSet(int userId, string jsonData)
        {
            if (!userCurrentPaths.ContainsKey(userId))
                return CreateErrorResponse("Пользователь не авторизован");

            try
            {
                // Разделяем данные: сначала путь, потом файл
                string[] parts = jsonData.Split(new string[] { "|||" }, 2, StringSplitOptions.None);
                if (parts.Length != 2)
                    return CreateErrorResponse("Неверный формат данных");

                string targetPath = parts[0];
                string fileJson = parts[1];

                var fileInfo = JsonConvert.DeserializeObject<FileInfoFTP>(fileJson);
                if (fileInfo == null)
                    return CreateErrorResponse("Неверный формат файла");

                // Базовый путь пользователя
                string basePath = userCurrentPaths[userId];

                // Формируем полный путь
                string fullPath;
                if (string.IsNullOrEmpty(targetPath))
                {
                    // Корневая папка
                    fullPath = Path.Combine(basePath, fileInfo.Name);
                }
                else
                {
                    // Папка внутри пользовательской директории
                    fullPath = Path.Combine(basePath, targetPath.TrimStart('/'), fileInfo.Name);

                    // Создаем папки, если их нет
                    string directory = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }

                File.WriteAllBytes(fullPath, fileInfo.Data);

                LogCommand(userId, $"set {targetPath}/{fileInfo.Name}");
                return CreateSuccessResponse($"Файл '{fileInfo.Name}' успешно загружен");
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Ошибка загрузки файла: {ex.Message}");
            }
        }

        static string HandleList(int userId, string path = "")
        {
            if (!userCurrentPaths.ContainsKey(userId))
                return CreateErrorResponse("Пользователь не авторизован");

            string currentPath = userCurrentPaths[userId];

            if (!string.IsNullOrEmpty(path) && path != "/")
                currentPath = Path.Combine(currentPath, path.TrimStart('/'));

            try
            {
                var items = GetDirectoryStructure(currentPath);
                LogCommand(userId, $"list {path}");
                return JsonConvert.SerializeObject(new ViewModelMessage("list", JsonConvert.SerializeObject(items)));
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Ошибка чтения директории: {ex.Message}");
            }
        }

        private static List<FileSystemItem> GetDirectoryStructure(string path)
        {
            var items = new List<FileSystemItem>();

            foreach (var dir in Directory.GetDirectories(path))
            {
                var dirInfo = new DirectoryInfo(dir);
                items.Add(new FileSystemItem
                {
                    Name = dirInfo.Name,
                    Type = "folder",
                    Size = "",
                    LastModified = dirInfo.LastWriteTime
                });
            }

            foreach (var file in Directory.GetFiles(path))
            {
                var fileInfo = new FileInfo(file);
                items.Add(new FileSystemItem
                {
                    Name = fileInfo.Name,
                    Type = "file",
                    Size = FormatFileSize(fileInfo.Length),
                    LastModified = fileInfo.LastWriteTime
                });
            }

            return items;
        }

        static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        static void LogCommand(int userId, string command)
        {
            try
            {
                dbContext.UsersLog.Add(new UserLog
                {
                    UserId = userId,
                    Command = command,
                    Date = DateTime.Now
                });
                dbContext.SaveChanges();
            }
            catch { }
        }

        private static string CreateErrorResponse(string message) =>
            JsonConvert.SerializeObject(new ViewModelMessage("error", message));

        private static string CreateSuccessResponse(string message) =>
            JsonConvert.SerializeObject(new ViewModelMessage("success", message));
    }
}