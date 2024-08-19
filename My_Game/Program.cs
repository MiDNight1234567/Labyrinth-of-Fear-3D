using System;
using System.Text;
using System.Threading;
using System.Linq;
using System.Collections.Generic;

namespace My_Game
{
    internal class Program
    {
        private const int ScreenWidth = 240;
        private const int ScreenHeight = 120;
        private const int MapWidth = 32;
        private const int MapHeight = 32;
        private const double Fov = Math.PI / 3;
        private const double Depth = 16;

        private static double _playerX = 5;
        private static double _playerY = 5;
        private static double _playerFOV = 0;

        private static readonly StringBuilder Map = new StringBuilder();
        private static string originalMap; // Хранение оригинальной карты

        private static readonly char[] Screen = new char[ScreenWidth * ScreenHeight];

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8; // Для корректного отображения символов
            Console.SetWindowSize(ScreenWidth, ScreenHeight);
            Console.SetBufferSize(ScreenWidth, ScreenHeight);
            Console.CursorVisible = false;

            InitMap();
            originalMap = Map.ToString(); // Сохранение оригинальной карты

            DateTime dataTimeFrom = DateTime.Now;

            while (true)
            {
                DateTime dataTimeTo = DateTime.Now;
                double elapsedTime = (dataTimeTo - dataTimeFrom).TotalSeconds;
                dataTimeFrom = DateTime.Now;

                // Обработка ввода
                if (Console.KeyAvailable)
                {
                    ConsoleKey consoleKey = Console.ReadKey(intercept: true).Key;

                    switch (consoleKey)
                    {
                        case ConsoleKey.A:
                            _playerFOV += elapsedTime * 2.0; // Регулировка скорости вращения
                            break;

                        case ConsoleKey.D:
                            _playerFOV -= elapsedTime * 2.0;
                            break;

                        case ConsoleKey.W:
                            double moveXForward = Math.Sin(_playerFOV) * 5.0 * elapsedTime;
                            double moveYForward = Math.Cos(_playerFOV) * 5.0 * elapsedTime;
                            double newPlayerXForward = _playerX + moveXForward;
                            double newPlayerYForward = _playerY + moveYForward;

                            // Проверка на столкновение при движении вперед
                            if (Map[(int)newPlayerYForward * MapWidth + (int)newPlayerXForward] != '#')
                            {
                                _playerX = newPlayerXForward;
                                _playerY = newPlayerYForward;
                            }
                            break;

                        case ConsoleKey.S:
                            double moveXBackward = -Math.Sin(_playerFOV) * 5.0 * elapsedTime;
                            double moveYBackward = -Math.Cos(_playerFOV) * 5.0 * elapsedTime;
                            double newPlayerXBackward = _playerX + moveXBackward;
                            double newPlayerYBackward = _playerY + moveYBackward;

                            // Проверка на столкновение при движении назад
                            if (Map[(int)newPlayerYBackward * MapWidth + (int)newPlayerXBackward] != '#')
                            {
                                _playerX = newPlayerXBackward;
                                _playerY = newPlayerYBackward;
                            }
                            break;
                    }
                }

                // Восстановление карты перед рисованием новых лучей
                Map.Clear();
                Map.Append(originalMap);

                // Прорисовка экрана
                Array.Fill(Screen, ' '); // Очистка экрана

                var rayCastingTasks = new List<Task<Dictionary<int, char>>>();

                //Ray casting
                for (int x = 0; x < ScreenWidth; x++)
                {
                    int x1 = x;
                    rayCastingTasks.Add(item:Task.Run(function:() => CastRay(x1)));
                }

                Dictionary<int, char>[] rays = await Task.WhenAll(rayCastingTasks);

                foreach (Dictionary<int, char> dictionary in rays)
                {
                    foreach (int key in dictionary.Keys)
                    {
                        Screen[key] = dictionary[key];
                    }
                }

                // Status player
                char[] status = $"X: {_playerX}, Y: {_playerY}, FOV: {_playerFOV}, FPS: {(int)(1 / elapsedTime)}".ToCharArray();
                status.CopyTo(array: Screen, index: 0);

                // Map rendering
                for (int x = 0; x < MapWidth; x++)
                {
                    for (int y = 0; y < MapHeight; y++)
                    {
                        Screen[(y + 1) * ScreenWidth + x] = Map[y * MapWidth + x];
                    }
                }

                // Player position
                Screen[(int)(_playerY + 1) * ScreenWidth + (int)_playerX] = 'P';

                Console.SetCursorPosition(0, 0);
                Console.Write(Screen);

                // Небольшая задержка для уменьшения дребезжания
                Thread.Sleep(16); // ~60 FPS
            }
        }

