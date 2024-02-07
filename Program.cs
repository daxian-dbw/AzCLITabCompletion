using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    public EntryType Type { get; }
    public string Attribute { get; }
    public string Description { get; set; }

    public EntryInfo(string name, EntryType type, string description, string attribute)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);

        Name = name;
        Type = type;
        Description = description;
        Attribute = attribute;
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

public partial class HelpParser
{
    private readonly Regex _pattern = MatchCommandRegex();

    private static List<string> GetHelpText(string argument)
    {
        try
        {
            using var process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = "az",
                    Arguments = $"{argument} --help",
                    RedirectStandardOutput = true,
                }
            };

            process.Start();
            List<string> output = [];

            string line;
            while ((line = process.StandardOutput.ReadLine()) is not null)
            {
                output.Add(line);
            }

            process.WaitForExit();
            return output;
        }
        catch (Win32Exception e)
        {
            throw new ApplicationException($"Failed to run 'az {argument}': {e.Message}", e);
        }
    }

    private static string GetContinuedDescription(string line, int descIndex)
    {
        if (line.Length > descIndex)
        {
            bool prefixedWithSpaces = true;
            for (int i = 0; i < descIndex; i++)
            {
                if (line[i] is not ' ')
                {
                    prefixedWithSpaces = false;
                    break;
                }
            }

            if (prefixedWithSpaces && line[descIndex] is not ' ')
            {
                return line.Trim();
            }
        }

        return null;
    }

    public void ParseGroup(string group, string baseCommand, string path)
    {
        string command = $"{baseCommand} {group}";
        string newPath = Path.Combine(path, group);

        List<string> help = GetHelpText(command);
        Directory.CreateDirectory(newPath);

        bool inSubgroups = false, inCommands = false;
        int descIndex = -1;
        List<EntryInfo> entries = [];

        foreach (string line in help)
        {
            string currLine = line.TrimEnd();
            if (currLine.Equals("Subgroups:", StringComparison.Ordinal))
            {
                // Getting to the 'Subgroups' section.
                inSubgroups = true;
                continue;
            }

            if (currLine.Equals("Commands:", StringComparison.Ordinal))
            {
                // Getting to the 'Commands' section.
                inCommands = true;
                continue;
            }

            if (inSubgroups || inCommands)
            {
                if (currLine == string.Empty)
                {
                    // Reached the end of a section.
                    inSubgroups = inCommands = false;
                    continue;
                }

                Match match = _pattern.Match(currLine);
                if (match.Success)
                {
                    entries.Add(new EntryInfo(
                        name: match.Groups["name"].Value,
                        type: inSubgroups ? EntryType.Group : EntryType.Command,
                        description: match.Groups["desc"].Value,
                        attribute: match.Groups.TryGetValue("attr", out var v) ? v.Value : null));

                    descIndex = match.Groups["desc"].Index;
                    continue;
                }

                string continuedDesc = GetContinuedDescription(currLine, descIndex);
                if (continuedDesc is not null)
                {
                    EntryInfo entry = entries[^1];
                    entry.Description += $" {continuedDesc}";
                }
            }
        }
    }

    [GeneratedRegex(@"^ {4}(?<name>[a-z0-9-]+) +(?<attr>\[[a-zA-Z]+\] )?\: (?<desc>\w+.*)$")]
    private static partial Regex MatchCommandRegex();

    [GeneratedRegex(@"^ {4}(?<long>--[a-z-]+) (?<alias>-[a-z]|--[a-z-]+)? +(?<attr>\[[a-zA-Z]+\] )?\: (?<desc>\w+.*)$")]
    private static partial Regex MatchOptionRegex();
}
