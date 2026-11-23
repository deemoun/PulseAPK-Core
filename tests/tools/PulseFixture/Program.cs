// The test copies a private executable and its mode file into each scenario directory.
string mode = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mode"));
switch (mode)
{
    case "build-edited":
    case "decode-edited":
        if (mode == "build-edited") File.WriteAllText(args[3], "rebuilt");
        else File.WriteAllText(Path.Combine(Directory.CreateDirectory(args[3]).FullName, "result.txt"), "decoded");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ready"), "ready");
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            while (!File.Exists(Path.Combine(AppContext.BaseDirectory, "resume"))) await Task.Delay(10, timeout.Token);
        return 0;
    case "sign-output":
    case "decode-output":
        for (int i = 0; i < 4096; i++)
        {
            Console.WriteLine($"Decoded resource {i} — ✓");
            Console.Error.WriteLine($"Diagnostic for resource {i}");
        }
        if (mode == "sign-output") File.WriteAllText(Path.Combine(args[3], "app-signed.apk"), "signed");
        Console.Write("last output");
        Console.Error.Write("last diagnostic");
        return 0;
    case "wait":
        File.WriteAllText(args[1] + ".pid", Environment.ProcessId.ToString());
        await Task.Delay(TimeSpan.FromSeconds(30));
        return 0;
    case "decode-wait":
    case "decode-fail":
        Directory.CreateDirectory(args[3]);
        File.WriteAllText(Path.Combine(args[3], "new.txt"), "partial");
        File.WriteAllText(args[1] + ".ready", "ready");
        if (mode == "decode-wait") await Task.Delay(TimeSpan.FromSeconds(30));
        return 9;
    case "signed":
        File.WriteAllText(Path.Combine(args[3], "app-signed.apk"), "signed");
        return 0;
    default:
        if (args[0] == "b") File.WriteAllText(args[3], "fresh");
        return int.Parse(mode);
}
