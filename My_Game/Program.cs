using System.Text;
using System.Data;
using WMPLib;
using System.Runtime.InteropServices;

namespace Labyrinth_of_Fear_3D
{
    internal class Program
    {
        private const int ScreenWidth = 240;
        private const int ScreenHeight = 120;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleScreenBufferSize(IntPtr hConsoleOutput, COORD dwSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
        }

        private const int SW_MAXIMIZE = 3;

        private const int MapWidth = 40;
        private const int MapHeight = 60;
        private const double Fov = Math.PI / 3;
        private const double Depth = 16;

        private static double _playerX;
        private static double _playerY;
        private static double _playerFOV = 0;

        private static WindowsMediaPlayer SoundOfGame;
        private static WindowsMediaPlayer FootstepSound;
        private static WindowsMediaPlayer CollisionSound;
        private static WindowsMediaPlayer EnemySound;
        private static WindowsMediaPlayer DoorSound;

        private static bool isMoving = false;

        private static readonly List<(double X, double Y, double DirectionX, double DirectionY)> Enemies = new List<(double, double, double, double)>
        {
            (15, 15, 0, 1), // Враг 1: начальная позиция и направление
            (20, 20, 1, 0)  // Враг 2: начальная позиция и направление
        };

        private static readonly StringBuilder Map = new StringBuilder();
        private static string originalMap;

        private static readonly char[] Screen = new char[ScreenWidth * ScreenHeight];

        private static readonly Random Random = new Random();

        static async Task Main(string[] args)
        {
            // Максимизируем консольное окно
            IntPtr consoleWindow = GetConsoleWindow();
            ShowWindow(consoleWindow, SW_MAXIMIZE);

            // Настройка размера буфера консоли
            COORD bufferSize = new COORD { X = (short)Console.WindowWidth, Y = (short)Console.WindowHeight };
            SetConsoleScreenBufferSize(consoleWindow, bufferSize);

            // Вызов начального экрана
            await ShowStartScreenAsync();

            // Запуск фоновой музыки
            InitializeSounds();

            Console.OutputEncoding = Encoding.UTF8; // Для корректного отображения символов
            Console.SetWindowSize(ScreenWidth, ScreenHeight);
            Console.SetBufferSize(ScreenWidth, ScreenHeight);
            Console.CursorVisible = false;

            InitMap();
            originalMap = Map.ToString(); // Сохранение оригинальной карты
            GenerateRandomEnemies(13); // Добавление 5 случайных врагов

            // Установка случайной начальной позиции игрока
            (_playerX, _playerY) = GetRandomStartPosition();

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
                    bool playerMoved = false; // Флаг для отслеживания, было ли движение

                    switch (consoleKey)
                    {
                        case ConsoleKey.A:
                            _playerFOV += elapsedTime * 2.0; // Регулировка скорости вращения
                            playerMoved = true;
                            break;

                        case ConsoleKey.D:
                            _playerFOV -= elapsedTime * 2.0;
                            playerMoved = true;
                            break;

                        case ConsoleKey.W:
                            double moveXForward = Math.Sin(_playerFOV) * 5.0 * elapsedTime;
                            double moveYForward = Math.Cos(_playerFOV) * 5.0 * elapsedTime;
                            double newPlayerXForward = _playerX + moveXForward;
                            double newPlayerYForward = _playerY + moveYForward;

                            // Проверка на касание двери
                            char cellForward = Map[(int)newPlayerYForward * MapWidth + (int)newPlayerXForward];

                            if (cellForward == 'D') // Проверка на дверь
                            {
                                await HandleVictory();
                                return;
                            }
                            else if (cellForward != '#')
                            {
                                _playerX = newPlayerXForward;
                                _playerY = newPlayerYForward;
                            }
                            playerMoved = true;
                            break;

                        case ConsoleKey.S:
                            double moveXBackward = -Math.Sin(_playerFOV) * 5.0 * elapsedTime;
                            double moveYBackward = -Math.Cos(_playerFOV) * 5.0 * elapsedTime;
                            double newPlayerXBackward = _playerX + moveXBackward;
                            double newPlayerYBackward = _playerY + moveYBackward;

                            if (Map[(int)newPlayerYBackward * MapWidth + (int)newPlayerXBackward] != '#')
                            {
                                _playerX = newPlayerXBackward;
                                _playerY = newPlayerYBackward;
                            }
                            playerMoved = true;
                            break;
                    }

                    // Обновление состояния движения
                    if (playerMoved)
                    {
                        if (!isMoving)
                        {
                            isMoving = true;
                            PlayFootstepSound(); // Начинаем воспроизведение звука шагов
                        }
                    }
                    else if (isMoving)
                    {
                        isMoving = false;
                        await StopFootstepSoundAsync(); // Останавливаем воспроизведение звука шагов
                    }
                }
                else if (isMoving) // Если клавиши не нажаты, останавливаем звук
                {
                    isMoving = false;
                    await StopFootstepSoundAsync();
                }

