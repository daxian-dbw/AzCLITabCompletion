using System.Collections;
using System.Text.Json;

namespace AzCLI.Abstraction;

public class Option
{
    public string Name { get; }
    public string[] Alias { get; }
    public string[] Short { get; }
    public string Attribute { get; }
    public string Description { get; set; }
    public List<string> Arguments { get; set; }

    public Option(string name, string description, string[] alias, string[] @short, string attribute, List<string> arguments)
    {
        Utils.ThrowIfNullOrEmptyString(name, nameof(name));
        Utils.ThrowIfNullOrEmptyString(description, nameof(description));

        Name = name;
        Alias = alias;
        Short = @short;
        Attribute = attribute;
        Description = description;
        Arguments = arguments;
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
    public EntryType Type { get; }
    public string Attribute { get; }
    public string Description { get; set; }

    public EntryInfo(string name, EntryType type, string description, string attribute)
    {
        Utils.ThrowIfNullOrEmptyString(name, nameof(name));
        Utils.ThrowIfNullOrEmptyString(description, nameof(description));

        Name = name;
        Type = type;
        Description = description;
        Attribute = attribute;
    }
}

public class EntryPair
{
    public EntryInfo Info { get; }
    public CommandBase Command { set; get; }

    public EntryPair(EntryInfo info)
    {
        Utils.ThrowIfNull(info, nameof(info));
        Info = info;
    }
}

public abstract class CommandBase
{
    public string Name { get; }
    public string Description { get; }

    protected CommandBase(string name, string description)
    {
        Utils.ThrowIfNullOrEmptyString(name, nameof(name));
        Utils.ThrowIfNullOrEmptyString(description, nameof(description));

        Name = name;
        Description = description;
    }
}

public sealed class Group : CommandBase
{
    private readonly string _path;
    private readonly Dictionary<string, EntryPair> _commands;
    public List<EntryInfo> EntryInfos { get; }

    public Group(string name, string description, string path, List<EntryInfo> entryInfos)
        : base(name, description)
    {
        Utils.ThrowIfNullOrEmptyString(path, nameof(path));
        Utils.ThrowIfNullOrEmptyList(entryInfos, nameof(entryInfos));

        _path = path;
        _commands = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entryInfo in entryInfos)
        {
            _commands.Add(entryInfo.Name, new EntryPair(entryInfo));
        }

        EntryInfos = entryInfos;
    }

    public CommandBase GetChildCommand(string name)
    {
        if (_commands.TryGetValue(name, out var pair))
        {
            if (pair.Command is not null)
            {
                return pair.Command;
            }

            var entryInfo = pair.Info;
            if (entryInfo.Type is EntryType.Group)
            {
                string groupPath = Path.Combine(_path, name);
                string file = Path.Combine(groupPath, $"{name}-entries.json");
                var entries = JsonSerializer.Deserialize<List<EntryInfo>>(File.OpenRead(file));
                pair.Command = new Group(entryInfo.Name, entryInfo.Description, groupPath, entries);
            }
            else
            {
                string commandPath = Path.Combine(_path, $"{name}.json");
                pair.Command = JsonSerializer.Deserialize<Command>(File.OpenRead(commandPath));
            }

            return pair.Command;
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
        Utils.ThrowIfNullOrEmptyList(options, nameof(options));

        Options = options;
        Examples = examples;
    }

    public Option FindOption(string name)
    {
        foreach (Option option in Options)
        {
            if (name.StartsWith("--"))
            {
                if (string.Equals(option.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }

                if (option.Alias is not null)
                {
                    foreach (string alias in option.Alias)
                    {
                        if (string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
                        {
                            return option;
                        }
                    }
                }
            }
            else if (option.Short is not null)
            {
                foreach (string s in option.Short)
                {
                    if (string.Equals(s, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return option;
                    }
                }
            }
        }

        return null;
    }
}

internal class Utils
{
    internal static void ThrowIfNullOrEmptyString(string value, string paramName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Argument cannot be null or an empty string.", paramName);
        }
    }

    internal static void ThrowIfNullOrEmptyList(IList value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName, "Argument cannot be null.");
        }

        if (value.Count is 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "Argument cannot not be an empty list.");
        }
    }

    internal static void ThrowIfNull(object value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName, "Argument cannot be null.");
        }
    }
}
