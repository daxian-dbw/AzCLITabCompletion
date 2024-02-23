
Register-ArgumentCompleter -Native -CommandName az -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    $azCompletion = [AzCLI.Completion.AzCompletion]::GetSingleton("E:\yard\tmp\az-cli-out\az")
    $azCompletion.GetCompletions($wordToComplete, $commandAst, $cursorPosition);
}
