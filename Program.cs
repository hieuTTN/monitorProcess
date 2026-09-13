// using monitorProcess.Services;

// var userResolver = new UserResolverService();
// var enumerator = new ProcessEnumeratorService(userResolver);
// var pathChecker = new PathAnomalyDetectionService();
// var hashService = new HashService();
// var whitelist = new WhitelistService("Data/whitelist.json");
// var dangerousDb = new DangerousProcessDbService("Data/dangerous_processes.json", hashService);

// var processes = enumerator.GetAllProcesses();
// Console.WriteLine($"Tổng số tiến trình: {processes.Count}\n");

// foreach (var p in processes.OrderBy(x => x.Pid).Take(15))
// {
//     Console.WriteLine(p);

//     var pathAlert = pathChecker.Check(p);
//     if (pathAlert != null) Console.WriteLine("  ⚠ " + pathAlert.Message);

//     var wlAlert = whitelist.Check(p);
//     if (wlAlert != null) Console.WriteLine("  ⚠ " + wlAlert.Message);

//     var dbAlert = dangerousDb.Check(p);
//     if (dbAlert != null) Console.WriteLine("  ⚠ " + dbAlert.Message);
// }
