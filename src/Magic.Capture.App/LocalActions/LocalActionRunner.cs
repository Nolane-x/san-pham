using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Magic.Capture.App.Capture;
using Magic.Capture.App.Persistence;
using Magic.Capture.Core.Capture;
using Magic.Capture.Core.LocalActions;

namespace Magic.Capture.App.LocalActions;

internal sealed record LocalActionApprovalRequest(LocalActionProfile Profile, string ExecutablePath, string Sha256);

internal sealed record LocalActionExecutionContext(
    CaptureAsset Asset,
    IReadOnlyDictionary<string, object?> Values,
    Func<LocalActionApprovalRequest, CancellationToken, Task<bool>>? ConfirmApprovalAsync = null);

internal sealed record LocalActionExecutionResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    byte[]? OutputBytes,
    string? OutputText,
    string? OutputFileExtension,
    TimeSpan Duration);

internal sealed class LocalActionRunner
{
    private readonly AppPaths _paths;
    private readonly LocalActionApprovalStore _approvals;

    public LocalActionRunner(AppPaths paths, LocalActionApprovalStore approvals)
    {
        _paths = paths;
        _approvals = approvals;
    }

    public async Task<LocalActionExecutionResult> ExecuteAsync(
        LocalActionProfile profile,
        LocalActionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var validation = LocalActionProfileValidator.Validate(profile);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
        if (!profile.Enabled) throw new InvalidOperationException($"Local Action '{profile.Name}' is disabled.");

        var executablePath = Path.GetFullPath(profile.ExecutablePath);
        if (!File.Exists(executablePath)) throw new FileNotFoundException("Local Action executable was not found.", executablePath);
        var executableHash = await ComputeSha256Async(executablePath, cancellationToken);
        if (!await _approvals.IsApprovedAsync(executablePath, executableHash, cancellationToken))
        {
            if (context.ConfirmApprovalAsync is null)
                throw new InvalidOperationException("This Local Action executable has not been approved. Run it interactively once to review and approve the program.");
            var approved = await context.ConfirmApprovalAsync(new LocalActionApprovalRequest(profile, executablePath, executableHash), cancellationToken);
            if (!approved) throw new InvalidOperationException("Local Action execution was canceled before program approval.");
            await _approvals.ApproveAsync(executablePath, executableHash, cancellationToken);
        }

        var tempRoot = Path.Combine(_paths.LocalActionTempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var inputPath = Path.Combine(tempRoot, "input.png");
        var outputPath = Path.Combine(tempRoot, "output" + profile.OutputFileExtension);
        try
        {
            await File.WriteAllBytesAsync(inputPath, context.Asset.PngBytes, cancellationToken);
            var variables = BuildVariables(context, inputPath, outputPath);
            var startInfo = BuildStartInfo(profile, executablePath, variables);

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var startedAt = Stopwatch.GetTimestamp();
            if (!process.Start()) throw new InvalidOperationException($"Local Action '{profile.Name}' could not start.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(profile.TimeoutMilliseconds);
            var token = timeout.Token;
            var stdoutTask = ReadBoundedAsync(process.StandardOutput.BaseStream, profile.MaxStdoutBytes, token);
            var stderrTask = ReadBoundedAsync(process.StandardError.BaseStream, profile.MaxStderrBytes, token);
            try
            {
                await process.WaitForExitAsync(token);
                var stdoutRead = await stdoutTask;
                var stderrRead = await stderrTask;
                if (stdoutRead.Exceeded) throw new InvalidOperationException($"Local Action stdout exceeded the configured {profile.MaxStdoutBytes:N0}-byte limit.");
                if (stderrRead.Exceeded) throw new InvalidOperationException($"Local Action stderr exceeded the configured {profile.MaxStderrBytes:N0}-byte limit.");
                var stdout = Encoding.UTF8.GetString(stdoutRead.Bytes);
                var stderr = Encoding.UTF8.GetString(stderrRead.Bytes);
                var duration = Stopwatch.GetElapsedTime(startedAt);

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(BuildExitFailure(profile, process.ExitCode, stderr));

                var (outputBytes, outputText, outputExtension) = await ReadOutputAsync(profile, outputPath, stdout, cancellationToken);
                return new LocalActionExecutionResult(process.ExitCode, stdout, stderr, outputBytes, outputText, outputExtension, duration);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                TryKill(process);
                throw new TimeoutException($"Local Action '{profile.Name}' exceeded its {profile.TimeoutMilliseconds:N0} ms timeout.");
            }
            catch
            {
                TryKill(process);
                throw;
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static ProcessStartInfo BuildStartInfo(LocalActionProfile profile, string executablePath, IReadOnlyDictionary<string, string> variables)
    {
        var info = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = string.IsNullOrWhiteSpace(profile.WorkingDirectory)
                ? Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
                : Path.GetFullPath(profile.WorkingDirectory)
        };

        foreach (var template in profile.Arguments)
            info.ArgumentList.Add(LocalActionTemplate.Expand(template, variables));
        return info;
    }

    private static IReadOnlyDictionary<string, string> BuildVariables(LocalActionExecutionContext context, string inputPath, string outputPath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = inputPath,
            ["output"] = outputPath,
            ["width"] = context.Asset.Width.ToString(CultureInfo.InvariantCulture),
            ["height"] = context.Asset.Height.ToString(CultureInfo.InvariantCulture),
            ["windowTitle"] = context.Asset.SourceDisplayName ?? string.Empty,
            ["source"] = context.Asset.SourceDisplayName ?? context.Asset.SourceKind.ToString(),
            ["captureId"] = context.Asset.Id.ToString("N"),
            ["utc"] = context.Asset.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)
        };

        foreach (var (key, value) in context.Values)
        {
            if (value is null || values.ContainsKey(key)) continue;
            if (value is byte[] or CaptureAsset) continue;
            values[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
        if (context.Values.TryGetValue("text", out var text) && text is not null)
            values["ocrText"] = Convert.ToString(text, CultureInfo.InvariantCulture) ?? string.Empty;
        else
            values["ocrText"] = string.Empty;
        return values;
    }

    private static async Task<(byte[]? Bytes, string? Text, string? Extension)> ReadOutputAsync(
        LocalActionProfile profile,
        string outputPath,
        string stdout,
        CancellationToken cancellationToken)
    {
        if (profile.OutputMode == LocalActionOutputMode.None) return (null, null, null);
        if (profile.OutputMode == LocalActionOutputMode.StdoutText) return (null, stdout, null);
        if (!File.Exists(outputPath))
            throw new InvalidOperationException($"Local Action '{profile.Name}' did not create the expected $output file.");

        var file = new FileInfo(outputPath);
        if (file.Length > profile.MaxOutputFileBytes)
            throw new InvalidOperationException($"Local Action '{profile.Name}' output file exceeded the configured {profile.MaxOutputFileBytes:N0}-byte limit.");
        var bytes = await ReadBoundedFileAsync(outputPath, profile.MaxOutputFileBytes, cancellationToken);

        if (profile.OutputMode == LocalActionOutputMode.OutputFileImage)
        {
            if (!PngDimensions.TryRead(bytes, out _, out _))
                throw new InvalidDataException("Local Action image output is not a valid PNG. Use .png output for image chaining.");
            return (bytes, null, ".png");
        }
        if (profile.OutputMode == LocalActionOutputMode.OutputFileText)
            return (bytes, DecodeUtf8Text(bytes), profile.OutputFileExtension);

        if (PngDimensions.TryRead(bytes, out _, out _)) return (bytes, null, ".png");
        return (bytes, DecodeUtf8Text(bytes), profile.OutputFileExtension);
    }

    private static async Task<byte[]> ReadBoundedFileAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length > maximumBytes) throw new InvalidDataException("Local Action output file exceeded its configured size limit.");
        using var output = new MemoryStream((int)Math.Min(input.Length, 64 * 1024));
        var rented = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            var total = 0;
            while (true)
            {
                var read = await input.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > maximumBytes) throw new InvalidDataException("Local Action output file exceeded its configured size limit.");
                await output.WriteAsync(rented.AsMemory(0, read), cancellationToken);
            }
            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static string DecodeUtf8Text(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new InvalidDataException("Local Action text output is not valid UTF-8."); }
    }

    private sealed record BoundedReadResult(byte[] Bytes, bool Exceeded);

    private static async Task<BoundedReadResult> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var rented = ArrayPool<byte>.Shared.Rent(32 * 1024);
        var exceeded = false;
        var stored = 0;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken);
                if (read == 0) break;
                var remaining = Math.Max(0, maximumBytes - stored);
                var toStore = Math.Min(read, remaining);
                if (toStore > 0)
                {
                    await output.WriteAsync(rented.AsMemory(0, toStore), cancellationToken);
                    stored += toStore;
                }
                if (toStore < read) exceeded = true;
            }
            return new BoundedReadResult(output.ToArray(), exceeded);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken);
                if (read == 0) break;
                hash.AppendData(rented, 0, read);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static string BuildExitFailure(LocalActionProfile profile, int exitCode, string stderr)
    {
        var detail = string.IsNullOrWhiteSpace(stderr) ? string.Empty : " " + stderr.Trim();
        if (detail.Length > 1_024) detail = detail[..1_024] + "…";
        return $"Local Action '{profile.Name}' exited with code {exitCode}.{detail}";
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
