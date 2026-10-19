using System.CommandLine;
using System.Text;
var bundleCommand = new Command("bundle", "Bundle code files into a single file");
//Options
//1
var supportedLanguages = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
{
    ["c#"] = new[] { ".cs" },
    ["java"] = new[] { ".java" },
    ["python"] = new[] { ".py" },
    ["javascript"] = new[] { ".js" },
    ["typescript"] = new[] { ".ts" },
    ["html"] = new[] { ".html", ".htm" },
    ["css"] = new[] { ".css" },
    ["sql"] = new[] { ".sql" },
    ["c"] = new[] { ".c", ".h" },
    ["cpp"] = new[] { ".cpp", ".hpp" }
};

var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "bin", "obj", "debug", "release", ".git", ".vs", "node_modules"
};
//validation functions
// ===== Validation shared by bundle and create-rsp =====
// Each function returns an error message, or null if the input is valid

string? ValidateLanguages(string[] languages)
{
    if (languages.Length == 0)
        return "At least one language is required";

    var invalid = languages
        .Where(l => !l.Equals("all", StringComparison.OrdinalIgnoreCase)
                    && !supportedLanguages.ContainsKey(l))
        .ToList();
    if (invalid.Count > 0)
        return $"Unsupported language(s): {string.Join(", ", invalid)}\n" +
               $"Supported: all, {string.Join(", ", supportedLanguages.Keys)}";

    if (languages.Length > 1 && languages.Any(l => l.Equals("all", StringComparison.OrdinalIgnoreCase)))
        return "Cannot combine 'all' with other languages";

    return null;
}

string? ValidateSort(string sort)
{
    if (!sort.Equals("name", StringComparison.OrdinalIgnoreCase)
        && !sort.Equals("type", StringComparison.OrdinalIgnoreCase))
        return "--sort must be 'name' or 'type'";
    return null;
}

string? ValidateOutputPath(string path)
{
    try
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return $"The directory '{dir}' does not exist";
        return null;
    }
    catch (Exception)
    {
        return "Invalid path";
    }
}


// ===== Collecting code files =====
List<FileInfo> CollectFiles(string root, string[] languages, string outputPath)
{
    // If 'all' was selected, take all extensions. Otherwise only the extensions of the selected languages
    bool all = languages.Any(l => l.Equals("all", StringComparison.OrdinalIgnoreCase));
    var extensions = all
        ? supportedLanguages.Values.SelectMany(e => e)
        : languages.SelectMany(l => supportedLanguages[l]);
    var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

    var options = new EnumerationOptions
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true // Skips folders without access permission
    };

    return new DirectoryInfo(root)
        .EnumerateFiles("*", options)                       // Fix: EnumereateFiles -> EnumerateFiles
        .Where(f => extensionSet.Contains(f.Extension))
        .Where(f => !IsInExcludedFolder(f, root))
        .Where(f => !f.FullName.Equals(outputPath, StringComparison.OrdinalIgnoreCase))
        .ToList();
}

// Checks whether any folder on the path to the file is an excluded folder
bool IsInExcludedFolder(FileInfo file, string root)
{
    string relative = Path.GetRelativePath(root, file.FullName);
    var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // The last item is the file name itself, so we only check what comes before it
    return parts.Take(parts.Length - 1).Any(p => excludedFolders.Contains(p));
}

// ===== Step 1: comment style by file type (used by note) =====
string MakeComment(string extension, string text) => extension.ToLower() switch
{
    ".py" => $"# {text}",
    ".sql" => $"-- {text}",
    ".html" or ".htm" => $"<!-- {text} -->",
    ".css" => $"/* {text} */",
    _ => $"// {text}"
};
// Asks a yes/no question until a valid answer is given (Enter = no)
bool AskYesNo(string prompt)
{
    while (true)
    {
        Console.Write(prompt);
        string answer = (Console.ReadLine() ?? "").Trim().ToLower();
        if (answer is "y" or "yes") return true;
        if (answer is "n" or "no" or "") return false;
        Console.WriteLine("Error: please type y or n");
    }
}
// Asks until valid input is given. validate returns an error message or null.
string Ask(string prompt, Func<string, string?> validate, string defaultValue = "")
{
    while (true)
    {
        Console.Write(prompt);
        string input = (Console.ReadLine() ?? "").Trim().Trim('"');
        if (input == "") input = defaultValue;

        string? error = validate(input);
        if (error == null) return input;
        Console.WriteLine($"Error: {error}");
    }
}

string[] SplitLanguages(string text) =>
    text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

// Wraps a value in quotes if it contains whitespace (important for a path like "my folder\out.txt")
string Quote(string s) => s.Any(char.IsWhiteSpace) ? $"\"{s}\"" : s;


var languageOption = new Option<string[]>("--language", "-l")
{
    Description = "List of programming languages to include, or 'all'",
    Required = true,
    AllowMultipleArgumentsPerToken = true
};

//2
var outputOption = new Option<FileInfo>("--output", "-o")
{
    Description = "File name or full path of the bundle file",

    DefaultValueFactory = _ => new FileInfo("bundle.txt")
};
//3
var noteOption = new Option<bool>("--note", "-n")
{
    Description = "Write the source file name and relative path as a comment"
};
//4
var sortOption = new Option<string>("--sort", "-s")
{
    Description = "File sort order: 'name' (default) or 'type'",
    DefaultValueFactory = _ => "name"
};
//5
var removeEmptyLinesOption = new Option<bool>("--remove-empty-lines", "-r")
{
    Description = "Remove empty lines from the source code"
};
//6
var authorOption = new Option<string>("--author", "-a")
{
    Description = "Author name, written at the top of the bundle file"
};
//7
var dateOption = new Option<bool>("--date", "-d")
{
    Description = "Write the last modified date of each source file above it"
};
bundleCommand.Options.Add(languageOption);
bundleCommand.Options.Add(outputOption);
bundleCommand.Options.Add(noteOption);
bundleCommand.Options.Add(sortOption);
bundleCommand.Options.Add(removeEmptyLinesOption);
bundleCommand.Options.Add(authorOption);
bundleCommand.Options.Add(dateOption);

