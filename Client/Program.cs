using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Server;

namespace Client
{
    public class Program
    {
        public static IPAddress IPAddress;
        public static int Port;
        public static int Id = -1;

        public static void Main(string[] args)
        {
            Console.Write("Введите Ip адрес сервера: ");
            string sIpAdress = Console.ReadLine();
            Console.Write("Введите порт: ");
            string sPort = Console.ReadLine();
            if (int.TryParse(sPort, out Port) && IPAddress.TryParse(sIpAdress, out IPAddress))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Данные успешно введены. Запускаю сервер.");
                while (true)
                {
                    ConnectServer();
                }
            }
        }

        public static bool CheckCommand(string message)
        {
            bool BCommand = false;
            return BCommand;
        }

        public static void ConnectServer()
        {

        }
    }
}