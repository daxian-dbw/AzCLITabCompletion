using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Core;
using Spectre.Console;
using AzCLI.Abstraction;

namespace AzCLI.HelpParser;

internal class Program
{
    static void Main(string[] args)
    {
        // Console.WriteLine($"attach to {Environment.ProcessId}");
        // while (!Debugger.IsAttached)
        // {
        //     Thread.Sleep(300);
        // }
        // Debugger.Break();

        Console.OutputEncoding = Encoding.UTF8;
        RunRemaining();
        // RunAll();
    }

    static void RunRemaining()
    {
        using FileStream stream = File.OpenRead(@"E:\yard\tmp\repro\az\az-entries.json");
        List<EntryInfo> entries = JsonSerializer.Deserialize<List<EntryInfo>>(stream);
        HashSet<string> alreadyParsed =
        [
            "account","acr","ad","advisor","afd","aks","ams","apim","appconfig","appservice","aro","backup","batch","bicep","billing","bot","cache","capacity","cdn","cloud","cognitiveservices","config","connection","consumption","container","containerapp","cosmosdb","databoxedge","deployment","deployment-scripts","disk","disk-access","disk-encryption-set","dla","dls","dms","eventgrid","eventhubs","extension","feature","functionapp","group","hdinsight","identity","image","iot","keyvault","kusto","lab","lock","logicapp","managed-cassandra","managedapp","managedservices","maps","mariadb","monitor","mysql","netappfiles","network","policy","postgres","ppg","private-link","provider","redis","relay","resource","resourcemanagement","restore-point","role","search","security","servicebus"
        ];

        using HelpParser parser = new(@"C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd");
        AnsiConsole.Status()
            .AutoRefresh(true)
            .Start("Starting ...", ctx =>
            {
                Stopwatch watch = new();
                watch.Start();
                try
                {
                    parser.StatusContext = ctx;
                    foreach (EntryInfo entry in entries)
                    {
                        if (entry.Type is EntryType.Group && !alreadyParsed.Contains(entry.Name))
                        {
                            parser.ParseGroup(entry.Name, baseCommand: "", path: @"E:\yard\tmp\repro\az");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"Exception caught: {ex.Message}\n{ex.StackTrace}");
                }
                finally
                {
                    parser.StatusContext = null;
                }

                watch.Stop();
                AnsiConsole.MarkupLine($"\nTotal time: {watch.Elapsed.TotalMinutes} minutes");
            });
    }

    static void RunAll()
    {
        using HelpParser parser = new(@"C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd", noExample: true);
        AnsiConsole.Status()
            .AutoRefresh(true)
            .Start("Starting ...", ctx =>
            {
                Stopwatch watch = new();
                watch.Start();
                try
                {
                    parser.StatusContext = ctx;
                    parser.ParseGroup(group: "az", baseCommand: "", path: @"E:\yard\tmp\repro");
                    // parser.ParseGroup(group: "migration", baseCommand: "servicebus", path: @"E:\yard\tmp");
                    // parser.ParseCommand("create", "Create an Azure Virtual Machine.", "vm", @"E:\yard\tmp");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"Exception caught: {ex.Message}\n{ex.StackTrace}");
                }
                finally
                {
                    parser.StatusContext = null;
                }

                watch.Stop();
                AnsiConsole.MarkupLine($"\nTotal time: {watch.Elapsed.TotalMinutes} minutes");
            });
    }
}

public sealed partial class HelpParser : IDisposable
{
    /// <summary>
    /// We may not want to include all examples considering the size.
    /// From my private run, parsing all help content of `az` including examples generates 32.6 MB data,
    /// which is too big. Given that we are not using examples any time soon, we probably should not
    /// include them in the initial az completion module.
    /// </summary>
    private readonly bool _includeExample;
    private readonly string _azCmd;
    private readonly string _tempDir;
    private readonly string _tempFile;
    private readonly Regex _commandRegex;
    private readonly Regex _optionRegex;
    private readonly Regex _allowedValuesRegex;
    private readonly Logger _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// The status context used to update the status on the spinner while parsing the help content.
    /// </summary>
    internal StatusContext StatusContext { get; set; }