                // Обновление позиции врагов
                UpdateEnemies(elapsedTime);

                // Восстановление карты перед рисованием новых лучей
                Map.Clear();
                Map.Append(originalMap);

                // Добавление врагов на карту
                foreach (var enemy in Enemies)
                {
                    Map[(int)enemy.Y * MapWidth + (int)enemy.X] = 'E'; // 'E' для врагов
                }

                // Проверка на столкновение с врагом
                if (Enemies.Any(enemy => (int)_playerX == (int)enemy.X && (int)_playerY == (int)enemy.Y))
                {
                    await HandleGameOver(); // Обработка завершения игры
                    return; // Завершение основного цикла и возвращение к экрану выбора
                }

                // Прорисовка экрана
                Array.Fill(Screen, ' '); // Очистка экрана

                var rayCastingTasks = new List<Task<Dictionary<int, char>>>();

                // Ray casting
                for (int x = 0; x < ScreenWidth; x++)
                {
                    int x1 = x;
                    rayCastingTasks.Add(Task.Run(() => CastRay(x1)));
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

        // Метод для получения случайной начальной позиции на карте
        private static (double X, double Y) GetRandomStartPosition()
        {
            int startX, startY;

            do
            {
                startX = Random.Next(1, MapWidth - 1);
                startY = Random.Next(1, MapHeight - 1);
            } while (Map[startY * MapWidth + startX] != '.'); // Проверка, чтобы начальная позиция была безопасной

            return (startX, startY);
        }

        private static async Task ShowStartScreenAsync()
        {
            Console.Clear();
            Console.CursorVisible = false;
            Console.SetWindowSize(ScreenWidth, ScreenHeight);
            Console.SetBufferSize(ScreenWidth, ScreenHeight);

            string[] options = { "START", "EXIT", "Инструкция" };
            int selectedOption = 0;

            // Получаем центр экрана
            int centerX = ScreenWidth / 2;
            int centerY = ScreenHeight / 2;

            // Вычисляем максимальную ширину опций
            int maxOptionWidth = options.Max(option => option.Length);

            // Вычисляем вертикальное смещение для центровки
            int verticalOffset = options.Length / 2;

            while (true)
            {
                Console.Clear();

                // Печатаем заголовок
                Console.SetCursorPosition(centerX - 10, centerY - verticalOffset - 2);
                Console.WriteLine("Labyrinth of Fear 3D");

                // Печатаем опции по центру экрана
                for (int i = 0; i < options.Length; i++)
                {
                    int optionX = centerX - (maxOptionWidth / 2); // Центрируем по горизонтали
                    int optionY = centerY - verticalOffset + i; // Центрируем по вертикали

                    Console.SetCursorPosition(optionX, optionY);

                    if (i == selectedOption)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow; // Выделение выбранной опции
                        Console.WriteLine("> " + options[i]);
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("  " + options[i]);
                    }
                }

                switch (Console.ReadKey(true).Key)
                {
                    case ConsoleKey.UpArrow:
                        selectedOption = (selectedOption == 0) ? options.Length - 1 : selectedOption - 1;
                        break;
                    case ConsoleKey.DownArrow:
                        selectedOption = (selectedOption == options.Length - 1) ? 0 : selectedOption + 1;
                        break;
                    case ConsoleKey.Enter:
                        if (selectedOption == 0) // START
                        {
                            return; // Продолжаем игру
                        }
                        else if (selectedOption == 1) // EXIT
                        {
                             await ExitGame(); // Выход из игры
                        }
                        else if (selectedOption == 2) // Инструкция
                        {
                            await ShowInstructionAsync(); // Показать инструкцию
                        }
                        break;
                }
            }
        }