        public static Dictionary<int, char> CastRay(int x)
        {
            var result = new Dictionary<int, char>();

            double rayAngle = _playerFOV + Fov / 2 - x * Fov / ScreenWidth;

            double rayX = Math.Sin(rayAngle);
            double rayY = Math.Cos(rayAngle);

            double distanceToWall = 0;
            bool hitWall = false;
            bool isBound = false;

            while (!hitWall && distanceToWall < Depth)
            {
                distanceToWall += 0.1;

                int testX = (int)(_playerX + rayX * distanceToWall);
                int testY = (int)(_playerY + rayY * distanceToWall);

                if (testX < 0 || testX >= MapWidth || testY < 0 || testY >= MapHeight)
                {
                    hitWall = true;
                    distanceToWall = Depth;
                }
                else
                {
                    char testCell = Map[testY * MapWidth + testX];

                    if (testCell == '#')
                    {
                        hitWall = true;

                        var boundsVectorList = new List<(double module, double cos)>();

                        for (int tx = 0; tx < 2; tx++)
                        {
                            for (int ty = 0; ty < 2; ty++)
                            {
                                double vectorX = testX + tx - _playerX;
                                double vectorY = testY + ty - _playerY;

                                double vectorModule = Math.Sqrt(vectorX * vectorX + vectorY * vectorY);
                                double cosAngle = rayX * vectorX / vectorModule + rayY * vectorY / vectorModule;

                                boundsVectorList.Add((vectorModule, cosAngle));
                            }
                        }

                        boundsVectorList = boundsVectorList.OrderBy(vector => vector.module).ToList();

                        double boundAngle = 0.03 / distanceToWall;

                        if (Math.Acos(boundsVectorList[0].cos) < boundAngle ||
                            Math.Acos(boundsVectorList[1].cos) < boundAngle)
                            isBound = true;
                    }
                    else
                    {
                        Map[testY * MapWidth + testX] = '*'; // Рисуем луч на карте
                    }
                }
            }

            int ceiling = (int)(ScreenHeight / 2.0 - ScreenHeight / distanceToWall);
            int floor = ScreenHeight - ceiling;

            char wallShade;

            if (isBound)
                wallShade = '|';
            else if (distanceToWall < Depth / 4d)
                wallShade = '\u2588';
            else if (distanceToWall < Depth / 3d)
                wallShade = '\u2593';
            else if (distanceToWall < Depth / 2d)
                wallShade = '\u2592';
            else if (distanceToWall < Depth)
                wallShade = '\u2591';
            else wallShade = ' ';

            for (int y = 0; y < ScreenHeight; y++)
            {
                if (y <= ceiling)
                {
                    result[y * ScreenWidth + x] = ' ';
                }
                else if (y > ceiling && y <= floor)
                {
                    result[y * ScreenWidth + x] = wallShade;
                }
                else
                {
                    char floorShade;

                    double b = 1 - (y - ScreenHeight / 2d) / (ScreenHeight / 2d);

                    if (b < 0.25)
                        floorShade = '#';
                    else if (b < 0.5)
                        floorShade = 'x';
                    else if (b < 0.75)
                        floorShade = '-';
                    else if (b < 0.9)
                        floorShade = '.';
                    else
                        floorShade = ' ';

                    result[y * ScreenWidth + x] = floorShade;
                }
            }

            return result;
        }

        private static void InitMap()
        {
            // Инициализация карты
            Map.Clear();
            Map.Append("################################");
            Map.Append("#.....................#........#");
            Map.Append("#.....................#........#");
            Map.Append("#.....................#........#");
            Map.Append("#.....................#........#");
            Map.Append("#.....................#........#");
            Map.Append("#.....................#........#");
            Map.Append("#.....................#........#");
            Map.Append("#.....................#........#");
            Map.Append("#......######.........#........#");
            Map.Append("#.....................#........#");
            Map.Append("#.....................#........#");
            Map.Append("#.....................#.....#..#");
            Map.Append("#.....................#######..#");
            Map.Append("#..............................#");
            Map.Append("#..............................#");
            Map.Append("#..............................#");
            Map.Append("#..............................#");
            Map.Append("#..............................#");
            Map.Append("#############..................#");
            Map.Append("#...........#..................#");
            Map.Append("#..#######.....................#");
            Map.Append("#..#.....#.....................#");
            Map.Append("#.....#..#..#..................#");
            Map.Append("#.....#..#..#..................#");
            Map.Append("#######..#..#..................#");
            Map.Append("#.....#..#..#..................#");
            Map.Append("#..#.....#..#..................#");
            Map.Append("#..#...######..................#");
            Map.Append("#..#........#..................#");
            Map.Append("#..#........#..................#");
            Map.Append("################################");
        }
    }
}
