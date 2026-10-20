using FlowSight.Cli;

const string usage = "usage: flowsight demo --out <dir> [--seed <n>] [--requests <n>]";

if (args.Length == 0 || args[0] != "demo")
{
    Console.Error.WriteLine(usage);
    return 1;
}

string? outDir = null;
int seed = 42, requests = 2000;
for (var i = 1; i < args.Length; i++)
{
    var value = i + 1 < args.Length ? args[i + 1] : null;
    switch (args[i])
    {
        case "--out" when value is not null: outDir = value; i++; break;
        case "--seed" when int.TryParse(value, out var s): seed = s; i++; break;
        case "--requests" when int.TryParse(value, out var r) && r > 0: requests = r; i++; break;
        default:
            Console.Error.WriteLine(usage);
            return 1;
    }
}

if (outDir is null)
{
    Console.Error.WriteLine(usage);
    return 1;
}

DemoExporter.Run(outDir, seed, requests);
Console.WriteLine($"Wrote {requests} requests to {Path.GetFullPath(outDir)}");
return 0;