        private static async Task ShowInstructionAsync()
        {
            Console.Clear();
            Console.WriteLine("Цель игры:\n\nВам предстоит исследовать лабиринт и найти дверь, которая приведет к победе. По пути вам придется избегать врагов и взаимодействовать с различными объектами.\r\n\r\nУправление:\r\n\r\nW – Двигайтесь вперед.\r\nS – Двигайтесь назад.\r\nA – Поверните влево.\r\nD – Поверните вправо.\r\n\nОсобенности:\r\n\r\nИгровое поле: Ваше движение происходит в ограниченном пространстве, а ваша цель – найти дверь.\r\nВраги(E): Будьте осторожны, враги могут быть на вашем пути. Если вы столкнетесь с врагом, игра закончится.\r\nДверь(D): Найдите дверь, чтобы выиграть игру.\r\n\nСоветы:\r\n\r\nРегулярно осматривайтесь и планируйте свои действия. Некоторые участки карты могут быть опасны.\r\nИспользуйте свою позицию для определения направления движения и избегайте врагов.\r\n\nУдачи!");
            Console.WriteLine();
            Console.WriteLine("Press Enter to return to the main menu...");
            Console.ReadLine();
        }

        private static async Task ExitGame()
        {
            Console.Clear();
            Console.WriteLine("Press Enter to exit...");
            Environment.Exit(0); // Завершение процесса
        }

        private static async Task ResetGame()
        {
            Console.Clear();

            // Остановка звуков
            await StopFootstepSoundAsync();
            SoundOfGame.controls.stop();

            // Очистка списка врагов
            Enemies.Clear();

            // Пересоздание карты
            InitMap();
            originalMap = Map.ToString(); // Сохранение оригинальной карты

            // Генерация новых врагов
            GenerateRandomEnemies(13); // Количество врагов можно изменить по необходимости

            // Установка случайной начальной позиции игрока
            (_playerX, _playerY) = GetRandomStartPosition();

            // Сброс других игровых переменных
            _playerFOV = 0; // начальный угол обзора

            // Ожидание некоторого времени перед началом новой игры (опционально)
            //await Task.Delay(1000);

            // Перезапуск основного игрового цикла
            await Main(new string[0]); // Вы можете использовать параметры по умолчанию или передавать другие, если необходимо
        }

        private static async Task HandleGameOver()
        {
            await PlayEnemySoundAsync();
            await PlayCollisionSoundAsync();
            Console.Clear();
            Console.WriteLine("Game Over! The enemy has consumed your soul!");
            Console.WriteLine("Press Enter to return to the main menu...");
            while (Console.ReadKey(true).Key != ConsoleKey.Enter)
            {
                // Ожидание нажатия клавиши Enter
            }
            await CleanupResources();
            await CleanupSoundsAsync();
            await ResetGame(); // Сброс игры и возвращение к начальному экрану

        }

        // Изменим победный сценарий
        private static async Task HandleVictory()
        {
            await PlayDoorSoundAsync();
            Console.Clear();
            Console.WriteLine("You Won! You have found the door of truth!");
            Console.WriteLine("Press Enter to return to the main menu...");
            while (Console.ReadKey(true).Key != ConsoleKey.Enter)
            {
                // Ожидание нажатия клавиши Enter
            }
            await CleanupResources();
            await CleanupSoundsAsync();
            await ResetGame(); // Сброс игры и возвращение к начальному экрану
        }

        // Метод для воспроизведения фоновой музыки
        private static void InitializeSounds()
        {
            SoundOfGame = new WindowsMediaPlayer();
            FootstepSound = new WindowsMediaPlayer();
            CollisionSound = new WindowsMediaPlayer();
            EnemySound = new WindowsMediaPlayer();
            DoorSound = new WindowsMediaPlayer();

            SoundOfGame = new WindowsMediaPlayer();
            SoundOfGame.URL = @"E:\Resume - Project\Visual Studio\Labyrinth of Fear 3D\Labyrinth of Fear 3D\bin\Debug\\songs\\Sound-of-Game.mp3";
            SoundOfGame.settings.setMode("loop", true); // Проигрывание в цикле
            SoundOfGame.controls.play();
            SoundOfGame.settings.volume = 80;

            FootstepSound = new WindowsMediaPlayer();
            FootstepSound.settings.volume = 50;

            // Инициализация звука столкновения
            CollisionSound = new WindowsMediaPlayer();
            CollisionSound.settings.volume = 80;

            EnemySound = new WindowsMediaPlayer();
            EnemySound.settings.volume = 100;

            DoorSound = new WindowsMediaPlayer();
            DoorSound.settings.volume = 80;
        }

