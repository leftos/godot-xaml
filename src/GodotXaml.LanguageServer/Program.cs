using GodotXaml.LanguageServer;

// The protocol owns stdout; logs go to stderr and to the client's log.
return await LanguageServerHost.Run(Console.OpenStandardInput(), Console.OpenStandardOutput(), new TaskDelayScheduler(), Console.Error);
