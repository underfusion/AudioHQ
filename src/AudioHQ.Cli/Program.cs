using System.Runtime.InteropServices;
using AudioHQ.Core;

Console.WriteLine($"AudioHQ CLI v{AppVersion.Display}");
Console.WriteLine();

var devices = AudioDevices.GetActiveRenderDevices();
var source = AudioDevices.GetDefaultRender();

Console.WriteLine($"Source (Windows default output): {source.FriendlyName}");
Console.WriteLine();
Console.WriteLine("Available outputs:");
for (int i = 0; i < devices.Count; i++)
{
    string marker = devices[i].ID == source.ID ? "  <- default (source)" : "";
    Console.WriteLine($"  [{i}] {devices[i].FriendlyName}{marker}");
}

bool firstTry = true;
while (true)
{
    int index;
    if (firstTry && args.Length > 0)
    {
        index = int.Parse(args[0]);
    }
    else
    {
        Console.Write("\nPick target output index (q to quit): ");
        string? line = Console.ReadLine();
        if (line is null || line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
            return;
        if (!int.TryParse(line, out index) || index < 0 || index >= devices.Count)
        {
            Console.WriteLine("Invalid index.");
            continue;
        }
    }
    firstTry = false;

    var target = devices[index];
    try
    {
        using var engine = new MirrorEngine();
        var liveSource = AudioDevices.FindRenderById(source.ID)
            ?? throw new InvalidOperationException("Source device is no longer active.");
        var liveTarget = AudioDevices.FindRenderById(target.ID)
            ?? throw new InvalidOperationException("Target device is no longer active.");

        engine.Start(liveSource);
        engine.AddOutput(liveTarget);
        Console.WriteLine($"\nMirroring '{source.FriendlyName}' -> '{target.FriendlyName}'");
        Console.WriteLine("Play some music. Press Enter to stop.");
        Console.ReadLine();
        return;
    }
    catch (COMException ex)
    {
        string reason = (uint)ex.HResult switch
        {
            0x8889000A => "device is in use in EXCLUSIVE mode by another application - close it or pick a different output",
            0x88890008 => "device does not support the required audio format",
            0x88890004 => "device is not available (unplugged or disabled?)",
            _ => $"audio device error 0x{ex.HResult:X8}",
        };
        Console.WriteLine($"\nCannot open '{target.FriendlyName}': {reason}");
    }
}