        private static async Task PlayCollisionSoundAsync()
        {
            CollisionSound.URL = @"E:\Resume - Project\Visual Studio\Labyrinth of Fear 3D\Labyrinth of Fear 3D\bin\Debug\\songs\\Young-Male-Scream.mp3";
            CollisionSound.controls.play();
            await Task.Delay(800); // Ожидание, чтобы звук успел проиграться (1000 мс = 1 секунда)
        }

        private static async Task PlayEnemySoundAsync()
        {
            EnemySound.URL = @"E:\Resume - Project\Visual Studio\Labyrinth of Fear 3D\Labyrinth of Fear 3D\bin\Debug\\songs\\Echo-Jumpscare.mp3";
            EnemySound.controls.play();
            await Task.Delay(500); // Ожидание, чтобы звук успел проиграться (1000 мс = 1 секунда)
        }

        private static async Task PlayDoorSoundAsync()
        {
            EnemySound.URL = @"E:\Resume - Project\Visual Studio\Labyrinth of Fear 3D\Labyrinth of Fear 3D\bin\Debug\\songs\\Door-open-close.mp3";
            EnemySound.controls.play();
            await Task.Delay(1000); // Ожидание, чтобы звук успел проиграться (1000 мс = 1 секунда)
        }

        private static void PlayFootstepSound()
        {
            FootstepSound.URL = @"E:\Resume - Project\Visual Studio\Labyrinth of Fear 3D\Labyrinth of Fear 3D\bin\Debug\\songs\\FootstepSound.mp3";
            FootstepSound.controls.play();
        }

        private static async Task StopFootstepSoundAsync()
        {
            try
            {
                if (FootstepSound != null)
                {
                    FootstepSound.controls.stop();
                    await Task.Delay(100); // Задержка для обработки остановки
                }
            }
            catch (COMException ex)
            {
                // Логирование или обработка исключения
                Console.WriteLine($"Error stopping footstep sound: {ex.Message}");
            }
        }

        private static async Task CleanupSoundsAsync()
        {
            SoundOfGame.controls.stop();
            FootstepSound.controls.stop();
            CollisionSound.controls.stop();
            EnemySound.controls.stop();
            DoorSound.controls.stop();

            SoundOfGame.close();
            FootstepSound.close();
            CollisionSound.close();
            EnemySound.close();
            DoorSound.close();
        }

