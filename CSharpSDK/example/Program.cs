using System;
using Xvirus;

// Print the SDK version
Console.WriteLine("Xvirus SDK " + XvirusSDK.Version());

// Load the engine (detection databases, AI model, heuristics)
XvirusSDK.Load();

// --- Single file scan ---
string targetFile = @"C:\Windows\System32\notepad.exe"; // change to the file you want to scan
var result = XvirusSDK.Scan(targetFile);

Console.WriteLine($"\nFile:      {result.Path}");
Console.WriteLine($"Malware:   {result.IsMalware}");
Console.WriteLine($"Detection: {result.Name}");
Console.WriteLine($"Score:     {result.MalwareScore:P1}");

// --- Folder scan ---
string targetFolder = @"C:\Windows\System32"; // change to the folder you want to scan
Console.WriteLine($"\nScanning folder: {targetFolder}");

int total = 0;
foreach (var r in XvirusSDK.ScanFolder(targetFolder))
{
    total++;
    if (r.IsMalware)
        Console.WriteLine($"  THREAT  {r.Name,-30} {r.Path}");
}

Console.WriteLine($"Scanned {total} file(s).");

// --- Check for updates ---
string updateInfo = XvirusSDK.CheckUpdates();
Console.WriteLine($"\nUpdate info: {updateInfo}");

// Release all engine resources
XvirusSDK.Unload();
