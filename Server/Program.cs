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
            InitializeDatabase();
            Console.Write("Введите порт (по умолчанию 8888): ");
            string sPort = Console.ReadLine();
            if (string.IsNullOrEmpty(sPort))
                sPort = "5000";

            if (int.TryParse(sPort, out int port))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Запуск сервера на порту {port}...");
                Console.ResetColor();
                StartServer(port);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Неверный порт. Завершение работы.");
                Console.ResetColor();
                Console.ReadKey();
            }
        }

        private static void InitializeDatabase()
        {
            try
            {
                if (!dbContext.Users.Any())
                {
                    Console.WriteLine("Инициализация базы данных...");

                    var user = new User
                    {
                        Login = "Horoshev",
                        Password = "Asdfg123",
                        Src = @"C:\FTP_Server"
                    };

                    dbContext.Users.Add(user);
                    dbContext.SaveChanges();

                    // Создаем корневую директорию для пользователя
                    if (!Directory.Exists(user.Src))
                    {
                        Directory.CreateDirectory(user.Src);
                        Console.WriteLine($"Создана корневая директория: {user.Src}");

                        // Создаем тестовые файлы
                        CreateTestFiles(user.Src);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка инициализации БД: {ex.Message}");
            }
        }

        private static void CreateTestFiles(string directory)
        {
            try
            {
                // Создаем несколько тестовых файлов
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
                        Console.WriteLine($"Создан тестовый файл: {fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка создания тестовых файлов: {ex.Message}");
            }
        }

        static void StartServer(int port)
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, port);
            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(endPoint);
                listener.Listen(10);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Сервер запущен и слушает порт {port}");
                Console.WriteLine("Ожидание подключений...");
                Console.ResetColor();

                while (true)
                {
                    Socket clientSocket = listener.Accept();
                    string clientInfo = clientSocket.RemoteEndPoint.ToString();
                    Console.WriteLine($"Новое подключение от {clientInfo}");

                    // Обрабатываем клиента в отдельном потоке
                    Task.Run(() => HandleClient(clientSocket, clientInfo));
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Ошибка сервера: {ex.Message}");
                Console.ResetColor();
                Console.ReadKey();
            }
        }

        static async void HandleClient(Socket clientSocket, string clientInfo)
        {
            try
            {
                NetworkStream stream = new NetworkStream(clientSocket);
                StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                while (clientSocket.Connected)
                {
                    try
                    {
                        // Читаем команду от клиента
                        string jsonData = await reader.ReadLineAsync();

                        if (string.IsNullOrEmpty(jsonData))
                        {
                            Console.WriteLine($"Клиент {clientInfo} отключился");
                            break;
                        }

                        Console.WriteLine($"Получено от {clientInfo}: {jsonData}");

                        // Обработка команды
                        string response = ProcessCommand(jsonData, clientInfo);

                        // Отправка ответа
                        await writer.WriteLineAsync(response);
                        await writer.FlushAsync();
                    }
                    catch (IOException)
                    {
                        Console.WriteLine($"Клиент {clientInfo} разорвал соединение");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Ошибка с клиентом {clientInfo}: {ex.Message}");
                Console.ResetColor();
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
                ViewModelSend request = JsonConvert.DeserializeObject<ViewModelSend>(jsonData);

                if (request == null)
                {
                    return JsonConvert.SerializeObject(new ViewModelMessage("error", "Неверный формат запроса"));
                }

                Console.WriteLine($"Обработка команды: {request.Message}");

                string[] parts = request.Message.Split(new char[] { ' ' }, 2);
                string command = parts[0].ToLower();
                string arguments = parts.Length > 1 ? parts[1] : "";

                switch (command)
                {
                    case "connect":
                        return HandleConnect(arguments, clientInfo);

                    case "get":
                        return HandleGet(request.Id, arguments);

                    case "set":
                        return HandleSet(request.Id, arguments);

                    case "list":
                        return HandleList(request.Id);

                    default:
                        return JsonConvert.SerializeObject(new ViewModelMessage("error", $"Неизвестная команда: {command}"));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки команды: {ex.Message}");
                return JsonConvert.SerializeObject(new ViewModelMessage("error", $"Ошибка сервера: {ex.Message}"));
            }
        }

        static string HandleConnect(string arguments, string clientInfo)
        {
            string[] credentials = arguments.Split(' ');
            if (credentials.Length != 2)
            {
                return JsonConvert.SerializeObject(new ViewModelMessage("error", "Неверный формат. Используйте: connect логин пароль"));
            }

            string login = credentials[0];
            string password = credentials[1];

            User user = dbContext.Users.FirstOrDefault(u => u.Login == login && u.Password == password);

            if (user != null)
            {
                // Запоминаем текущий путь для пользователя
                userCurrentPaths[user.Id] = user.Src;

                // Логируем подключение
                LogCommand(user.Id, $"Подключение с {clientInfo}");

                Console.WriteLine($"Успешная авторизация: {login}");
                return JsonConvert.SerializeObject(new ViewModelMessage("authorization", user.Id.ToString()));
            }
            else
            {
                Console.WriteLine($"Неудачная попытка входа: {login}");
                return JsonConvert.SerializeObject(new ViewModelMessage("error", "Неверный логин или пароль"));
            }
        }

        static string HandleGet(int userId, string fileName)
        {
            if (!userCurrentPaths.ContainsKey(userId))
            {
                return JsonConvert.SerializeObject(new ViewModelMessage("error", "Пользователь не авторизован"));
            }

            string currentPath = userCurrentPaths[userId];
            string filePath = Path.Combine(currentPath, fileName);

            if (!File.Exists(filePath))
            {
                return JsonConvert.SerializeObject(new ViewModelMessage("error", $"Файл '{fileName}' не найден на сервере"));
            }

            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                var fileInfo = new FileInfoFTP(fileBytes, Path.GetFileName(filePath));

                LogCommand(userId, $"get {fileName}");
                Console.WriteLine($"Файл {fileName} отправлен клиенту");

                return JsonConvert.SerializeObject(new ViewModelMessage("file", JsonConvert.SerializeObject(fileInfo)));
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new ViewModelMessage("error", $"Ошибка чтения файла: {ex.Message}"));
            }
        }

        static string HandleSet(int userId, string jsonData)
        {
            if (!userCurrentPaths.ContainsKey(userId))
            {
                return JsonConvert.SerializeObject(new ViewModelMessage("error", "Пользователь не авторизован"));
            }

            try
            {
                var fileInfo = JsonConvert.DeserializeObject<FileInfoFTP>(jsonData);
                if (fileInfo == null)
                {
                    return JsonConvert.SerializeObject(new ViewModelMessage("error", "Неверный формат файла"));
                }

                string currentPath = userCurrentPaths[userId];
                string filePath = Path.Combine(currentPath, fileInfo.Name);

                File.WriteAllBytes(filePath, fileInfo.Data);

                LogCommand(userId, $"set {fileInfo.Name}");
                Console.WriteLine($"Файл {fileInfo.Name} получен от клиента");

                return JsonConvert.SerializeObject(new ViewModelMessage("success", $"Файл '{fileInfo.Name}' успешно загружен на сервер"));
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new ViewModelMessage("error", $"Ошибка загрузки файла: {ex.Message}"));
            }
        }

        static string HandleList(int userId)
        {
            if (!userCurrentPaths.ContainsKey(userId))
            {
                return JsonConvert.SerializeObject(new ViewModelMessage("error", "Пользователь не авторизован"));
            }

            string currentPath = userCurrentPaths[userId];

            try
            {
                var files = Directory.GetFiles(currentPath);
                var fileList = new List<string>();

                foreach (var file in files)
                {
                    fileList.Add(Path.GetFileName(file));
                }

                LogCommand(userId, "list");
                return JsonConvert.SerializeObject(new ViewModelMessage("list", JsonConvert.SerializeObject(fileList)));
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new ViewModelMessage("error", $"Ошибка чтения директории: {ex.Message}"));
            }
        }

        static void LogCommand(int userId, string command)
        {
            try
            {
                var log = new UserLog
                {
                    UserId = userId,
                    Command = command,
                    Date = DateTime.Now
                };

                dbContext.UsersLog.Add(log);
                dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка логирования: {ex.Message}");
            }
        }
    }
}