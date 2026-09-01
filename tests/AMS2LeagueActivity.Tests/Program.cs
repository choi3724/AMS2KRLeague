using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AMS2LeagueActivity.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            IReadOnlyList<TestCase> tests = ActivityCaptureTests.Cases()
                .Concat(UploadQueueTests.Cases())
                .Concat(PayloadContractTests.Cases())
                .ToArray();
            int passed = 0;
            int failed = 0;
            var suite = Stopwatch.StartNew();

            foreach (TestCase test in tests)
            {
                var timer = Stopwatch.StartNew();
                try
                {
                    test.Test();
                    passed++;
                    Console.WriteLine("PASS " + test.Name + " (" + timer.ElapsedMilliseconds + " ms)");
                }
                catch (Exception exception)
                {
                    failed++;
                    Console.Error.WriteLine("FAIL " + test.Name + " (" + timer.ElapsedMilliseconds + " ms)");
                    Console.Error.WriteLine(exception.ToString());
                }
            }

            Console.WriteLine(
                "RESULT: " + passed + " passed, " + failed + " failed, " + tests.Count +
                " total (" + suite.ElapsedMilliseconds + " ms)");
            return failed == 0 ? 0 : 1;
        }
    }
}
