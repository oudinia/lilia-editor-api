using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace Lilia.Api.Services;

public interface ICompilationQueueService
{
    Task<CompilationResult> CompileLatexAsync(string latex, CompilationType type, int timeoutSeconds = 30);
    int QueueLength { get; }
    int ActiveCompilations { get; }
    double CacheHitRate { get; }
    double AvgCompilationTimeMs { get; }
}

public record CompilationResult(bool Success, byte[]? Output, string? Error, string[] Warnings, TimeSpan Duration);

public enum CompilationType { Validate, Pdf, Png, Svg }

public class CompilationQueueService : ICompilationQueueService, IDisposable
{
    private readonly ILogger<CompilationQueueService> _logger;
    private readonly Channel<CompilationRequest> _channel;
    private readonly SemaphoreSlim _workerSemaphore;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _workers;

    private int _activeCompilations;
    private long _totalCompilations;
    private long _cacheHits;
    private long _totalDurationTicks;

    // Simple in-memory cache for validation results
    private readonly ConcurrentDictionary<string, CompilationResult> _cache = new();
    private const int MaxCacheSize = 200;

    public int QueueLength => _channel.Reader.Count;
    public int ActiveCompilations => _activeCompilations;
    public double CacheHitRate => _totalCompilations > 0
        ? (double)_cacheHits / _totalCompilations * 100
        : 0;
    public double AvgCompilationTimeMs => _totalCompilations - _cacheHits > 0
        ? (double)_totalDurationTicks / (_totalCompilations - _cacheHits) / TimeSpan.TicksPerMillisecond
        : 0;

    public CompilationQueueService(ILogger<CompilationQueueService> logger, IConfiguration configuration)
    {
        _logger = logger;

        var maxConcurrent = configuration.GetValue("Compilation:MaxConcurrent", 5);
        var queueCapacity = configuration.GetValue("Compilation:QueueCapacity", 50);

        _channel = Channel.CreateBounded<CompilationRequest>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

        _workerSemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);

        // Start worker tasks that process the channel
        _workers = new Task[maxConcurrent];
        for (var i = 0; i < maxConcurrent; i++)
        {
            _workers[i] = Task.Run(() => ProcessQueueAsync(_cts.Token));
        }

