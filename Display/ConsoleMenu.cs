using System;

namespace DeliverySimulator.Display;

public static class ConsoleMenu
{
    /// <summary>
    /// Nyílbillentyűs menü. Visszaadja a kiválasztott indexet (0..N-1), vagy -1-et, ha Esc-et nyomtak.
    /// </summary>
    public static int Show(string title, IReadOnlyList<string> items, int selectedIndex = 0)
    {
        if (items == null || items.Count == 0)
            throw new ArgumentException("A menünek legalább egy elemet tartalmaznia kell.", nameof(items));

        int index = Math.Clamp(selectedIndex, 0, items.Count - 1);
        ConsoleKey key;

        Console.CursorVisible = false;
        try
        {
            while (true)
            {
                Console.Clear();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(title);
                    Console.ResetColor();
                    Console.WriteLine();
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (i == index)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkCyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.Write("> ");
                    }
                    else
                    {
                        Console.ResetColor();
                        Console.Write("  ");
                    }

                    Console.WriteLine(items[i]);
                    Console.ResetColor();
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Fel/Le: mozgatás, Enter: választás, Esc: vissza/kilépés");
                Console.ResetColor();

                key = Console.ReadKey(intercept: true).Key;

                switch (key)
                {
                    case ConsoleKey.UpArrow:
                        index = (index - 1 + items.Count) % items.Count;
                        break;
                    case ConsoleKey.DownArrow:
                        index = (index + 1) % items.Count;
                        break;
                    case ConsoleKey.Enter:
                        Console.Clear();
                        Console.CursorVisible = true;
                        return index;
                    case ConsoleKey.Escape:
                        Console.Clear();
                        Console.CursorVisible = true;
                        return -1;
                }
            }
        }
        finally
        {
            Console.ResetColor();
            Console.CursorVisible = true;
        }
    }
}