    public HelpParser(string azExePath, bool noExample = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(azExePath);

        _azCmd = azExePath;
        _commandRegex = MatchCommandRegex();
        _optionRegex = MatchOptionRegex();
        _allowedValuesRegex = MatchAllowedValuesRegex();
        _includeExample = !noExample;

        _tempFile = Path.GetTempFileName();
        _tempDir = $"{_tempFile}.az";
        Directory.CreateDirectory(_tempDir);

        string logDir = Path.GetDirectoryName(typeof(HelpParser).Assembly.Location);
        string logFile = Path.Combine(logDir, "log1.txt");
        _logger = new LoggerConfiguration()
            .WriteTo.File(logFile)
            .CreateLogger();
        _logger.Information("Logger started");

        // We want the generated JSON content to be friendly to human readers too.
        _jsonOptions = new JsonSerializerOptions() { WriteIndented = true };
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir);
        }

        _logger.Dispose();
    }

    private List<string> GetHelpText(string subCommand)
    {
        try
        {
            using var process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{_azCmd}\" {subCommand} --help",
                    UseShellExecute = false,
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
            throw new ApplicationException($"Failed to run 'az {subCommand}': {e.Message}", e);
        }
    }

    private List<string> GetArgumentValues(string subCommand)
    {
        try
        {
            string commandLine = $"az {subCommand}";

            using var process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{_azCmd}\"",
                    UseShellExecute = false,
                    WorkingDirectory = _tempDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            var env = process.StartInfo.Environment;
            env.Add("ARGCOMPLETE_USE_TEMPFILES", "1");
            env.Add("_ARGCOMPLETE_STDOUT_FILENAME", _tempFile);
            env.Add("COMP_LINE", commandLine);
            env.Add("COMP_POINT", (commandLine.Length + 1).ToString());
            env.Add("_ARGCOMPLETE", "1");
            env.Add("_ARGCOMPLETE_SUPPRESS_SPACE", "0");
            env.Add("_ARGCOMPLETE_IFS", "\n");
            env.Add("_ARGCOMPLETE_SHELL", "powershell");

            process.Start();
            process.WaitForExit();

            string line;
            using FileStream stream = File.OpenRead(_tempFile);
            if (stream.Length is 0)
            {
                // No allowed values for the option.
                return null;
            }

            using StreamReader reader = new(stream);
            List<string> output = [];

            while ((line = reader.ReadLine()) is not null)
            {
                if (line.StartsWith('-'))
                {
                    // Argument completion generates incorrect results -- options are written into the file instead of argument allowed values.
                    _logger.Information("Unexpected completion results for `{0}`: options returned while expecting argument values. Example: {1}", commandLine, line);
                    return null;
                }

                string value = line.Trim();
                if (value != string.Empty)
                {
                    output.Add(value);
                }
            }

            return output;
        }
        catch (Win32Exception e)
        {
            throw new ApplicationException($"Failed to get allowed values for 'az {subCommand}': {e.Message}", e);
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
        string command = group is "az"
            ? string.Empty
            : baseCommand is "" ? group : $"{baseCommand} {group}";
        string newPath = Path.Combine(path, group);

        List<string> help = GetHelpText(command);
        Directory.CreateDirectory(newPath);

        StatusContext?.Status($"Parsing group 'az {command}'");

        bool inSubgroups = false, inCommands = false;
        int descIndex = -1;
        List<EntryInfo> entries = [];

        foreach (string line in help)
        {
            string currLine = line.TrimEnd();
            if (currLine.Length > 0 && currLine[0] is not ' ')
            {
                if (currLine.Equals("Subgroups:", StringComparison.Ordinal))
                {
                    // Getting to the 'Subgroups' section.
                    inSubgroups = true;
                }
                else if (currLine.Equals("Commands:", StringComparison.Ordinal))
                {
                    // Getting to the 'Commands' section.
                    inCommands = true;
                }

                // Ignore the current line.
                continue;
            }

            if (inSubgroups || inCommands)
            {
                if (currLine == string.Empty)
                {
                    // Reached the end of a section.
                    inSubgroups = inCommands = false;
                    descIndex = -1;
                    continue;
                }

                if (currLine.Contains(" [Deprecated] ", StringComparison.OrdinalIgnoreCase))
                {
                    // Ignore deprecated command/group.
                    _logger.Information("Deprecated command/group found.\n  Command: {0}\n  Line:{1}", command, currLine);
                    descIndex = -1;
                    continue;
                }

                Match match = _commandRegex.Match(currLine);
                if (match.Success)
                {
                    entries.Add(new EntryInfo(
                        name: match.Groups["name"].Value,
                        type: inSubgroups ? EntryType.Group : EntryType.Command,
                        description: match.Groups["desc"].Value,
                        attribute: match.Groups["attr"].Value.Trim()));

                    descIndex = match.Groups["desc"].Index;
                    continue;
                }

                if (currLine.Contains(" : "))
                {
                    _logger.Error("Failed to match a command/group.\n  Command: {0}\n  Line:{1}", command, currLine);
                }

                string continuedDesc = descIndex > 0 ? GetContinuedDescription(currLine, descIndex) : null;
                if (continuedDesc is not null)
                {
                    EntryInfo entry = entries[^1];
                    entry.Description += $" {continuedDesc}";
                }
            }
        }

        string jsonFile = Path.Join(newPath, $"{group}-entries.json");
        using FileStream stream = File.OpenWrite(jsonFile);
        JsonSerializer.Serialize(stream, entries, _jsonOptions);

        foreach (EntryInfo entry in entries)
        {
            if (entry.Type is EntryType.Command)
            {
                ParseCommand(entry.Name, entry.Description, command, newPath);
            }
        }

        foreach (EntryInfo entry in entries)
        {
            if (entry.Type is EntryType.Group)
            {
                ParseGroup(entry.Name, command, newPath);
            }
        }
    }

    public void ParseCommand(string name, string description, string baseCommand, string path)
    {
        string command = baseCommand is "" ? name : $"{baseCommand} {name}";
        List<string> help = GetHelpText(command);

        StatusContext?.Status($"Parsing command 'az {command}': options");

        bool inArgSection = false, inExampleSection = false;
        int i = 0, descIndex = -1;
        List<Option> options = [];

        for (; i < help.Count; i++)
        {
            string line = help[i].TrimEnd();
            if (line.Length > 0 && line[0] is not ' ')
            {
                if (line.EndsWith("Arguments", StringComparison.Ordinal))
                {
                    // Getting to an argument section.
                    inArgSection = true;
                }
                else if (line.Equals("Examples", StringComparison.Ordinal))
                {
                    // Getting to the 'Examples' section.
                    inExampleSection = true;
                    break;
                }

                // Ignore the current line.
                continue;
            }

            if (inArgSection)
            {
                if (line == string.Empty)
                {
                    // Reached the end of a section.
                    inArgSection = false;
                    descIndex = -1;
                    continue;
                }

                if (line.Contains(" [Deprecated] ", StringComparison.OrdinalIgnoreCase))
                {
                    // Ignore deprecated options.
                    _logger.Information("Deprecated option found.\n  Command: {0}\n  Line:{1}", command, line);
                    descIndex = -1;
                    continue;
                }

                Match match = _optionRegex.Match(line);
                if (match.Success)
                {
                    string[] alias = null;
                    if (match.Groups["alias"].Success)
                    {
                        var gp = match.Groups["alias"];
                        alias = new string[gp.Captures.Count];
                        for (int k = 0; k < gp.Captures.Count; k++)
                        {
                            alias[k] = gp.Captures[k].Value;
                        }
                    }

                    string[] shorts = null;
                    if (match.Groups["short"].Success)
                    {
                        var gp = match.Groups["short"];
                        shorts = new string[gp.Captures.Count];
                        for (int k = 0; k < gp.Captures.Count; k++)
                        {
                            shorts[k] = gp.Captures[k].Value;
                        }
                    }

                    var option = new Option(
                        name: match.Groups["long"].Value,
                        description: match.Groups["desc"].Value,
                        alias: alias,
                        @short: shorts,
                        attribute:match.Groups["attr"].Value.Trim(),
                        arguments: null
                    );

                    options.Add(option);
                    descIndex = match.Groups["desc"].Index;
                    continue;
                }

                if (line.Trim().StartsWith("--") && line.Contains(" : "))
                {
                    _logger.Error("Failed to match the option.\n  Command: {0}\n  Line:{1}", command, line);
                }

                string continuedDesc = descIndex > 0 ? GetContinuedDescription(line, descIndex) : null;
                if (continuedDesc is not null)
                {
                    Option option = options[^1];
                    option.Description += $" {continuedDesc}";
                }
            }
        }

        StatusContext?.Status($"Parsing command 'az {command}': option args");

        // Populate the arguments for each option.
        foreach (Option option in options)
        {
            // The help content has very good descriptions for an option, including the allowed values for it.
            string desc = option.Description;
            bool runAzCompletion = false;
            if (desc.Contains("Allowed values: ", StringComparison.OrdinalIgnoreCase))
            {
                if (desc.Contains("Allowed values: false, true.", StringComparison.OrdinalIgnoreCase) ||
                    desc.Contains("Allowed values: true, false.", StringComparison.OrdinalIgnoreCase))
                {
                    // No need to specify argument for a switch option.
                    continue;
                }

                Match match = _allowedValuesRegex.Match(desc);
                if (match.Success)
                {
                    string[] args = match.Groups["args"].Value.Split(",", StringSplitOptions.TrimEntries);
                    option.Arguments = new List<string>(args);
                }
                else
                {
                    // Something unexpected. Let's log it here and run az completion to get possible args.
                    _logger.Information("Failed to match the allowed values.\n  Command: {0}\n  Option: {1}\n  Description: {2}", command, option.Name, desc);
                    runAzCompletion = true;
                }
            }
            else if (desc.Contains(" Values from: ", StringComparison.OrdinalIgnoreCase))
            {
                runAzCompletion = true;
            }

            if (runAzCompletion)
            {
                // Use the tab completion functionality of 'az' to get the allowed values, if there are any.
                option.Arguments = GetArgumentValues($"{command} {option.Name}");
            }
        }

        // We may not include any examples in our data, to reduce the overall size.
        string examples = null;
        if (_includeExample && inExampleSection)
        {
            StatusContext?.Status($"Parsing command 'az {command}': examples");

            bool inAnExample = false, inCode = false;
            int consecutiveEmptyLineCount = 0, exampleCount = 0;
            StringBuilder text = new();

            for (i++; i < help.Count; i++)
            {
                string line = help[i].TrimEnd();
                if (line == string.Empty)
                {
                    consecutiveEmptyLineCount++;
                    if (consecutiveEmptyLineCount is 2)
                    {
                        // An example ends
                        if (inCode)
                        {
                            inCode = false;
                            text.Append("\n```\n\n");
                        }

                        inAnExample = false;
                        consecutiveEmptyLineCount = 0;
                    }
                    continue;
                }

                if (inCode && consecutiveEmptyLineCount is 1)
                {
                    // We are still in a code block, but the previous line is an empty line in the code block.
                    text.Append("\n\n");
                }
                consecutiveEmptyLineCount = 0;

                if (line.Length > 4 && line.StartsWith("    ") && line[4] is not ' ')
                {
                    // This is a line of description for the example.
                    if (inAnExample)
                    {
                        text.Append(' ').Append(line.TrimStart());
                    }
                    else
                    {
                        inAnExample = true;
                        exampleCount++;
                        text.Append($"{exampleCount}. {line.TrimStart()}");
                    }
                }
                else if (line.Length > 8 && line.StartsWith("        "))
                {
                    // We don't add the new line at the end of the current line.
                    // Instead, we add new line to the previous line when needed.
                    if (line[8] is not ' ')
                    {
                        // This is a line of code for the example
                        if (inCode)
                        {
                            if (text[^1] is not '\n')
                            {
                                text.Append(' ');
                            }
                            text.Append(line.TrimStart());
                        }
                        else
                        {
                            inCode = true;
                            text.Append("\n```sh\n")
                                .Append(line.TrimStart());
                        }
                    }
                    else
                    {
                        // this is a continuation line of code.
                        text.Append('\n')
                            .Append(' ', 3)
                            .Append(line.TrimStart());
                    }
                }
            }

            // Replace the line continuation operator.
            text.Replace(" \\\n", " `\n");
            examples = text.ToString();
        }

        try
        {
            var cmd = new Command(name, description, options, examples);
            string jsonFile = Path.Combine(path, $"{name}.json");
            using FileStream stream = File.OpenWrite(jsonFile);
            JsonSerializer.Serialize(stream, cmd, _jsonOptions);
        }
        catch (Exception e)
        {
            _logger.Error("Failed to handle command '{0}'\n  Base Command: {1}\n  Error: {2},", name, baseCommand, e.Message);
        }
    }

    /// <summary>
    /// This is to match commands and groups.
    ///   - For command name, valid characters are 'a-z', '0-9', '-', and '_'.
    ///   - For attribute, it starts with '[' and ends with ']', valid characters are 'a-z' and 'A-Z'.
    ///   - For description, it starts with a non-space character and can be followed by arbitrary texts.
    /// </summary>
    [GeneratedRegex(@"^ {4}(?<name>[a-z0-9-_]+) +(?<attr>\[[a-zA-Z]+\] )?\: (?<desc>[^\s].*)$")]
    private static partial Regex MatchCommandRegex();

    /// <summary>
    /// This is to match options.
    ///   - For the option long name, valid characters are 'a-z', 'A-Z', '0-9', '_', and '-'. A couple options have a comma right after the long name.
    ///   - For the alias, there could be 0 or N aliases defined. Valid characters are the same as the long name.
    ///   - For the short name, there could be 0 or N short names. Valid characters are 'a-z', and 'A-Z'.
    ///   - For the attribute, it starts with '[' and ends with ']', valid characters are 'a-z' and 'A-Z'.
    ///   - For the description, it starts with a non-space character and can be followed by arbitrary texts.
    /// </summary>
    /// <returns></returns>
    [GeneratedRegex(@"^ {4}(?<long>--[\w-]+),? (?<alias>--[\w-]+ )*(?<short>-[a-zA-Z] )* *(?<attr>\[[a-zA-Z]+\] )?\: (?<desc>[^\s].*)$")]
    private static partial Regex MatchOptionRegex();

    /// <summary>
    /// This is to match allowed values of an option.
    ///   - It's mostly 'Allowed values', but a couple options starts with the lower case 'a'.
    ///   - It mostly prefixed with a space, but a couple options only have "Allowed value: ..." in its description, so in that case we need to match the start of sentence.
    ///   - For the arguments, it starts with a non-space character and can be followed characters defined below.
    /// </summary>
    /// <returns></returns>
    [GeneratedRegex(@"(?:^| )(?:a|A)llowed values: (?<args>[^\s][\w, -/~\*]*)\.")]
    private static partial Regex MatchAllowedValuesRegex();
}
