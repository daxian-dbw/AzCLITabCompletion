namespace AzCLI.Help.Parser;

internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello World!");
    }
}

public class Option
{
    public string Long { get; }
    public char? Short { get; }
    public string Description { get; }
    public List<string> Arguments { get; set; }

    public Option(string longName, string description, char? shortName)
    {
        ArgumentException.ThrowIfNullOrEmpty(longName);
        ArgumentException.ThrowIfNullOrEmpty(description);
        if (shortName is not null)
        {
            char value = shortName.Value;
            if (value < 'A' || value > 'z' || (value > 'Z' && value < 'a'))
            {
                throw new ArgumentException("The short name should be a letter between 'A-Z' or 'a-z'", nameof(shortName));
            }
        }

        Long = longName;
        Short = shortName;
        Description = description;
        Arguments = null;
    }
}

public abstract class CommandBase
{
    public string Name { get; }
    public string Description { get; }

    protected CommandBase(string name, string description)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);

        Name = name;
        Description = description;
    }
}

public sealed class Group : CommandBase
{
    public List<string> SubGroups { get; }
    public List<Command> Commands { get; }

    public Group(string name, string description, List<CommandBase> commands)
        : base(name, description)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentOutOfRangeException.ThrowIfZero(commands.Count);

        Commands = commands;
    }
}

public sealed class Command : CommandBase
{
    public List<Option> Options { get; }
    public string Examples { get; }

    public Command(string name, string description, List<Option> options, string examples)
        : base(name, description)
    {
        ArgumentNullException.ThrowIfNull(options);

        Options = options;
        Examples = examples;
    }
}

public class HelpParser
{
    public Group ParseGroup()
}
