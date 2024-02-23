using System.Text.Json;
using System.Management.Automation;
using System.Management.Automation.Language;
using AzCLI.Abstraction;

namespace AzCLI.Completion;

public class AzCompletion
{
    public static AzCompletion s_singleton;

    private readonly string _root;
    private readonly Group _azGroup;

    private AzCompletion(string path)
    {
        _root = path;
        _azGroup = GetRootGroup();
    }

    private Group GetRootGroup()
    {
        string file = Path.Combine(_root, "az-entries.json");
        var entries = JsonSerializer.Deserialize<List<EntryInfo>>(File.OpenRead(file));
        return new Group("az", "az cli root command", _root, entries);
    }

    public static AzCompletion GetSingleton(string rootPath)
    {
        s_singleton ??= new AzCompletion(rootPath);
        return s_singleton;
    }

    public List<CompletionResult> GetCompletions(string wordToComplete, CommandAst commandAst, int cursorPosition)
    {
        // If 'wordToComplete' starts with dash, then we consider it a parameter completion,
        // unless it's actually a negative value.
        bool argCompletion = !wordToComplete.StartsWith("-") || int.TryParse(wordToComplete, out _);
        var elements = commandAst.CommandElements;

        int i = 0;
        for (; i < elements.Count; i++)
        {
            if (elements[i].Extent.EndOffset >= cursorPosition)
            {
                break;
            }
        }

        int lastCommandIndex = 0;
        CommandBase current = _azGroup;
        for (int j = 1; j < i; j++)
        {
            var element = elements[j];
            if (element is CommandParameterAst)
            {
                // We've gone past the last command.
                lastCommandIndex = j - 1;
                break;
            }

            if (element is StringConstantExpressionAst strAst)
            {
                string value = strAst.Value;
                if (value.StartsWith("--"))
                {
                    // We've gone past the last command.
                    lastCommandIndex = j - 1;
                    break;
                }

                current = ((Group)current).GetChildCommand(value);
                lastCommandIndex = j;

                if (current is Command)
                {
                    break;
                }
            }
        }

        List<CompletionResult> results = null;
        if (argCompletion)
        {
            if (i == lastCommandIndex + 1)
            {
                // We are about to complete sub-command names.
                if (current is not Group group)
                {
                    // Only group has sub commands. Nothing to complete when 'current' is a leaf command.
                    return results;
                }

                if (wordToComplete == string.Empty)
                {
                    results = new List<CompletionResult>(group.EntryInfos.Count);
                    foreach (var entry in group.EntryInfos)
                    {
                        results.Add(new CompletionResult(
                            completionText: entry.Name,
                            listItemText: entry.Name,
                            resultType: CompletionResultType.Command,
                            toolTip: entry.Attribute is null ? entry.Description : $"{entry.Attribute} {entry.Description}"));
                    }

                    return results;
                }

                WildcardPattern pattern = new(wordToComplete.EndsWith("*") ? wordToComplete : $"{wordToComplete}*");
                foreach (var entry in group.EntryInfos)
                {
                    if (pattern.IsMatch(entry.Name))
                    {
                        results ??= new List<CompletionResult>();
                        results.Add(new CompletionResult(
                            completionText: entry.Name,
                            listItemText: entry.Name,
                            resultType: CompletionResultType.Command,
                            toolTip: entry.Attribute is null ? entry.Description : $"{entry.Attribute} {entry.Description}"));
                    }
                }

                return results;
            }

            // We are about to complete parameter arguments.
            var paramElem = elements[i - 1];
            var paramName = paramElem is CommandParameterAst paramAst
                ? paramAst.Extent.Text
                : paramElem is StringConstantExpressionAst strAst
                    ? strAst.Value
                    : null;

            if (paramName is not null && current is Command currCommand)
            {
                var option = currCommand.FindOption(paramName);
                if (option.Arguments is not null)
                {
                    if (wordToComplete == string.Empty)
                    {
                        results = new List<CompletionResult>(option.Arguments.Count);
                        foreach (string v in option.Arguments)
                        {
                            results.Add(new CompletionResult(
                                completionText: v,
                                listItemText: v,
                                resultType: CompletionResultType.ParameterValue,
                                toolTip: v));
                        }

                        return results;
                    }

                    WildcardPattern pattern = new(wordToComplete.EndsWith("*") ? wordToComplete : $"{wordToComplete}*");
                    foreach (var v in option.Arguments)
                    {
                        if (pattern.IsMatch(v))
                        {
                            results ??= new List<CompletionResult>();
                            results.Add(new CompletionResult(
                                completionText: v,
                                listItemText: v,
                                resultType: CompletionResultType.ParameterValue,
                                toolTip: v));
                        }
                    }
                }
            }

            return results;
        }

        // We are about to complete parameters
        if (current is Group)
        {
            if ("-h".StartsWith(wordToComplete, StringComparison.OrdinalIgnoreCase))
            {
                results ??= new List<CompletionResult>();
                results.Add(new CompletionResult(
                    completionText: "-h",
                    listItemText: "-h",
                    resultType: CompletionResultType.ParameterName,
                    toolTip: "Show the help message."));
            }

            if ("--help".StartsWith(wordToComplete, StringComparison.OrdinalIgnoreCase))
            {
                results ??= new List<CompletionResult>();
                results.Add(new CompletionResult(
                    completionText: "--help",
                    listItemText: "--help",
                    resultType: CompletionResultType.ParameterName,
                    toolTip: "Show the help message."));
            }

            return results;
        }

        HashSet<string> specifiedParameters = new(StringComparer.OrdinalIgnoreCase);
        for (int k = lastCommandIndex + 1; k < elements.Count; k++)
        {
            if (k == i)
            {
                // Skip the element that we are doing completion for.
                continue;
            }

            var element = elements[k];
            if (element is CommandParameterAst paramAst)
            {
                specifiedParameters.Add(paramAst.Extent.Text);
            }
            else if (element is StringConstantExpressionAst strAst && strAst.Value.StartsWith("--"))
            {
                specifiedParameters.Add(strAst.Value);
            }
        }

        WildcardPattern paramPattern = new(wordToComplete.EndsWith("*") ? wordToComplete : $"{wordToComplete}*");
        foreach (var option in ((Command)current).Options)
        {
            if (specifiedParameters.Contains(option.Name))
            {
                continue;
            }

            if (option.Alias is not null)
            {
                foreach (string alias in option.Alias)
                {
                    if (specifiedParameters.Contains(alias))
                    {
                        continue;
                    }
                }
            }

            if (option.Short is not null)
            {
                foreach (string s in option.Short)
                {
                    if (specifiedParameters.Contains(s))
                    {
                        continue;
                    }
                }
            }

            if (paramPattern.IsMatch(option.Name))
            {
                results ??= new List<CompletionResult>();
                results.Add(new CompletionResult(
                    completionText: option.Name,
                    listItemText: option.Name,
                    resultType: CompletionResultType.ParameterName,
                    toolTip: option.Attribute is null ? option.Description : $"{option.Attribute} {option.Description}"));

                continue;
            }

            if (option.Alias is not null)
            {
                foreach (string alias in option.Alias)
                {
                    if (paramPattern.IsMatch(alias))
                    {
                        results ??= new List<CompletionResult>();
                        results.Add(new CompletionResult(
                            completionText: alias,
                            listItemText: alias,
                            resultType: CompletionResultType.ParameterName,
                            toolTip: option.Attribute is null ? option.Description : $"{option.Attribute} {option.Description}"));
                    }
                }

                continue;
            }

            if (option.Short is not null)
            {
                foreach (string s in option.Short)
                {
                    if (paramPattern.IsMatch(s))
                    {
                        results ??= new List<CompletionResult>();
                        results.Add(new CompletionResult(
                            completionText: s,
                            listItemText: s,
                            resultType: CompletionResultType.ParameterName,
                            toolTip: option.Attribute is null ? option.Description : $"{option.Attribute} {option.Description}"));
                    }
                }
            }
        }

        return results;
    }
}
