using System.Text.Json;
using System.Text.RegularExpressions;
using NJsonSchema;

var rootDirectory = new DirectoryInfo(AppContext.BaseDirectory);
while (rootDirectory is not null
    && !Directory.Exists(Path.Combine(rootDirectory.FullName, "data", "cards")))
{
    rootDirectory = rootDirectory.Parent;
}

var root = rootDirectory?.FullName
    ?? throw new InvalidOperationException("Repository root could not be located.");
var cardsRoot = Path.Combine(root, "data", "cards");
var schemaPath = Path.Combine(root, "data", "schema", "cards.schema.json");

if (!File.Exists(schemaPath) || !Directory.Exists(cardsRoot))
{
    Console.Error.WriteLine("ERROR: expected data/cards and data/schema/cards.schema.json.");
    return 2;
}

// NJsonSchema 11 currently resolves the draft-07 "definitions" spelling;
// the repository contract uses the equivalent draft-2020 "$defs" spelling.
// Normalize only the in-memory validator input; the canonical schema file is untouched.
var schemaText = await File.ReadAllTextAsync(schemaPath);
schemaText = schemaText.Replace("\"$defs\"", "\"definitions\"", StringComparison.Ordinal)
    .Replace("#/$defs/", "#/definitions/", StringComparison.Ordinal);
var schema = await JsonSchema.FromJsonAsync(schemaText);
var pass = 0;
var fail = 0;
var files = Directory.GetFiles(cardsRoot, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

foreach (var file in files)
{
    var json = await File.ReadAllTextAsync(file);
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file));
    if (document.RootElement.ValueKind != JsonValueKind.Array)
    {
        Console.WriteLine($"FAIL file={Path.GetFileName(file)} error=root_not_array severity=ERROR");
        fail++;
        continue;
    }

    var cards = document.RootElement.EnumerateArray().ToArray();
    var errors = schema.Validate(json);
    if (errors.Count == 0)
    {
        pass += cards.Length;
        continue;
    }

    var failedIndexes = new HashSet<int>();
    foreach (var error in errors)
    {
        var match = Regex.Match(error.Path ?? string.Empty, @"#/(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var index) && index < cards.Length)
        {
            failedIndexes.Add(index);
            var card = cards[index];
            var id = card.TryGetProperty("id", out var idProperty) ? idProperty.GetString() ?? "<missing>" : "<missing>";
            Console.WriteLine($"FAIL file={Path.GetFileName(file)} card={id} path={error.Path} error={error.Kind} severity=ERROR message={error}");
        }
        else
        {
            Console.WriteLine($"FAIL file={Path.GetFileName(file)} card=<file> path={error.Path} error={error.Kind} severity=ERROR message={error}");
        }
    }

    fail += failedIndexes.Count == 0 ? cards.Length : failedIndexes.Count;
    pass += failedIndexes.Count == 0 ? 0 : cards.Length - failedIndexes.Count;
}

Console.WriteLine($"SCHEMA_VALIDATION pass={pass} fail={fail} files={files.Length} schema={Path.GetRelativePath(root, schemaPath)}");
return fail == 0 ? 0 : 1;
