using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

class Choice {
    public string Text;
    public string Next;

    public Choice(string text, string next) { Text = text; Next = next; }
}

class StoryNode
{
    public string[] Lines;
    public string Question;
    public List<Choice> Choices = new List<Choice>();
    public Action OnEnter;

    public StoryNode(params string[] lines) { Lines = lines; }

    public bool IsEnding { get { return Choices.Count == 0; } }
}

class Art
{
    public string Title;
    public ConsoleColor Color;
    public string Picture;

    public Art(string title, ConsoleColor color, string picture) { Title = title; Color = color; Picture = picture; }
}

class Program
{
    static Dictionary<string, StoryNode> nodes = new Dictionary<string, StoryNode>();
    static Dictionary<string, Art> arts = new Dictionary<string, Art>();
    static Dictionary<string, Process> openWindows = new Dictionary<string, Process>();
    static Random rng = new Random();

    static void Main(string[] args)
    {
        BuildArt();

        if (args.Length >= 6 && args[0] == "--art")
        {
            ShowArtWindow(args[1], int.Parse(args[2]), int.Parse(args[3]), new IntPtr(long.Parse(args[4])), int.Parse(args[5]));
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (s, e) => CloseAllArt();
        Console.CancelKeyPress += (s, e) => CloseAllArt();

        ShowTitle();
        BuildStory();
        Play("start");
        Console.ReadLine();
        CloseAllArt();
    }

    static void BuildArt()
    {
        arts["you"] = new Art("you", ConsoleColor.Cyan, @"
     .---.

");

        arts["grave"] = new Art("here lies", ConsoleColor.Gray, @"
      _______

");
    }

    static void BuildStory()
    {
        nodes["start"] = new StoryNode(
            "Time is a concept that concerns the living.",
            "If you chose that you wish to ascend you will find that it no longer concerns your soul.");
        nodes["start"].OnEnter = () => OpenArt("you", 900, 150);
        nodes["start"].Question = "Do you wish to ascend?";
        nodes["start"].Choices.Add(new Choice("Yes", "ascend"));
        nodes["start"].Choices.Add(new Choice("No", "stay"));

        nodes["ascend"] = new StoryNode(
            "Very well. Then you shall rise once more and ascend to the realm of those who sleep...");
        nodes["ascend"].OnEnter = () =>
        {
            CloseArt("you");
            OpenArt("grave", 900, 150);
        };

        nodes["stay"] = new StoryNode(
            "I understand your choice. It can be... terrifying. To chose to no longer walk amongst your own. Farewell.");
    }

    static void Play(string id)
    {
        List<string> path = new List<string>();

        while (true)
        {
            StoryNode current;
            if (!nodes.TryGetValue(id, out current))
            {
                Console.WriteLine(" [Missing scene: " + id + "]");
                break;
            }

            if (current.OnEnter != null) current.OnEnter();

            foreach (string line in current.Lines)
            {
                Console.Write(" ");
                Type(line);
                Console.WriteLine();
                Console.ReadLine();
            }

            if (current.IsEnding) break;

            Choice picked = AskChoice(current);
            path.Add(picked.Text);
            id = picked.Next;
            Console.WriteLine();
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("\n ~ THE END ~");
        Console.ResetColor();
        Console.WriteLine(" Your choices: " + string.Join(" -> ", path));
    }

    static Choice AskChoice(StoryNode node)
    {
        Console.WriteLine(" " + node.Question);
        for (int i = 0; i < node.Choices.Count; i++)
            Console.WriteLine("   [" + (i + 1) + "] " + node.Choices[i].Text);

        while (true)
        {
            Console.Write(" > ");
            string input = Console.ReadLine();
            if (input == null) return node.Choices[node.Choices.Count - 1];
            input = input.Trim().ToLower();

            int number;
            if (int.TryParse(input, out number) && number >= 1 && number <= node.Choices.Count)
                return node.Choices[number - 1];

            foreach (Choice c in node.Choices)
            {
                string text = c.Text.ToLower();
                if (input == text || (input.Length == 1 && text.StartsWith(input)))
                    return c;
            }

            Console.WriteLine(" Please pick one of the options.");
        }
    }

    static string AskText(string question)
    {
        Console.Write(" " + question + " ");
        return (Console.ReadLine() ?? "").Trim();
    }

    static void Type(string text, int speed = 30)
    {
        foreach (char ch in text)
        {
            Console.Write(ch);
            Thread.Sleep(speed);
        }
    }

    static void Glitch(string from, string to, int frames = 20)
    {
        string glyphs = "#@$%&*!?/\\|<>~";
        int length = Math.Max(from.Length, to.Length);
        from = from.PadRight(length);
        to = to.PadRight(length);

        for (int f = 0; f <= frames; f++)
        {
            char[] row = new char[length];
            for (int i = 0; i < length; i++)
            {
                if (i < length * f / frames) row[i] = to[i];
                else if (rng.Next(3) == 0) row[i] = glyphs[rng.Next(glyphs.Length)];
                else row[i] = from[i];
            }
            Console.Write("\r " + new string(row));
            Thread.Sleep(60);
        }
        Console.WriteLine();
    }

    static void OpenArt(string name, int x, int y)
    {
        CloseArt(name);
        string args = "\"" + Environment.ProcessPath + "\" --art " + name + " " + x + " " + y + " " + GetForegroundWindow().ToInt64() + " " + Environment.ProcessId;
        ProcessStartInfo info = new ProcessStartInfo("conhost.exe", args);
        info.UseShellExecute = true;
        openWindows[name] = Process.Start(info);
        Thread.Sleep(300);
    }

    static void CloseArt(string name)
    {
        Process p;
        if (!openWindows.TryGetValue(name, out p)) return;
        try { p.Kill(true); } catch { }
        openWindows.Remove(name);
    }

    static void CloseAllArt()
    {
        foreach (string name in new List<string>(openWindows.Keys))
            CloseArt(name);
    }

    static void ShowArtWindow(string name, int x, int y, IntPtr storyWindow, int storyProcessId)
    {
        Art art = arts[name];
        string[] rows = art.Picture.Replace("\r", "").Split('\n');
        int width = 0;
        foreach (string row in rows) width = Math.Max(width, row.Length);

        Console.Title = art.Title;
        Console.CursorVisible = false;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                Console.SetWindowSize(width + 4, rows.Length + 1);
                Console.SetBufferSize(width + 4, rows.Length + 1);
            }
            catch { }
        }

        SetWindowPos(GetConsoleWindow(), IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004);
        Console.ForegroundColor = art.Color;
        Console.Clear();
        Console.Write(string.Join("\n", rows));
        SetForegroundWindow(storyWindow);

        try
        {
            Process story = Process.GetProcessById(storyProcessId);
            story.WaitForExit();
        }
        catch { }
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    static void ShowTitle()
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(@"
  +==================================================+
  |--------------------------------------------------|
  |------T H E   D E A T H   O F---------------------|
  |--------------------------------------------------|
  |------------------Y O U   A N D   M E ------------|
  |--------------------------------------------------|
  |a game by Lunavia---------------------------------|
  +==================================================+
");
        Console.ResetColor();
        Console.WriteLine("            Press Enter to begin...");
        Console.ReadLine();
        try { Console.Clear(); } catch { Console.WriteLine("\n\n"); }
    }
}
