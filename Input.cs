namespace transferIt;

public static class Input
{
  // Keeps asking until the user types a valid port (1-65535).
  public static int askPort(string prompt)
  {
    while (true)
    {
      Console.Write(prompt);
      string? input = Console.ReadLine();
      if (input is null) Environment.Exit(1); // stdin closed

      if (int.TryParse(input, out int p) && p >= 1 && p <= 65535)
        return p;

      Console.Error.WriteLine("[ERROR]: Enter a number between 1 and 65535.");
    }
  }
}
