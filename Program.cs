using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Threading;

class GarbageObject
{
    public byte[] Data;

    public GarbageObject(int size)
    {
        Data = new byte[size];
    }
}

class FinalizableObject
{
    public byte[] Data = new byte[1024];

    ~FinalizableObject()
    {
    }
}

class Program
{
    static volatile bool isRunning = true;

    static void Main()
    {
        Console.WriteLine("1. Измерение времени сборки мусора");
        Task1();

        Console.WriteLine();
        Console.WriteLine("2. Демонстрация фоновой сборки мусора");
        Task2();

        Console.WriteLine();
        Console.WriteLine("3. Демонстрация NoGCRegion");
        Task3();

        Console.ReadLine();
    }

    static void Task1()
    {
        Console.WriteLine("Server GC: " + GCSettings.IsServerGC);
        Console.WriteLine("MaxGeneration: " + GC.MaxGeneration);
        Console.WriteLine("Для сравнения режимов запустите эту же программу с разными настройками GC.");

        List<byte[]> survivors = new List<byte[]>();

        for (int i = 0; i < 20000; i++)
        {
            survivors.Add(new byte[1024]);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        for (int i = 0; i < 100000; i++)
        {
            GarbageObject obj = new GarbageObject(1024);

            if (i % 5000 == 0)
            {
                FinalizableObject f = new FinalizableObject();
            }
        }

        Stopwatch sw = new Stopwatch();
        sw.Start();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        sw.Stop();

        Console.WriteLine("Время сборки мусора: " + sw.ElapsedMilliseconds + " мс");
        Console.WriteLine("CollectionCount Gen0: " + GC.CollectionCount(0));
        Console.WriteLine("CollectionCount Gen1: " + GC.CollectionCount(1));
        Console.WriteLine("CollectionCount Gen2: " + GC.CollectionCount(2));
        Console.WriteLine("TotalMemory: " + GC.GetTotalMemory(false));
    }

    static void Task2()
    {
        Console.WriteLine("Server GC: " + GCSettings.IsServerGC);
        Console.WriteLine("Фоновая/нефоновая GC задается конфигурацией запуска.");

        string path = "demo.txt";
        File.WriteAllText(path, "Это тестовый файл для чтения из потока.");

        try
        {
            GC.RegisterForFullGCNotification(10, 10);

            Thread monitorThread = new Thread(MonitorFullGC);
            Thread allocThread = new Thread(AllocateMemoryWork);
            Thread fileThread = new Thread(ReadFileWork);
            Thread consoleThread = new Thread(ConsoleWork);

            isRunning = true;

            monitorThread.Start();
            allocThread.Start();
            fileThread.Start();
            consoleThread.Start();

            Thread.Sleep(5000);
            isRunning = false;

            monitorThread.Join();
            allocThread.Join();
            fileThread.Join();
            consoleThread.Join();

            GC.CancelFullGCNotification();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Уведомления Full GC недоступны в текущем режиме: " + ex.Message);
        }
    }

    static void MonitorFullGC()
    {
        while (isRunning)
        {
            GCNotificationStatus status1 = GC.WaitForFullGCApproach(500);

            if (status1 == GCNotificationStatus.Succeeded)
            {
                Console.WriteLine();
                Console.WriteLine("Начало Full GC");
                Console.WriteLine("Память: " + GC.GetTotalMemory(false));
                Console.WriteLine("Gen0: " + GC.CollectionCount(0));
                Console.WriteLine("Gen1: " + GC.CollectionCount(1));
                Console.WriteLine("Gen2: " + GC.CollectionCount(2));
            }
            else if (status1 == GCNotificationStatus.NotApplicable)
            {
                Console.WriteLine();
                Console.WriteLine("Full GC notification: NotApplicable");
                break;
            }

            GCNotificationStatus status2 = GC.WaitForFullGCComplete(500);

            if (status2 == GCNotificationStatus.Succeeded)
            {
                Console.WriteLine("Окончание Full GC");
                Console.WriteLine("Память после GC: " + GC.GetTotalMemory(false));
            }
            else if (status2 == GCNotificationStatus.NotApplicable)
            {
                Console.WriteLine("Full GC complete: NotApplicable");
                break;
            }
        }
    }

    static void AllocateMemoryWork()
    {
        List<byte[]> list = new List<byte[]>();

        while (isRunning)
        {
            list.Add(new byte[100000]);

            if (list.Count > 200)
            {
                list.RemoveAt(0);
            }

            Thread.Sleep(10);
        }
    }

    static void ReadFileWork()
    {
        while (isRunning)
        {
            if (File.Exists("demo.txt"))
            {
                string text = File.ReadAllText("demo.txt");
            }

            Thread.Sleep(50);
        }
    }

    static void ConsoleWork()
    {
        while (isRunning)
        {
            Console.Write(".");
            Thread.Sleep(200);
        }

        Console.WriteLine();
    }

    static void Task3()
    {
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);

        bool started = false;

        try
        {
            started = GC.TryStartNoGCRegion(4 * 1024 * 1024);
            Console.WriteLine("NoGCRegion запущен: " + started);

            List<byte[]> list = new List<byte[]>();

            for (int i = 0; i < 100; i++)
            {
                list.Add(new byte[100000]);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка при работе с NoGCRegion: " + ex.Message);
        }
        finally
        {
            if (started)
            {
                try
                {
                    GC.EndNoGCRegion();
                    Console.WriteLine("NoGCRegion завершен");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EndNoGCRegion завершился с ошибкой: " + ex.Message);
                }
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int gen0After = GC.CollectionCount(0);
        int gen1After = GC.CollectionCount(1);
        int gen2After = GC.CollectionCount(2);

        Console.WriteLine("Gen0 до: " + gen0Before + ", после: " + gen0After);
        Console.WriteLine("Gen1 до: " + gen1Before + ", после: " + gen1After);
        Console.WriteLine("Gen2 до: " + gen2Before + ", после: " + gen2After);
    }
}