bundleCommand.SetAction(parseResult =>
{
    var languages = parseResult.GetValue(languageOption);
    var output = parseResult.GetValue(outputOption);
    var note = parseResult.GetValue(noteOption);
    var sort = parseResult.GetValue(sortOption);
    var date = parseResult.GetValue(dateOption);
    var removeEmptyLines = parseResult.GetValue(removeEmptyLinesOption);
    var author = parseResult.GetValue(authorOption);
    //validation
    string? error = ValidateLanguages(languages)
                ?? ValidateSort(sort)
                ?? ValidateOutputPath(output.FullName);
    if (error != null)
    {
        Console.WriteLine($"Error: {error}");
        return;
    }

    string fullPath = output.FullName;
    string root = Directory.GetCurrentDirectory();
    var files = CollectFiles(root, languages, fullPath);

    Console.WriteLine($"Found {files.Count} files:");
    foreach (var f in files)
    {
        Console.WriteLine("  " + Path.GetRelativePath(root, f.FullName));
    }

    // ===== Sorting =====
    // type = by extension (and by name within each extension), otherwise by file name
    var sortedFiles = sort.Equals("type", StringComparison.OrdinalIgnoreCase)
        ? files.OrderBy(f => f.Extension, StringComparer.OrdinalIgnoreCase)
               .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList()
        : files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

    if (sortedFiles.Count == 0)
    {
        Console.WriteLine("No code files found for the selected language(s)");
        return;
    }

    // ===== Writing the bundle =====
    try
    {
        using var writer = new StreamWriter(fullPath);

        // ----- Step 1: author -----
        if (!string.IsNullOrWhiteSpace(author))
        {
            writer.WriteLine($"// Author: {author}");
            writer.WriteLine();
        }

        foreach (var file in sortedFiles)
        {
           
            if (note)
            {
                string relative = Path.GetRelativePath(root, file.FullName);
                writer.WriteLine(MakeComment(file.Extension, $"Source: {relative}"));
            }
            if (date)
            {
                string modified = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                writer.WriteLine(MakeComment(file.Extension, $"Last modified: {modified}"));
            }

            IEnumerable<string> lines = File.ReadAllLines(file.FullName);


            if (removeEmptyLines)
                lines = lines.Where(l => !string.IsNullOrWhiteSpace(l));

            foreach (var line in lines)
            {
                writer.WriteLine(line);
            }
            writer.WriteLine(); 
        }

        Console.WriteLine($"Bundle created: {fullPath} ({sortedFiles.Count} files)");
    }
    catch (UnauthorizedAccessException)
    {
        Console.WriteLine("Error: no permission to save to this path");
    }
    catch (IOException ex)
    {
        Console.WriteLine($"Error: could not save the file ({ex.Message})");
    }
});
var createRspCommand = new Command("create-rsp", "Create a response file with a ready-made bundle command");

createRspCommand.SetAction(parseResult =>
{
    Console.WriteLine("Please enter a value for each option (Enter on an optional one = default)\n");

    string[] languages = SplitLanguages(
        Ask($"Languages (space-separated), or all. Supported: {string.Join(", ", supportedLanguages.Keys)}: ",
            s => ValidateLanguages(SplitLanguages(s))));

    string outputPath = Ask("Output file name or full path (default: bundle.txt): ",
                            ValidateOutputPath, "bundle.txt");

    bool note = AskYesNo("Write the source as a comment? (y/n): ");
    bool date = AskYesNo("Write the last modified date above each file? (y/n): ");

    string sort = Ask("Sort: name or type (default: name): ",
                      ValidateSort, "name").ToLower();

    bool removeEmpty = AskYesNo("Remove empty lines? (y/n): ");

    string author = Ask("Author name (can be skipped): ", _ => null);

    // ----- Building the full command -----
    var sb = new StringBuilder("bundle");
    sb.Append(" --language ").Append(string.Join(" ", languages));
    sb.Append(" --output ").Append(Quote(outputPath));
    if (date) sb.Append(" --date");
    if (note) sb.Append(" --note");
    sb.Append(" --sort ").Append(sort);
    if (removeEmpty) sb.Append(" --remove-empty-lines");
    if (author != "") sb.Append(" --author ").Append(Quote(author));

    // ----- Saving to a response file -----
    string rspName = Ask("Response file name (default: bundle.rsp): ", _ => null, "bundle.rsp");
    if (!rspName.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase)) rspName += ".rsp";

    try
    {
        File.WriteAllText(rspName, sb.ToString());
        Console.WriteLine($"\nResponse file created: {Path.GetFullPath(rspName)}");
        Console.WriteLine($"Saved command: {sb}");
        Console.WriteLine($"To run: fib2 @{rspName}");
    }
    catch (UnauthorizedAccessException)
    {
        Console.WriteLine("Error: no permission to save to this path");
    }
    catch (IOException ex)
    {
        Console.WriteLine($"Error: could not save the file ({ex.Message})");
    }
});

var rootCommand = new RootCommand("Root command for the File Bundler command-line interface (CLI)");

rootCommand.Subcommands.Add(bundleCommand);
rootCommand.Subcommands.Add(createRspCommand);
rootCommand.Parse(args).Invoke();