        _logger.LogInformation("CompilationQueueService started with {MaxConcurrent} workers and queue capacity {QueueCapacity}",
            maxConcurrent, queueCapacity);
    }

    public async Task<CompilationResult> CompileLatexAsync(string latex, CompilationType type, int timeoutSeconds = 30)
    {
        Interlocked.Increment(ref _totalCompilations);

        // Check cache for validation requests
        if (type == CompilationType.Validate)
        {
            var cacheKey = $"validate:{latex.GetHashCode()}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                Interlocked.Increment(ref _cacheHits);
                return cached;
            }
        }

        var request = new CompilationRequest(latex, type, timeoutSeconds);

        await _channel.Writer.WriteAsync(request, request.CancellationToken);

        // Wait for result with timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 5));
        try
        {
            return await request.CompletionSource.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            return new CompilationResult(false, null, $"Compilation timed out after {timeoutSeconds}s", [], TimeSpan.FromSeconds(timeoutSeconds));
        }
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            await _workerSemaphore.WaitAsync(cancellationToken);
            Interlocked.Increment(ref _activeCompilations);

            try
            {
                var sw = Stopwatch.StartNew();
                var result = await ExecuteCompilationAsync(request);
                sw.Stop();

                Interlocked.Add(ref _totalDurationTicks, sw.Elapsed.Ticks);

                var finalResult = result with { Duration = sw.Elapsed };

                // Cache validation results
                if (request.Type == CompilationType.Validate && finalResult.Success)
                {
                    var cacheKey = $"validate:{request.Latex.GetHashCode()}";
                    if (_cache.Count >= MaxCacheSize)
                    {
                        // Simple eviction: clear half the cache
                        var keysToRemove = _cache.Keys.Take(MaxCacheSize / 2).ToList();
                        foreach (var key in keysToRemove)
                            _cache.TryRemove(key, out _);
                    }
                    _cache.TryAdd(cacheKey, finalResult);
                }

                request.CompletionSource.TrySetResult(finalResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Compilation failed for {Type}", request.Type);
                request.CompletionSource.TrySetResult(
                    new CompilationResult(false, null, ex.Message, [], TimeSpan.Zero));
            }
            finally
            {
                Interlocked.Decrement(ref _activeCompilations);
                _workerSemaphore.Release();
            }
        }
    }

    private async Task<CompilationResult> ExecuteCompilationAsync(CompilationRequest request)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"lilia-compile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            var texPath = Path.Combine(tmpDir, "document.tex");
            var pdfPath = Path.Combine(tmpDir, "document.pdf");
            var logPath = Path.Combine(tmpDir, "document.log");
            await File.WriteAllTextAsync(texPath, request.Latex, request.CancellationToken);

            switch (request.Type)
            {
                case CompilationType.Validate:
                {
                    var (exitCode, _, stderr) = await RunProcessAsync(
                        "pdflatex",
                        $"-interaction=nonstopmode -halt-on-error -output-directory {tmpDir} {texPath}",
                        tmpDir, request.TimeoutSeconds);

                    var logContent = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "";

                    if (exitCode != 0)
                    {
                        var errorLines = logContent.Split('\n')
                            .Where(l => l.StartsWith('!') || l.Contains("Error"))
                            .Take(5)
                            .ToArray();
                        var errorMsg = errorLines.Length > 0
                            ? string.Join("\n", errorLines)
                            : stderr.Length > 500 ? stderr[..500] : stderr;
                        return new CompilationResult(false, null, $"LaTeX compilation failed:\n{errorMsg}", [], TimeSpan.Zero);
                    }

                    var warnings = logContent.Split('\n')
                        .Where(l => l.Contains("Warning"))
                        .Where(w => !w.Contains("Overfull") && !w.Contains("Underfull") && !w.Contains("Font shape"))
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Take(10)
                        .ToArray();

                    return new CompilationResult(true, null, null, warnings, TimeSpan.Zero);
                }

                case CompilationType.Pdf:
                {
                    // Two passes for references
                    for (var pass = 0; pass < 2; pass++)
                    {
                        var (exitCode, _, stderr) = await RunProcessAsync(
                            "pdflatex",
                            $"-interaction=nonstopmode -halt-on-error -output-directory {tmpDir} {texPath}",
                            tmpDir, request.TimeoutSeconds);

                        if (exitCode != 0 && pass == 1)
                        {
                            var logContent = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "";
                            var errorLines = logContent.Split('\n')
                                .Where(l => l.StartsWith('!') || l.Contains("Error"))
                                .Take(5);
                            return new CompilationResult(false, null,
                                $"LaTeX compilation failed:\n{string.Join("\n", errorLines)}", [], TimeSpan.Zero);
                        }
                    }

                    if (!File.Exists(pdfPath))
                        return new CompilationResult(false, null, "PDF was not generated", [], TimeSpan.Zero);

                    var pdf = await File.ReadAllBytesAsync(pdfPath);
                    return new CompilationResult(true, pdf, null, [], TimeSpan.Zero);
                }

                case CompilationType.Png:
                {
                    // Compile to PDF first, then convert
                    var (exitCode, _, _) = await RunProcessAsync(
                        "pdflatex",
                        $"-interaction=nonstopmode -halt-on-error -output-directory {tmpDir} {texPath}",
                        tmpDir, request.TimeoutSeconds);

                    if (exitCode != 0 || !File.Exists(pdfPath))
                        return new CompilationResult(false, null, "LaTeX compilation failed", [], TimeSpan.Zero);

                    var outputPrefix = Path.Combine(tmpDir, "output");
                    await RunProcessAsync("pdftoppm", $"-png -r 150 -singlefile {pdfPath} {outputPrefix}", tmpDir, 10);
                    var pngPath = outputPrefix + ".png";

                    if (!File.Exists(pngPath))
                        return new CompilationResult(false, null, "PNG conversion failed", [], TimeSpan.Zero);

                    var png = await File.ReadAllBytesAsync(pngPath);
                    return new CompilationResult(true, png, null, [], TimeSpan.Zero);
                }

                case CompilationType.Svg:
                {
                    var (exitCode, _, stderr) = await RunProcessAsync(
                        "latex",
                        $"-interaction=nonstopmode -halt-on-error -output-directory {tmpDir} {texPath}",
                        tmpDir, request.TimeoutSeconds);

                    if (exitCode != 0)
                        return new CompilationResult(false, null, $"LaTeX compilation failed: {stderr}", [], TimeSpan.Zero);

                    var dviPath = Path.Combine(tmpDir, "document.dvi");
                    var svgPath = Path.Combine(tmpDir, "document.svg");

                    if (!File.Exists(dviPath))
                        return new CompilationResult(false, null, "DVI was not generated", [], TimeSpan.Zero);

                    await RunProcessAsync("dvisvgm",
                        $"--no-fonts --exact-bbox --zoom=1.4 -o {svgPath} {dviPath}", tmpDir, 10);

                    if (!File.Exists(svgPath))
                        return new CompilationResult(false, null, "SVG conversion failed", [], TimeSpan.Zero);

                    var svg = await File.ReadAllTextAsync(svgPath);
                    if (svg.StartsWith("<?xml"))
                    {
                        var idx = svg.IndexOf("?>");
                        if (idx > 0) svg = svg[(idx + 2)..].TrimStart();
                    }

                    return new CompilationResult(true, Encoding.UTF8.GetBytes(svg), null, [], TimeSpan.Zero);
                }

                default:
                    return new CompilationResult(false, null, $"Unknown compilation type: {request.Type}", [], TimeSpan.Zero);
            }
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string command, string arguments, string workingDir, int timeoutSeconds)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException($"Process timed out after {timeoutSeconds}s");
        }

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try { Task.WhenAll(_workers).Wait(TimeSpan.FromSeconds(5)); } catch { }
        _cts.Dispose();
        _workerSemaphore.Dispose();
    }

    private sealed class CompilationRequest
    {
        public string Latex { get; }
        public CompilationType Type { get; }
        public int TimeoutSeconds { get; }
        public TaskCompletionSource<CompilationResult> CompletionSource { get; } = new();
        public CancellationToken CancellationToken { get; }

        public CompilationRequest(string latex, CompilationType type, int timeoutSeconds)
        {
            Latex = latex;
            Type = type;
            TimeoutSeconds = timeoutSeconds;
            CancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 10)).Token;
        }
    }
}
