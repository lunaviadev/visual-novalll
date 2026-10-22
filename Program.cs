// =====================================================================
//  The Death of You and Me - a short text-based narrative game
//  a game by Lunavia
//
//  HOW THE STORY IS STORED
//  The story is a directed graph. Every scene is a StoryNode, and every
//  StoryNode lives in a Dictionary<string, StoryNode> keyed by a short id
//  ("start", "wake", "pact" ...). The choices on a node are the edges of
//  the graph - each Choice just holds the id of the node it leads to.
//
//  I went with a graph instead of a pure tree because a tree can't merge:
//  in this story a few different branches lead back into the same scene
//  (e.g. "window" and "hold" both end up at "pact"), so with a tree I'd
//  have had to copy those scenes. With ids, two choices can point at the
//  same node and the scene only gets written once.
//
//  A node with no choices and no AutoNext is a leaf = an ending.
// =====================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// An edge in the story graph: the text the player sees + the id of the node it goes to
class Choice {
    public string Text;
    public string Next;

    public Choice(string text, string next) { Text = text; Next = next; }
}

// A single scene in the story graph
class StoryNode
{
    public string[] Lines;                          // printed one at a time, player presses Enter between them
    public string Question;                         // prompt shown above the choices
    public List<Choice> Choices = new List<Choice>();
    public string AutoNext;                         // for linear scenes - skip the question and go straight here
    public string Ending;                           // name of the ending, only set on leaf nodes
    public Action OnEnter;                          // runs before the lines (used for swapping art)
    public Action AfterLines;                       // runs after the lines (used for typed input / effects)

    public StoryNode(params string[] lines) { Lines = lines; }

    public bool IsEnding { get { return Choices.Count == 0 && AutoNext == null; } }
}

