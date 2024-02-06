using System.Text.Json;

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

public enum EntryType
{
    Group,
    Command
}

public class EntryInfo
{
    public string Name { get; }
    public string Description { get; }
    public EntryType Type { get; }

    public EntryInfo(string name, string description, EntryType type)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);

        Name = name;
        Description = description;
        Type = type;
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
    private readonly string _path;
    private readonly Dictionary<string, Tuple<EntryInfo, CommandBase>> _commands;
    public List<EntryInfo> EntryInfos { get; }

    public Group(string name, string description, string path, List<EntryInfo> entryInfos)
        : base(name, description)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(entryInfos);
        ArgumentOutOfRangeException.ThrowIfZero(entryInfos.Count);

        _path = path;
        _commands = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entryInfo in entryInfos)
        {
            _commands.Add(entryInfo.Name, Tuple.Create(entryInfo, (CommandBase)null));
        }

        EntryInfos = entryInfos;
    }

    public CommandBase GetChildCommand(string name)
    {
        if (_commands.TryGetValue(name, out var tuple))
        {
            var entryInfo = tuple.Item1;
            if (entryInfo.Type is EntryType.Group)
            {
                string groupPath = Path.Combine(_path, name);
                string file = Path.Combine(groupPath, "group_entries.json");
                var entries = JsonSerializer.Deserialize<List<EntryInfo>>(File.OpenRead(file));
                return new Group(entryInfo.Name, entryInfo.Description, groupPath, entries);
            }

            string commandPath = Path.Combine(_path, $"{name}.json");
            return JsonSerializer.Deserialize<Command>(File.OpenRead(commandPath));
        }

        return null;
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
