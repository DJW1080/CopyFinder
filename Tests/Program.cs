using System;

Console.WriteLine("CopyFinder smoke test starting.");

try
{
    // If we got here, the executable started.
    Console.WriteLine("PASS - application started.");
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL - {ex}");
    Environment.Exit(1);
}

Console.WriteLine("All smoke tests passed.");