// ASCII picture that gets shown in its own little console window beside the story
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

    // things the player types in, reused later in the story
    static string playerName = "";
    static string letterText = "";

    static void Main(string[] args)
    {
        BuildArt();

        // the art windows are just this same exe launched again with --art,
        // so if we got those args we draw the picture and stop here
        if (args.Length >= 6 && args[0] == "--art")
        {
            ShowArtWindow(args[1], int.Parse(args[2]), int.Parse(args[3]), new IntPtr(long.Parse(args[4])), int.Parse(args[5]));
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (s, e) => CloseAllArt();
        Console.CancelKeyPress += (s, e) => CloseAllArt();

        ShowTitle();
        BuildStory();

        bool again = true;
        while (again)
        {
            playerName = "";
            letterText = "";
            Play("start");

            Console.Write("\n Play again? (y/n) ");
            string answer = (Console.ReadLine() ?? "").Trim().ToLower();
            again = answer.StartsWith("y");

            CloseAllArt();
            if (again) { try { Console.Clear(); } catch { Console.WriteLine("\n\n"); } }
        }
    }

    static void BuildArt()
    {
        arts["you"] = new Art("you", ConsoleColor.Cyan, @"
      .---.
     /     \
     \ . . /
      \ - /
    .-'   '-.
   /  |   |  \
      |   |
      |___|
      /   \
");

        arts["grave"] = new Art("here lies", ConsoleColor.Gray, @"
       _______
      /       \
     /  HERE   \
    |   LIES    |
    |           |
    |    TWO    |
    |  HEARTS   |
    |           |
  __|___________|__
 / ,  ,  ,  ,  ,   \
");

        arts["heaven"] = new Art("together", ConsoleColor.Yellow, @"
  .    \   |   /    .
     .  \  |  /  .
   ------  (*)  ------
     .  /  |  \  .
  .    /   |   \    .

       o         o
      /|\_______/|\
      / \       / \
");

        arts["alone"] = new Art("alone", ConsoleColor.DarkCyan, @"
  .    \   |   /    .
   ------  (*)  ------
  .    /   |   \    .


           o
          /|\
          / \
");
    }

    // ---------------------------------------------------------------
    //  All the scenes + how they connect. Read this as a map:
    //
    //  start -> ascend -> name -> wake -> (talk | window -> talk)
    //  talk  -> (pact | hold -> pact)
    //  pact  -> (letter -> day | day)
    //  day   -> (bakery | pier) -> dusk
    //  dusk  -> together -> together_rise   [ENDING: Together]
    //        -> apart    -> apart_rise      [ENDING: Apart]
    //  start -> stay                        [ENDING: Linger]
    // ---------------------------------------------------------------
    static void BuildStory()
    {
        nodes["start"] = new StoryNode(
            "Time is a concept that concerns the living.",
            "If you chose that you wish to ascend you will find that it no longer concerns your soul.");
        nodes["start"].OnEnter = () => OpenArt("you", 900, 150);
        nodes["start"].Question = "Do you wish to ascend?";
        nodes["start"].Choices.Add(new Choice("Yes", "ascend"));
        nodes["start"].Choices.Add(new Choice("No", "stay"));

        nodes["stay"] = new StoryNode(
            "I understand your choice. It can be... terrifying. To chose to no longer walk amongst your own. Farewell.",
            "So you stay. You sit beside a stone with your name on it, and the seasons turn without you.",
            "Somewhere above, someone is waiting for you.",
            "She will wait a very long time.");
        nodes["stay"].OnEnter = () =>
        {
            CloseArt("you");
            OpenArt("grave", 900, 150);
        };
        nodes["stay"].Ending = "Linger";

        nodes["ascend"] = new StoryNode(
            "Very well. Then you shall rise once more and ascend to the realm of those who sleep...",
            "But no soul rises carrying what it has not set down.",
            "First, you must remember.");
        nodes["ascend"].AutoNext = "name";

        nodes["name"] = new StoryNode(
            "Start with the simplest thing.");
        nodes["name"].AfterLines = () =>
        {
            playerName = AskText("What was your name?");
            if (playerName == "") playerName = "Ellie";
            Say("{name}. Yes. That was you.");
        };
        nodes["name"].AutoNext = "wake";

        nodes["wake"] = new StoryNode(
            "You remember the morning first.",
            "Grey light through thin curtains. Rain ticking on the window.",
            "Mira is asleep beside you, one hand still curled into your shirt like she was scared you'd leave in the night.",
            "Tomorrow her father puts her on a plane, and you will never see her again. He made sure of that. He made sure everyone knew why.",
            "You lay awake all night. And somewhere around dawn, you decided.");
        nodes["wake"].Question = "What do you do?";
        nodes["wake"].Choices.Add(new Choice("Wake her", "talk"));
        nodes["wake"].Choices.Add(new Choice("Go to the window", "window"));

        nodes["window"] = new StoryNode(
            "You slip out of bed and stand at the window.",
            "The street is empty. Your phone sits face down on the sill, full of messages from people who used to love you.",
            "You don't turn it over.",
            "Behind you, the sheets rustle. Mira is awake, watching you like you're something she's trying to memorise.");
        nodes["window"].AutoNext = "talk";

        nodes["talk"] = new StoryNode(
            "\"Hey, you,\" she whispers, voice thick with sleep.",
            "\"I dreamt we were somewhere they couldn't find us.\"",
            "\"Somewhere it didn't matter.\"");
        nodes["talk"].Question = "She's waiting for you to say something.";
        nodes["talk"].Choices.Add(new Choice("Tell her what you've decided", "pact"));
        nodes["talk"].Choices.Add(new Choice("Say nothing. Just hold her", "hold"));

        nodes["hold"] = new StoryNode(
            "You pull her close and press your face into her shoulder. She smells like your shampoo.",
            "For a long time neither of you says anything.",
            "\"You're thinking about it too,\" she says at last. It isn't a question.",
            "You nod. She doesn't let go.");
        nodes["hold"].AutoNext = "pact";

        nodes["pact"] = new StoryNode(
            "\"I don't want to wake up in a world where they've taken you from me.\"",
            "\"Then let's not.\"",
            "Mira laces her fingers through yours.",
            "\"Together?\"",
            "\"Together. Wherever comes next.\"",
            "You choose tonight. You choose the hill where you first kissed. And you choose to spend the rest of the day pretending it's just a day.");
        nodes["pact"].Question = "Before you go...";
        nodes["pact"].Choices.Add(new Choice("Write a letter to your mother", "letter"));
        nodes["pact"].Choices.Add(new Choice("Don't look back", "day"));

        nodes["letter"] = new StoryNode(
            "Your mother hasn't spoken to you since the night she found out.",
            "You sit at her kitchen table one last time, pen in hand, and try to find the words.");
        nodes["letter"].AfterLines = () =>
        {
            letterText = AskText("Dear Mum,");
            if (letterText == "") letterText = "...";
            Say("You fold it twice and leave it under the salt shaker, where she'll find it in the morning.");
        };
        nodes["letter"].AutoNext = "day";

        nodes["day"] = new StoryNode(
            "The day is stupidly, unfairly beautiful.",
            "The rain clears by noon. Mira steals your jacket and refuses to give it back.");
        nodes["day"].Question = "Where do you spend the afternoon?";
        nodes["day"].Choices.Add(new Choice("The bakery where you met", "bakery"));
        nodes["day"].Choices.Add(new Choice("The end of the pier", "pier"));

        nodes["bakery"] = new StoryNode(
            "The old woman behind the counter remembers you. She always does.",
            "Mira orders what she ordered the day you met. Two cinnamon buns - one for her, one she pretends isn't for you.",
            "You laugh so hard you cry, and then you're just crying, and Mira wipes your face with her sleeve and doesn't ask why.");
        nodes["bakery"].AutoNext = "dusk";

        nodes["pier"] = new StoryNode(
            "You sit at the end of the pier with your legs over the edge, watching the gulls fight over chips.",
            "Mira names every boat that goes past. Apparently they're all named after you.",
            "\"In the next one,\" she says, \"I'm going to marry you. Big ugly dress. And everyone's going to come.\"",
            "\"Everyone,\" you agree.");
        nodes["pier"].AutoNext = "dusk";

        nodes["dusk"] = new StoryNode(
            "By the time you reach the hill, the sky has gone the colour of a bruise.",
            "Below you the town lights flicker on one by one, like it's saying goodbye.",
            "You lie down together in the long grass. Mira is shaking. So are you.",
            "This is it.");
        nodes["dusk"].Question = "In the last moment, you...";
        nodes["dusk"].Choices.Add(new Choice("Take her hand", "together"));
        nodes["dusk"].Choices.Add(new Choice("Close your eyes and let go first", "apart"));

        // --- ending 1: together ---
        nodes["together"] = new StoryNode(
            "You find her hand in the dark. Her fingers close around yours, tight and certain.",
            "She whispers something, so quietly you almost miss it.");
        nodes["together"].OnEnter = () =>
        {
            CloseArt("you");
            OpenArt("grave", 900, 150);
        };
        nodes["together"].AfterLines = () =>
        {
            Glitch("\"I'm scared.\"", "\"I'm not scared anymore.\"");
            Console.ReadLine();
            Say("The stars come out. Then, slowly, everything else goes out.");
            Say("...");
        };
        nodes["together"].AutoNext = "together_rise";

        nodes["together_rise"] = new StoryNode(
            "And then there is light.",
            "You rise, and she rises with you, her hand still in yours, the town falling away beneath you like something you've outgrown.",
            "\"See?\" Mira says, laughing, crying, glowing. \"Nobody can take me now.\"",
            "Then go, both of you. Time no longer concerns you.");
        nodes["together_rise"].OnEnter = () =>
        {
            CloseArt("grave");
            OpenArt("heaven", 900, 150);
        };
        nodes["together_rise"].AfterLines = Epilogue;
        nodes["together_rise"].Ending = "Together";

        // --- ending 2: apart ---
        nodes["apart"] = new StoryNode(
            "You close your eyes before she does. You tell yourself it's braver this way.",
            "You don't feel her hand reach for yours.",
            "You don't feel it find nothing.");
        nodes["apart"].OnEnter = () =>
        {
            CloseArt("you");
            OpenArt("grave", 900, 150);
        };
        nodes["apart"].AutoNext = "apart_rise";

        nodes["apart_rise"] = new StoryNode(
            "There is light, and you rise into it.",
            "But you rise alone.");
        nodes["apart_rise"].OnEnter = () =>
        {
            CloseArt("grave");
            OpenArt("alone", 900, 150);
        };
        nodes["apart_rise"].AfterLines = () =>
        {
            Glitch("Mira. Mira. Mira.", ".................");
            Console.ReadLine();
            Say("She went on ahead of you, child. Or perhaps she is still behind.");
            Say("Time no longer concerns you... but distance, it seems, still does.");
            Epilogue();
        };
        nodes["apart_rise"].Ending = "Apart";
    }

    // only shows if the player wrote the letter earlier
    static void Epilogue()
    {
        if (letterText == "") return;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Say("Far below, your mother finds a letter under the salt shaker.");
        Say("\"Dear Mum, " + letterText + "\"");
        Say("She reads it until the sun comes up.");
        Console.ResetColor();
    }

    // Walks the graph: print the node, ask for a choice, follow the edge to the next id.
    // Keeps going until it lands on a leaf node (an ending).
    static void Play(string id)
    {
        List<string> path = new List<string>();   // remembers the choices made so we can print the route at the end
        StoryNode current = null;

        while (true)
        {
            if (!nodes.TryGetValue(id, out current))
            {
                Console.WriteLine(" [Missing scene: " + id + "]");
                break;
            }

            if (current.OnEnter != null) current.OnEnter();

            foreach (string line in current.Lines)
                Say(line);

            if (current.AfterLines != null) current.AfterLines();

            if (current.IsEnding) break;

            // linear scene, no decision needed
            if (current.AutoNext != null)
            {
                id = current.AutoNext;
                continue;
            }

            Choice picked = AskChoice(current);
            path.Add(picked.Text);
            id = picked.Next;
            Console.WriteLine();
        }

        // count the leaves so the player knows how many endings there are to find
        int totalEndings = 0;
        foreach (StoryNode n in nodes.Values)
            if (n.IsEnding) totalEndings++;

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("\n ~ THE END ~");
        Console.ResetColor();
        if (current != null && current.Ending != null)
            Console.WriteLine(" Ending: " + current.Ending + "  (one of " + totalEndings + ")");
        Console.WriteLine(" Your choices: " + string.Join(" -> ", path));
    }

    // Shows the numbered options and keeps asking until the input is valid.
    // Accepts the number, the full option text, or just the first letter.
    static Choice AskChoice(StoryNode node)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(" " + node.Question);
        Console.ResetColor();
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

    // types a line out, then waits for Enter so the player reads at their own pace
    static void Say(string line)
    {
        Console.Write(" ");
        Type(line.Replace("{name}", playerName));
        Console.WriteLine();
        Console.ReadLine();
    }

    static void Type(string text, int speed = 30)
    {
        foreach (char ch in text)
        {
            Console.Write(ch);
            Thread.Sleep(speed);
        }
    }

    // scrambles one line of text into another, used for the big emotional beats
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

    // Art windows: launches a second copy of the exe in its own conhost window with --art.
    // We pass our window handle + process id so it can hand focus back and close itself when we exit.
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

        // just sit here until the main story process closes
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