        private static async Task CleanupResources()
        {
            if (SoundOfGame != null)
            {
                SoundOfGame.controls.stop();
                SoundOfGame.close();
            }
            // То же самое для других звуковых объектов
            if (FootstepSound != null)
            {
                FootstepSound.controls.stop();
                FootstepSound.close();
            }

            if (CollisionSound != null)
            {
                CollisionSound.controls.stop();
                CollisionSound.close();
            }

            if (EnemySound != null)
            {
                EnemySound.controls.stop();
                EnemySound.close();
            }

            if (DoorSound != null)
            {
                DoorSound.controls.stop();
                DoorSound.close();
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
            bool hitDoor = false; // Для отслеживания попадания на дверь
            bool hitEnemy = false; // Для отслеживания попадания на врага
            bool isBound = false;

            int enemyX = -1; // Для хранения координат врага
            int enemyY = -1;
            double enemyDistance = Depth; // Для хранения расстояния до врага

            int dorX = -1; // Для хранения координат врага
            int dorY = -1;
            double dorDistance = Depth; // Для хранения расстояния до врага


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
                    else if (testCell == 'D')
                    {
                        hitWall = true;
                        hitDoor = true; // Установить флаг, если луч попал в дверь
                        dorX = testX;
                        dorY = testY;
                        dorDistance = distanceToWall;
                    }
                    else if (testCell == 'E')
                    {
                        hitWall = true;
                        hitEnemy = true;
                        // Сохраняем координаты врага и его расстояние
                        enemyX = testX;
                        enemyY = testY;
                        enemyDistance = distanceToWall;
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

            if (hitEnemy)
            {
                // Определяем яркость врага по расстоянию
                char enemyShade;

                if (enemyDistance < 5) enemyShade = 'E';  // Ближе = ярче
                else if (enemyDistance < 10) enemyShade = 'e';
                else if (enemyDistance < 15) enemyShade = 'f';
                else enemyShade = ' ';

                wallShade = enemyShade;
            }
            else if (hitDoor) // Если луч попал в дверь, используем другой символ
            {
                char dorShade;

                if (dorDistance < 5) dorShade = 'D';  // Ближе = ярче
                else if (dorDistance < 10) dorShade = 'd';
                else if (dorDistance < 15) dorShade = 'o';
                else dorShade = ' ';

                wallShade = dorShade;
            }
            else if (isBound)
            {
                wallShade = '|';
            }
            else if (distanceToWall <= Depth / 4.0)
            {
                wallShade = '\u2588';
            }
            else if (distanceToWall < Depth / 3.0)
            {
                wallShade = '\u2593';
            }
            else if (distanceToWall < Depth / 2.0)
            {
                wallShade = '\u2592';
            }
            else if (distanceToWall < Depth)
            {
                wallShade = '\u2591';
            }
            else
            {
                wallShade = ' ';
            }

            for (int y = 0; y < ScreenHeight; y++)
            {
                if (y < ceiling)
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

                    double b = 1 - (y - ScreenHeight / 2.0) / (ScreenHeight / 2.0);

                    if (b < 0.25)
                        floorShade = '#';
                    else if (b < 0.5)
                        floorShade = 'x';
                    else if (b < 0.75)
                        floorShade = '.';
                    else if (b < 0.9)
                        floorShade = '-';
                    else
                        floorShade = ' ';

                    result[y * ScreenWidth + x] = floorShade;
                }
            }

            return result;
        }

        private static void InitMap()
        {
            Random random = new Random();

            // Инициализация карты
            Map.Clear();

            // Заполнение карты стенами по периметру
            for (int x = 0; x < MapWidth; x++)
            {
                Map.Append('#');
            }

            for (int y = 1; y < MapHeight - 1; y++)
            {
                Map.Append('#');
                for (int x = 1; x < MapWidth - 1; x++)
                {
                    char cell = random.NextDouble() < 0.2 ? '#' : '.'; // 20% вероятность стены
                    Map.Append(cell);
                }
                Map.Append('#');
            }
            for (int x = 0; x < MapWidth; x++)
            {
                Map.Append('#');
            }

            // Размещение двери
            int doorX = random.Next(1, MapWidth - 1);
            int doorY = random.Next(1, MapHeight - 1);
            Map[doorY * MapWidth + doorX] = 'D';

            // Сохранение оригинальной карты
            originalMap = Map.ToString();
        }


        // Метод для обновления позиции врагов
        private static void UpdateEnemies(double elapsedTime)
        {
            for (int i = 0; i < Enemies.Count; i++)
            {
                var (X, Y, DirX, DirY) = Enemies[i];

                // Определяем небольшое смещение врагов
                double moveX = DirX * elapsedTime * 2; // Скорость уменьшена до 2 для плавности
                double moveY = DirY * elapsedTime * 2;

                double newX = X + moveX;
                double newY = Y + moveY;

                // Проверка столкновения с картой
                if (newX >= 0 && newX < MapWidth && Map[(int)Y * MapWidth + (int)newX] != '#')
                {
                    X = newX;
                }
                else
                {
                    // Если столкнулись со стеной, меняем направление
                    DirX = -DirX;
                }

                if (newY >= 0 && newY < MapHeight && Map[(int)newY * MapWidth + (int)X] != '#')
                {
                    Y = newY;
                }
                else
                {
                    // Если столкнулись со стеной, меняем направление
                    DirY = -DirY;
                }

                // Обновление позиции и направления врага
                Enemies[i] = (X, Y, DirX, DirY);

                // Случайное изменение направления через каждые 2-5 секунд
                if (Random.NextDouble() < 0.02)
                {
                    DirX = (Random.NextDouble() - 0.5) * 2;
                    DirY = (Random.NextDouble() - 0.5) * 2;
                    Enemies[i] = (X, Y, DirX, DirY);
                }
            }
        }
        private static void GenerateRandomEnemies(int count)
        {
            Random random = new Random();

            while (Enemies.Count < count)
            {
                int x = random.Next(1, MapWidth - 1);
                int y = random.Next(1, MapHeight - 1);

                // Проверка, чтобы не ставить врагов на стенки, дверь и на место игрока
                if (Map[y * MapWidth + x] == '.' && !(x == (int)_playerX && y == (int)_playerY))
                {
                    // Генерация случайного направления
                    double dirX = (random.NextDouble() - 0.5) * 2;
                    double dirY = (random.NextDouble() - 0.5) * 2;

                    // Добавляем врага в список с координатами и направлением
                    Enemies.Add((x, y, dirX, dirY));
                }
            }
        }
    }
}