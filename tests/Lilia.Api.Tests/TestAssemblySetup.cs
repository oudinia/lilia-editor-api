using System.Runtime.CompilerServices;

namespace Lilia.Api.Tests;

/// <summary>
/// Run once when the test assembly loads, before any test or host.
///
/// <para>Since the database tests run in parallel (1 Nov 2026), several API hosts live in this one
/// process, and the compile slots (CompileSlots) are static: the server's 6 TeX and 2 Typst slots were
/// shared by every host and every compile test, requests queued for their minute and timed out, and
/// tests slowed tenfold until the 3-minute hang guard killed the run. The tests get more slots and a
/// longer wait (Program.cs keeps them: it only resizes when configuration says so), and a thread pool
/// that starts large enough for many hosts at once.</para>
///
/// <para>Hosts don't reload their settings: each would watch its files with an inotify instance, and with
/// a host per class the account's limit (128) ran out mid-run and every later host failed to start.</para>
/// </summary>
internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.SetMinThreads(Math.Max(workers, 64), Math.Max(io, 64));
        Lilia.Engines.CompileSlots.Configure(slots: 4 * Environment.ProcessorCount, maxWaitSeconds: 300,
            typstSlots: 2 * Environment.ProcessorCount);
    }
}
