using AiBurgerClock;

try
{
    int total = await SharedTestSuite.RunAllAsync();
    Console.WriteLine($"PASS ALL SHARED: {total:N0} assertions");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL: " + error);
    return 1;
}
