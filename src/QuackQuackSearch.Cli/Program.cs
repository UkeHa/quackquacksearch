using System.Diagnostics;
using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.DBus;
using QuackQuackSearch.Core.Index;
using QuackQuackSearch.Core.Search;
using QuackQuackSearch.Core.Storage;
using QuackQuackSearch.Core.System;
using Spectre.Console;
using Tmds.DBus;

namespace QuackQuackSearch.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("QuackQuackSearch")
                .Color(Color.Yellow));

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        string firstArg = args[0].ToLowerInvariant();

        bool isSubcommand = firstArg switch
        {
            "status" or "add" or "remove" or "exclude" or "rescan" or "benchmark" or
            "mounts" or "config" or "service" or "hotkey" or "gui" => true,
            _ => false
        };

        try
        {
            if (!isSubcommand)
            {
                string[] searchArgs = firstArg == "search" ? args[1..] : args;
                return await HandleSearchAsync(searchArgs);
            }

            return firstArg switch
            {
                "status" => await HandleStatusAsync(),
                "add" => await HandleAddAsync(args[1..]),
                "remove" => await HandleRemoveAsync(args[1..]),
                "exclude" => await HandleExcludeAsync(args[1..]),
                "rescan" => await HandleRescanAsync(args[1..]),
                "benchmark" => await HandleBenchmarkAsync(args[1..]),
                "mounts" => HandleMounts(),
                "config" => HandleConfig(args[1..]),
                "service" => HandleService(args[1..]),
                "hotkey" => HandleHotkey(args[1..]),
                "gui" => HandleGui(args[1..]),
                _ => HandleUnknownCommand(firstArg)
            };
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        AnsiConsole.MarkupLine("[bold yellow]QuackQuackSearch (qqs)[/] - Lightning fast Linux file search");
        AnsiConsole.MarkupLine("[grey]Usage: qqs <query> [[--exact]][/]");
        AnsiConsole.MarkupLine("[grey]       qqs <command> [[arguments]][/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Search (Default Action):[/]");
        AnsiConsole.MarkupLine("  [green]qqs <query>[/]                                     Searches files instantly with fuzzy matching");
        AnsiConsole.MarkupLine("  [green]qqs <query> --exact[/]                             Searches strictly by exact substring");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Daemon & Paths:[/]");
        AnsiConsole.MarkupLine("  [green]qqs status[/]                                      Displays running daemon status and monitored paths");
        AnsiConsole.MarkupLine("  [green]qqs add <path> [[--network]] [[-x <exclude>]][/]       Adds path to live monitoring (with optional exclusions)");
        AnsiConsole.MarkupLine("  [green]qqs remove <path>[/]                                Removes path from live monitoring");
        AnsiConsole.MarkupLine("  [green]qqs exclude add <path> <pattern>[/]                 Exclude subfolder or pattern from a monitored path");
        AnsiConsole.MarkupLine("  [green]qqs exclude remove <path> <pat>[/]                  Remove exclusion pattern from path");
        AnsiConsole.MarkupLine("  [green]qqs exclude list [[[grey]path[/]]][/]                             List exclusions for configured path(s)");
        AnsiConsole.MarkupLine("  [green]qqs rescan [[path]][/]                                Forces background re-indexing of a path");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]System & Tools:[/]");
        AnsiConsole.MarkupLine("  [green]qqs benchmark [[directory]][/]                    Runs crawler and SIMD search benchmark");
        AnsiConsole.MarkupLine("  [green]qqs mounts[/]                                    Lists detected local and network mounts");
        AnsiConsole.MarkupLine("  [green]qqs config [[show|init]][/]                        Inspects or initializes configuration");
        AnsiConsole.MarkupLine("  [green]qqs service [[install|start|status]][/]         Manages systemd --user service unit");
        AnsiConsole.MarkupLine("  [green]qqs hotkey [[register|status]][/]                 Manages global GUI desktop hotkey (Meta+Shift+F)");
        AnsiConsole.MarkupLine("  [green]qqs gui[/]                                       Launches desktop graphical interface");
        AnsiConsole.WriteLine();
    }

    private static int HandleUnknownCommand(string command)
    {
        AnsiConsole.MarkupLine($"[bold red]Unknown command:[/] {Markup.Escape(command)}");
        PrintHelp();
        return 1;
    }

    private static async Task<IDaemonService?> TryGetDaemonServiceAsync()
    {
        try
        {
            var conn = Connection.Session;
            await conn.ConnectAsync();
            bool hasOwner = await conn.IsServiceActiveAsync("org.quackquacksearch.Daemon");
            if (!hasOwner) return null;

            return conn.CreateProxy<IDaemonService>("org.quackquacksearch.Daemon", "/org/quackquacksearch/Daemon");
        }
        catch
        {
            return null;
        }
    }

    private static async Task<int> HandleSearchAsync(string[] args)
    {
        bool fuzzy = true;
        var queryParts = new List<string>();

        foreach (var arg in args)
        {
            if (arg is "--exact" or "-e")
            {
                fuzzy = false;
            }
            else if (arg is "--fuzzy" or "-f")
            {
                fuzzy = true;
            }
            else
            {
                queryParts.Add(arg);
            }
        }

        if (queryParts.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Please provide a search query.[/]");
            return 1;
        }

        string query = queryParts[0];
        var daemon = await TryGetDaemonServiceAsync();

        if (daemon != null)
        {
            var sw = Stopwatch.StartNew();
            var results = await daemon.SearchWithOptionsAsync(query, 30, fuzzy);
            sw.Stop();

            string mode = fuzzy ? " (Fuzzy)" : " (Exact)";
            AnsiConsole.MarkupLine($"[green]Daemon found {results.Length} results in {sw.Elapsed.TotalMilliseconds:F2} ms{mode}:[/]");
            DisplayResultsTable(results.Select(r => new SearchResult(r.FullPath, r.FileName, r.Size, DateTimeOffset.FromUnixTimeSeconds(r.ModifiedTime), r.IsDirectory, r.Score)));
            return 0;
        }

        // Fallback: standalone crawl if daemon is not running
        string searchDir = queryParts.Count > 1 ? queryParts[1] : Directory.GetCurrentDirectory();
        AnsiConsole.MarkupLine("[grey](Daemon is not running. Performing direct local scan...)[/]");

        var engine = new SearchIndexEngine();
        var crawler = new FastFileSystemCrawler();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync($"Indexing {searchDir}...", async _ =>
            {
                await crawler.CrawlAsync(searchDir, items =>
                {
                    engine.BulkAdd(items);
                    return Task.CompletedTask;
                });
            });

        var localSw = Stopwatch.StartNew();
        var localResults = engine.Search(query, new SearchOptions { MaxResults = 30, Fuzzy = fuzzy });
        localSw.Stop();

        string localMode = fuzzy ? " (Fuzzy)" : " (Exact)";
        AnsiConsole.MarkupLine($"[green]Found {localResults.Count} results in {localSw.Elapsed.TotalMilliseconds:F2} ms{localMode} (searched {engine.TotalFiles:N0} files):[/]");
        DisplayResultsTable(localResults);
        return 0;
    }

    private static void DisplayResultsTable(IEnumerable<SearchResult> results)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Score");
        table.AddColumn("Name");
        table.AddColumn("Path");
        table.AddColumn("Size");
        table.AddColumn("Modified");

        foreach (var r in results)
        {
            string sizeStr = r.IsDirectory ? "<DIR>" : FormatBytes(r.Size);
            table.AddRow(
                $"[yellow]{r.Score:F0}[/]",
                r.IsDirectory ? $"[bold blue]{Markup.Escape(r.FileName)}[/]" : Markup.Escape(r.FileName),
                $"[grey]{Markup.Escape(Path.GetDirectoryName(r.FullPath) ?? "/")}[/]",
                sizeStr,
                r.ModifiedTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            );
        }

        AnsiConsole.Write(table);
    }

    private static async Task<int> HandleStatusAsync()
    {
        var daemon = await TryGetDaemonServiceAsync();
        if (daemon == null)
        {
            AnsiConsole.MarkupLine("[red]QuackQuackSearch Daemon is currently NOT running on D-Bus.[/]");
            AnsiConsole.MarkupLine("[grey]You can start it with:[/] [yellow]dotnet run --project src/QuackQuackSearch.Daemon[/]");
            return 1;
        }

        var status = await daemon.GetStatusAsync();
        var paths = await daemon.ListPathsAsync();

        var table = new Table().Border(TableBorder.Simple);
        table.AddColumn("Daemon Metric");
        table.AddColumn("Value");

        table.AddRow("Status", "[bold green]ONLINE (D-Bus)[/]");
        table.AddRow("Uptime", $"{TimeSpan.FromSeconds(status.UptimeSeconds):hh\\:mm\\:ss}");
        table.AddRow("Indexed Files", $"{status.TotalFiles:N0}");
        table.AddRow("Indexed Directories", $"{status.TotalDirectories:N0}");
        table.AddRow("Active inotify Watches", $"{status.ActiveWatches:N0}");
        table.AddRow("RAM Usage", $"{FormatBytes(status.MemoryBytes)}");

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan]Monitored Paths:[/]");
        var pathTable = new Table().Border(TableBorder.Rounded);
        pathTable.AddColumn("Path");
        pathTable.AddColumn("Type");
        pathTable.AddColumn("Excludes");
        pathTable.AddColumn("Status");

        foreach (var p in paths)
        {
            string statusColor = p.Status == "Active" ? "green" : (p.Status == "Scanning" ? "yellow" : "red");
            string excludes = string.IsNullOrWhiteSpace(p.ExcludesCsv) ? "[grey]-[/]" : Markup.Escape(p.ExcludesCsv);
            pathTable.AddRow(
                Markup.Escape(p.Path),
                p.Type,
                excludes,
                $"[{statusColor}]{p.Status}[/]"
            );
        }

        AnsiConsole.Write(pathTable);
        return 0;
    }

    private static async Task<int> HandleAddAsync(string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Usage: qqs add <path> [--network] [-x|--exclude <exclude_pattern>][/]");
            return 1;
        }

        string path = string.Empty;
        bool isNetwork = false;
        var excludes = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--network", StringComparison.OrdinalIgnoreCase))
            {
                isNetwork = true;
            }
            else if ((args[i].Equals("--exclude", StringComparison.OrdinalIgnoreCase) || args[i].Equals("-x", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                excludes.Add(args[++i]);
            }
            else if (string.IsNullOrEmpty(path))
            {
                path = args[i];
            }
        }

        if (string.IsNullOrEmpty(path))
        {
            AnsiConsole.MarkupLine("[red]Please specify a path to add.[/]");
            return 1;
        }

        string type = isNetwork ? "network" : "local";
        var daemon = await TryGetDaemonServiceAsync();

        if (daemon != null)
        {
            bool ok = await daemon.AddPathAsync(path, type);
            if (ok)
            {
                AnsiConsole.MarkupLine($"[green]Successfully added '{path}' ({type}) to running daemon![/]");
                foreach (var exc in excludes)
                {
                    await daemon.AddExcludeAsync(path, exc);
                    AnsiConsole.MarkupLine($"  [grey]+ Excluded:[/] [yellow]{Markup.Escape(exc)}[/]");
                }
                return 0;
            }
            AnsiConsole.MarkupLine($"[red]Failed to add '{path}'. Path does not exist or invalid.[/]");
            return 1;
        }

        // Add directly to config
        var config = ConfigManager.LoadOrCreateDefault();
        string fullPath = Path.GetFullPath(path);
        var existing = config.Paths.FirstOrDefault(p => p.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            AnsiConsole.MarkupLine($"[yellow]Path is already configured:[/] {fullPath}");
            foreach (var exc in excludes)
            {
                existing.CustomExcludes ??= [];
                if (!existing.CustomExcludes.Contains(exc, StringComparer.OrdinalIgnoreCase))
                {
                    existing.CustomExcludes.Add(exc);
                    AnsiConsole.MarkupLine($"  [grey]+ Excluded:[/] [yellow]{Markup.Escape(exc)}[/]");
                }
            }
            ConfigManager.Save(config);
            return 0;
        }

        config.Paths.Add(new PathConfigEntry
        {
            Path = fullPath,
            Type = type,
            Enabled = true,
            CustomExcludes = excludes
        });
        ConfigManager.Save(config);
        AnsiConsole.MarkupLine($"[green]Added '{fullPath}' to config.json. Start daemon to monitor.[/]");
        return 0;
    }

    private static async Task<int> HandleExcludeAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            AnsiConsole.MarkupLine("[bold]Usage:[/] qqs exclude <add|remove|list> <path> [pattern...]");
            AnsiConsole.MarkupLine("  [green]qqs exclude add <path> <pattern>[/]     Exclude a subfolder or pattern from a monitored path");
            AnsiConsole.MarkupLine("  [green]qqs exclude remove <path> <pattern>[/]  Remove an exclusion pattern from a monitored path");
            AnsiConsole.MarkupLine("  [green]qqs exclude list [[[grey]path[/]]][/]            List all exclusions or exclusions for a specific path");
            return 0;
        }

        string action = args[0].ToLowerInvariant();
        var daemon = await TryGetDaemonServiceAsync();

        if (action == "list")
        {
            string? targetPath = args.Length > 1 ? args[1] : null;
            if (daemon != null)
            {
                var paths = await daemon.ListPathsAsync();
                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("Path");
                table.AddColumn("Exclusions");

                foreach (var p in paths)
                {
                    if (targetPath != null && !p.Path.TrimEnd('/').Equals(Path.GetFullPath(targetPath).TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                        continue;

                    string exc = string.IsNullOrWhiteSpace(p.ExcludesCsv) ? "[grey](none)[/]" : Markup.Escape(p.ExcludesCsv.Replace(";", "\n"));
                    table.AddRow(Markup.Escape(p.Path), exc);
                }
                AnsiConsole.Write(table);
                return 0;
            }
            else
            {
                var config = ConfigManager.LoadOrCreateDefault();
                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("Path");
                table.AddColumn("Exclusions");

                foreach (var p in config.Paths)
                {
                    if (targetPath != null && !p.Path.TrimEnd('/').Equals(Path.GetFullPath(targetPath).TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                        continue;

                    string exc = (p.CustomExcludes == null || p.CustomExcludes.Count == 0) ? "[grey](none)[/]" : Markup.Escape(string.Join("\n", p.CustomExcludes));
                    table.AddRow(Markup.Escape(p.Path), exc);
                }
                AnsiConsole.Write(table);
                return 0;
            }
        }

        if (action == "add")
        {
            if (args.Length < 3)
            {
                AnsiConsole.MarkupLine("[red]Usage: qqs exclude add <monitored-path> <pattern_or_subfolder>[/]");
                return 1;
            }

            string rootPath = args[1];
            string pattern = string.Join(" ", args[2..]);

            if (daemon != null)
            {
                bool ok = await daemon.AddExcludeAsync(rootPath, pattern);
                if (ok)
                {
                    AnsiConsole.MarkupLine($"[green]Successfully added exclusion '{pattern}' to '{rootPath}' (daemon updated & purged).[/]");
                    return 0;
                }
                AnsiConsole.MarkupLine($"[red]Failed to add exclusion. Ensure '{rootPath}' is a monitored path.[/]");
                return 1;
            }
            else
            {
                var config = ConfigManager.LoadOrCreateDefault();
                string norm = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
                var entry = config.Paths.FirstOrDefault(p => Path.GetFullPath(p.Path).TrimEnd(Path.DirectorySeparatorChar).Equals(norm, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    AnsiConsole.MarkupLine($"[red]Path '{rootPath}' is not configured in config.json.[/]");
                    return 1;
                }

                entry.CustomExcludes ??= [];
                if (!entry.CustomExcludes.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                {
                    entry.CustomExcludes.Add(pattern);
                    ConfigManager.Save(config);
                    AnsiConsole.MarkupLine($"[green]Added exclusion '{pattern}' to '{entry.Path}' in config.json.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Exclusion '{pattern}' is already present on '{entry.Path}'.[/]");
                }
                return 0;
            }
        }

        if (action == "remove")
        {
            if (args.Length < 3)
            {
                AnsiConsole.MarkupLine("[red]Usage: qqs exclude remove <monitored-path> <pattern_or_subfolder>[/]");
                return 1;
            }

            string rootPath = args[1];
            string pattern = string.Join(" ", args[2..]);

            if (daemon != null)
            {
                bool ok = await daemon.RemoveExcludeAsync(rootPath, pattern);
                if (ok)
                {
                    AnsiConsole.MarkupLine($"[green]Successfully removed exclusion '{pattern}' from '{rootPath}'.[/]");
                    return 0;
                }
                AnsiConsole.MarkupLine($"[red]Failed to remove exclusion. Pattern or path not found.[/]");
                return 1;
            }
            else
            {
                var config = ConfigManager.LoadOrCreateDefault();
                string norm = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
                var entry = config.Paths.FirstOrDefault(p => Path.GetFullPath(p.Path).TrimEnd(Path.DirectorySeparatorChar).Equals(norm, StringComparison.OrdinalIgnoreCase));
                if (entry == null || entry.CustomExcludes == null)
                {
                    AnsiConsole.MarkupLine($"[red]Path '{rootPath}' not found in config.json.[/]");
                    return 1;
                }

                int removed = entry.CustomExcludes.RemoveAll(x => x.Equals(pattern, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    ConfigManager.Save(config);
                    AnsiConsole.MarkupLine($"[green]Removed exclusion '{pattern}' from '{entry.Path}' in config.json.[/]");
                    return 0;
                }
                AnsiConsole.MarkupLine($"[yellow]Exclusion '{pattern}' was not found in '{entry.Path}'.[/]");
                return 1;
            }
        }

        // If the first argument was actually a path, treat as "qqs exclude list <path>"
        return await HandleExcludeAsync(["list", args[0]]);
    }

    private static async Task<int> HandleRemoveAsync(string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Usage: qqs remove <path>[/]");
            return 1;
        }

        string path = args[0];
        var daemon = await TryGetDaemonServiceAsync();
        if (daemon != null)
        {
            bool ok = await daemon.RemovePathAsync(path);
            if (ok)
            {
                AnsiConsole.MarkupLine($"[green]Successfully removed '{path}' from running daemon![/]");
                return 0;
            }
            AnsiConsole.MarkupLine($"[yellow]Path was not found in daemon config:[/] {path}");
            return 1;
        }

        var config = ConfigManager.LoadOrCreateDefault();
        string fullPath = Path.GetFullPath(path);
        int removed = config.Paths.RemoveAll(p => p.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            ConfigManager.Save(config);
            AnsiConsole.MarkupLine($"[green]Removed '{fullPath}' from config.json.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[yellow]Path not found in config.json:[/] {path}");
        return 1;
    }

    private static async Task<int> HandleRescanAsync(string[] args)
    {
        string path = args.Length > 0 ? args[0] : string.Empty;
        var daemon = await TryGetDaemonServiceAsync();
        if (daemon == null)
        {
            AnsiConsole.MarkupLine("[red]QuackQuackSearch Daemon is not running.[/]");
            return 1;
        }

        await daemon.TriggerRescanAsync(path);
        AnsiConsole.MarkupLine($"[green]Triggered rescan for: {(string.IsNullOrEmpty(path) ? "ALL configured paths" : path)}[/]");
        return 0;
    }

    private static async Task<int> HandleBenchmarkAsync(string[] args)
    {
        string targetDir = args.Length > 0 ? args[0] : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(targetDir))
        {
            AnsiConsole.MarkupLine($"[red]Directory does not exist:[/] {targetDir}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[bold blue]=== Starting QuackQuackSearch Benchmark on: {targetDir} ===[/]");

        var engine = new SearchIndexEngine();
        var crawler = new FastFileSystemCrawler();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memBefore = GC.GetTotalMemory(true);

        var crawlSw = Stopwatch.StartNew();
        long filesCount = 0;
        long dirsCount = 0;

        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Crawling filesystem tree...[/]");

                var progress = new Progress<CrawlProgress>(p =>
                {
                    filesCount = p.FilesFound;
                    dirsCount = p.DirectoriesFound;
                    task.Description = $"[green]Crawled:[/] {filesCount:N0} files, {dirsCount:N0} dirs";
                });

                await crawler.CrawlAsync(targetDir, items =>
                {
                    engine.BulkAdd(items);
                    return Task.CompletedTask;
                }, progress);

                task.Value = 100;
            });

        crawlSw.Stop();
        long memAfter = GC.GetTotalMemory(false);
        long memUsedBytes = Math.Max(0, memAfter - memBefore);

        var statsTable = new Table().Border(TableBorder.Simple);
        statsTable.AddColumn("Metric");
        statsTable.AddColumn("Value");

        statsTable.AddRow("Indexed Files", $"{engine.TotalFiles:N0}");
        statsTable.AddRow("Indexed Directories", $"{engine.TotalDirectories:N0}");
        statsTable.AddRow("Crawl Duration", $"{crawlSw.Elapsed.TotalSeconds:F2} seconds");
        statsTable.AddRow("Throughput", $"{engine.TotalFiles / Math.Max(0.01, crawlSw.Elapsed.TotalSeconds):N0} files/sec");
        statsTable.AddRow("Estimated Heap Used", $"{FormatBytes(memUsedBytes)} (~{(double)memUsedBytes / Math.Max(1, engine.TotalFiles):F1} B/file)");

        AnsiConsole.Write(statsTable);

        // Benchmark SIMD Searches
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold cyan]=== SIMD Substring Search Benchmarks ===[/]");

        string[] testQueries = ["test", "config", ".json", "main", "quack", "log", "a", "*.cs"];

        var searchTable = new Table().Border(TableBorder.Rounded);
        searchTable.AddColumn("Query");
        searchTable.AddColumn("Matches Found");
        searchTable.AddColumn("Latency (Avg 5 runs)");
        searchTable.AddColumn("Status");

        foreach (string query in testQueries)
        {
            engine.Search(query, new SearchOptions { MaxResults = 50 });

            var times = new List<double>(5);
            int matchCount = 0;

            for (int i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                var results = engine.Search(query, new SearchOptions { MaxResults = 50 });
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
                matchCount = results.Count;
            }

            double avgMs = times.Average();
            string status = avgMs < 20.0 ? "[green]EXCELLENT (<20ms)[/]" : (avgMs < 50.0 ? "[yellow]GOOD (<50ms)[/]" : "[red]SLOW[/]");

            searchTable.AddRow(
                $"[bold]{query}[/]",
                $"{matchCount}",
                $"{avgMs:F2} ms ({avgMs * 1000:F0} µs)",
                status
            );
        }

        AnsiConsole.Write(searchTable);

        // Test MessagePack Serialization & Deserialization
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold magenta]=== Persistence Benchmark (MessagePack) ===[/]");

        string tempCacheFile = Path.Combine(Path.GetTempPath(), "quackquacksearch_benchmark.cache");
        try
        {
            var saveSw = Stopwatch.StartNew();
            await IndexSerializer.SaveAsync(engine, tempCacheFile);
            saveSw.Stop();

            long fileSize = new FileInfo(tempCacheFile).Length;

            var restoreEngine = new SearchIndexEngine();
            var loadSw = Stopwatch.StartNew();
            bool loaded = await IndexSerializer.TryLoadAsync(restoreEngine, tempCacheFile);
            loadSw.Stop();

            var persistTable = new Table().Border(TableBorder.Simple);
            persistTable.AddColumn("Persistence Metric");
            persistTable.AddColumn("Value");

            persistTable.AddRow("Serialized File Size", $"{FormatBytes(fileSize)}");
            persistTable.AddRow("Serialization Time", $"{saveSw.Elapsed.TotalMilliseconds:F1} ms");
            persistTable.AddRow("Deserialization Time", $"{loadSw.Elapsed.TotalMilliseconds:F1} ms");
            persistTable.AddRow("Integrity Check", loaded && restoreEngine.TotalFiles == engine.TotalFiles ? "[green]PASS[/]" : "[red]FAIL[/]");

            AnsiConsole.Write(persistTable);
        }
        finally
        {
            if (File.Exists(tempCacheFile)) File.Delete(tempCacheFile);
        }

        return 0;
    }

    private static int HandleMounts()
    {
        AnsiConsole.MarkupLine("[bold green]=== Detected Filesystem Mounts ===[/]");
        var candidates = MountScanner.ScanMounts();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Device");
        table.AddColumn("Mount Point");
        table.AddColumn("Type");
        table.AddColumn("FS Type");
        table.AddColumn("Recommended Mode");
        table.AddColumn("User Accessible");

        foreach (var m in candidates)
        {
            string typeColor = m.Type switch
            {
                MountType.Local => "green",
                MountType.Network => "yellow",
                MountType.GVFS => "magenta",
                _ => "grey"
            };

            table.AddRow(
                Markup.Escape(m.Device),
                Markup.Escape(m.MountPoint),
                $"[{typeColor}]{m.Type}[/]",
                m.FsType,
                m.SuggestedMonitoringMode == "local" ? "[green]inotify[/]" : "[yellow]polling[/]",
                m.IsRemovableOrUserAccessible ? "[cyan]Yes[/]" : "[grey]No[/]"
            );
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static int HandleConfig(string[] args)
    {
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "show";

        if (sub == "init")
        {
            var config = ConfigManager.CreateDefault();
            ConfigManager.Save(config);
            AnsiConsole.MarkupLine($"[green]Default configuration written to:[/] {ConfigManager.GetConfigFilePath()}");
            return 0;
        }

        var loaded = ConfigManager.LoadOrCreateDefault();
        AnsiConsole.MarkupLine($"[bold]Config File:[/] [cyan]{ConfigManager.GetConfigFilePath()}[/]");
        AnsiConsole.MarkupLine($"[bold]Monitored Paths:[/] {loaded.Paths.Count}");

        foreach (var p in loaded.Paths)
        {
            AnsiConsole.MarkupLine($"  - [yellow]{p.Path}[/] (Type: [green]{p.Type}[/], Enabled: {p.Enabled})");
        }

        return 0;
    }

    private static int HandleService(string[] args)
    {
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        string systemdUserDir = Core.System.XdgDirectories.SystemdUserDir;
        string serviceFile = Path.Combine(systemdUserDir, "quackquacksearch.service");

        if (sub == "install")
        {
            Directory.CreateDirectory(systemdUserDir);
            string daemonBin = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "QuackQuackSearch.Daemon", "bin", "Release", "net10.0", "QuackQuackSearch.Daemon"));

            if (!File.Exists(daemonBin))
            {
                daemonBin = "dotnet run --project " + Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src", "QuackQuackSearch.Daemon"));
            }

            string unit = $"""
[Unit]
Description=QuackQuackSearch Daemon (Lightning fast file search)
After=default.target

[Service]
Type=simple
ExecStart={daemonBin}
Restart=on-failure
RestartSec=5s

[Install]
WantedBy=default.target
""";
            File.WriteAllText(serviceFile, unit);
            AnsiConsole.MarkupLine($"[green]Service unit written to:[/] {serviceFile}");
            AnsiConsole.MarkupLine("[cyan]To enable and start:[/]");
            AnsiConsole.MarkupLine("  systemctl --user daemon-reload");
            AnsiConsole.MarkupLine("  systemctl --user enable --now quackquacksearch");
            return 0;
        }

        if (File.Exists(serviceFile))
        {
            AnsiConsole.MarkupLine($"[green]Service unit is installed at:[/] {serviceFile}");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Service unit is not yet installed.[/] Run [green]qqs service install[/]");
        }

        return 0;
    }

    private static int HandleGui(string[] args)
    {
        string baseDir = AppContext.BaseDirectory;
        string sameDirGui = Path.Combine(baseDir, "QuackQuackSearch.Gui");
        if (File.Exists(sameDirGui))
        {
            Process.Start(new ProcessStartInfo(sameDirGui) { UseShellExecute = false });
            AnsiConsole.MarkupLine("[green]Launched QuackQuackSearch GUI.[/]");
            return 0;
        }

        string relativeGui = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "QuackQuackSearch.Gui", "bin", "Release", "net10.0", "QuackQuackSearch.Gui"));
        if (File.Exists(relativeGui))
        {
            Process.Start(new ProcessStartInfo(relativeGui) { UseShellExecute = false });
            AnsiConsole.MarkupLine("[green]Launched QuackQuackSearch GUI.[/]");
            return 0;
        }

        var whichCheck = Process.Start(new ProcessStartInfo("which", "quackquacksearch-gui") { RedirectStandardOutput = true });
        whichCheck?.WaitForExit();
        if (whichCheck?.ExitCode == 0)
        {
            Process.Start(new ProcessStartInfo("quackquacksearch-gui") { UseShellExecute = false });
            AnsiConsole.MarkupLine("[green]Launched QuackQuackSearch GUI.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine("[yellow]QuackQuackSearch GUI binary not found. Please install via install.sh or build the project.[/]");
        return 1;
    }

    private static int HandleHotkey(string[] args)
    {
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        string shortcut = "Meta+Shift+F";

        if (sub is "register" or "install")
        {
            AnsiConsole.MarkupLine($"[cyan]Registering global shortcut ({shortcut}) for QuackQuackSearch GUI...[/]");
            bool registered = false;

            if (File.Exists("/usr/bin/kwriteconfig6"))
            {
                Process.Start("kwriteconfig6", "--file kglobalshortcutsrc --group quackquacksearch-gui.desktop --key _k_friendly_name QuackQuackSearch")?.WaitForExit();
                Process.Start("kwriteconfig6", $"--file kglobalshortcutsrc --group quackquacksearch-gui.desktop --key _launch \"{shortcut},none,Launch QuackQuackSearch\"")?.WaitForExit();
                Process.Start("kquitapp6", "kglobalaccel 2>/dev/null || true")?.WaitForExit();
                registered = true;
                AnsiConsole.MarkupLine($"[green]Successfully registered KDE Plasma global shortcut:[/] [bold cyan]{shortcut}[/]");
            }
            else if (File.Exists("/usr/bin/kwriteconfig5"))
            {
                Process.Start("kwriteconfig5", "--file kglobalshortcutsrc --group quackquacksearch-gui.desktop --key _k_friendly_name QuackQuackSearch")?.WaitForExit();
                Process.Start("kwriteconfig5", $"--file kglobalshortcutsrc --group quackquacksearch-gui.desktop --key _launch \"{shortcut},none,Launch QuackQuackSearch\"")?.WaitForExit();
                registered = true;
                AnsiConsole.MarkupLine($"[green]Successfully registered KDE Plasma global shortcut:[/] [bold cyan]{shortcut}[/]");
            }

            if (!registered)
            {
                AnsiConsole.MarkupLine("[yellow]Could not automatically detect KDE Plasma config tool.[/]");
                AnsiConsole.MarkupLine("To configure manually in your desktop settings:");
                AnsiConsole.MarkupLine("  • [bold]Command:[/] [green]quackquacksearch-gui[/] or [green]qqs gui[/]");
                AnsiConsole.MarkupLine($"  • [bold]Recommended Shortcut:[/] [cyan]{shortcut}[/]");
            }

            return 0;
        }

        AnsiConsole.MarkupLine("[bold]Global Hotkey Configuration:[/]");
        AnsiConsole.MarkupLine($"  • Default Shortcut: [bold cyan]{shortcut}[/]");
        AnsiConsole.MarkupLine("  • Target Command:   [green]quackquacksearch-gui[/]");
        AnsiConsole.MarkupLine("  • Behavior:         Toggles & brings GUI to front (Single-Instance)");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("To register for KDE Plasma automatically, run:");
        AnsiConsole.MarkupLine("  [green]qqs hotkey register[/]");
        return 0;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        double d = bytes;
        while (d >= 1024 && i < suffixes.Length - 1)
        {
            d /= 1024;
            i++;
        }
        return $"{d:0.##} {suffixes[i]}";
    }
}
