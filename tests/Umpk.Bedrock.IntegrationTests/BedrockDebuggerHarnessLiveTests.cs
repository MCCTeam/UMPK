using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Bedrock.IntegrationTests;

/// <summary>Exercises the opt-in GDB child seam against a fresh isolated offline BDS without touching any server owned by another run.</summary>
public sealed class BedrockDebuggerHarnessLiveTests(ITestOutputHelper output)
{
    /// <summary>Starts one fresh exact-SHA offline BDS under GDB, proves distinct launcher and inferior PIDs, sends one harmless console line, stops gracefully, and proves the exact inferior PID exited.</summary>
    [BedrockLiveFact]
    public async Task DebuggerChildSmokeStartsAndStopsOwnedInferior()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = overallTimeout.Token;
        string commandFile = Path.Combine(Path.GetTempPath(), $"umpk-gdb-{Guid.NewGuid():N}.gdb");
        try
        {
            await File.WriteAllTextAsync(commandFile, BuildGdbCommandScript(), ct).ConfigureAwait(false);
            await using var harness = new BedrockServerHarness(debuggerCommandFile: commandFile);
            await harness.StartAsync(ct).ConfigureAwait(false);
            Assert.True(harness.IsDebuggerLaunch);
            Assert.Equal(commandFile, harness.DebuggerCommandFile);
            int launcherPid = harness.LauncherProcessId;
            int inferiorPid = harness.InferiorProcessId;
            Assert.True(launcherPid > 0);
            Assert.True(inferiorPid > 0);
            Assert.NotEqual(launcherPid, inferiorPid);
            Assert.Contains(harness.Output, static line => line.Contains("Server started.", StringComparison.Ordinal));
            Assert.True(File.Exists(harness.InferiorPidMarkerPath));
            string copiedExecutableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(harness.RunDirectory, "bedrock_server"), ct).ConfigureAwait(false))).ToLowerInvariant();
            Assert.Equal(BedrockServerHarness.ExpectedExecutableSha256, copiedExecutableSha256);
            output.WriteLine($"Debugger smoke ready on IPv4 port {harness.Port} with launcher PID {launcherPid} and inferior PID {inferiorPid} (exe {harness.ExecutableSha256}).");
            int outputBeforeList = harness.Output.Count;
            await harness.SendCommandAsync("list", ct).ConfigureAwait(false);
            await WaitForPlayerListAsync(harness, outputBeforeList, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            await harness.StopAsync().ConfigureAwait(false);
            await WaitForExactPidExitAsync(inferiorPid, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            string stdoutPath = Path.Combine(harness.EvidenceDirectory, "bds-stdout.log");
            string stdoutSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(stdoutPath, ct).ConfigureAwait(false))).ToLowerInvariant();
            var evidence = new
            {
                version = "1.26.45.1-1",
                protocol = 2169,
                sourceArtifactSha256 = BedrockServerHarness.ExpectedSourceSha256,
                executableSha256 = harness.ExecutableSha256,
                copiedExecutableSha256,
                configSha256 = harness.ConfigSha256,
                harness.Port,
                launcherPid,
                inferiorPid,
                consoleListObserved = true,
                inferiorExited = true,
                stdoutSha256,
            };
            await File.WriteAllTextAsync(Path.Combine(harness.EvidenceDirectory, "debugger-smoke.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
            output.WriteLine($"Debugger smoke stopped owned launcher PID {launcherPid} and verified exact inferior PID {inferiorPid} exited.");
        }
        finally
        {
            try
            {
                if (File.Exists(commandFile))
                    File.Delete(commandFile);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Builds the temporary GDB script that reports the inferior PID to the harness marker file and then continues the owned inferior.</summary>
    private static string BuildGdbCommandScript()
    {
        string pidVariable = BedrockServerHarness.InferiorPidFileEnvironmentVariable;
        return string.Join(Environment.NewLine, ["set pagination off", "set confirm off", "set debuginfod enabled off", "set disable-randomization off", "starti", "python", "import os", $"pid_path = os.environ[\"{pidVariable}\"]", "with open(pid_path, \"w\") as marker:", "    marker.write(str(gdb.selected_inferior().pid))", "end", "continue", string.Empty]);
    }

    /// <summary>Starts one fresh exact-SHA offline BDS under two auto-continue software breakpoints on the generic tagged 24-byte append commit and proves owned-child lifecycle without requiring any append hit.</summary>
    [BedrockLiveFact]
    public async Task BreakpointSmokeInitializesTwoSoftwareBreakpoints()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(7));
        CancellationToken ct = overallTimeout.Token;
        string commandFile = Path.Combine(Path.GetTempPath(), $"umpk-gdb-break-{Guid.NewGuid():N}.gdb");
        try
        {
            string script = BuildBreakpointSmokeScript();
            await File.WriteAllTextAsync(commandFile, script, ct).ConfigureAwait(false);
            string scriptSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(script))).ToLowerInvariant();
            await using var harness = new BedrockServerHarness(debuggerCommandFile: commandFile);
            await harness.StartAsync(ct).ConfigureAwait(false);
            Assert.True(harness.IsDebuggerLaunch);
            int launcherPid = harness.LauncherProcessId;
            int inferiorPid = harness.InferiorProcessId;
            Assert.True(launcherPid > 0);
            Assert.True(inferiorPid > 0);
            Assert.NotEqual(launcherPid, inferiorPid);
            Assert.Contains(harness.Output, static line => line.Contains("Server started.", StringComparison.Ordinal));
            string copiedExecutableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(harness.RunDirectory, "bedrock_server"), ct).ConfigureAwait(false))).ToLowerInvariant();
            Assert.Equal(BedrockServerHarness.ExpectedExecutableSha256, copiedExecutableSha256);
            string initPath = Path.Combine(harness.EvidenceDirectory, "breakpoint-init.log");
            string eventsPath = Path.Combine(harness.EvidenceDirectory, "append-events.jsonl");
            Assert.True(File.Exists(initPath));
            Assert.True(File.Exists(eventsPath));
            string initText = await File.ReadAllTextAsync(initPath, ct).ConfigureAwait(false);
            Assert.Contains("kind=software break *addr", initText, StringComparison.Ordinal);
            Assert.Contains("breakpoints=2", initText, StringComparison.Ordinal);
            long bias = ParseHexValue(initText, "bias=");
            long preAddr = ParseHexValue(initText, "pre_addr=");
            long postAddr = ParseHexValue(initText, "post_addr=");
            Assert.True(bias > 0);
            Assert.Equal(0x6e0f95bL, preAddr - bias);
            Assert.Equal(0x6e0f962L, postAddr - bias);
            Assert.NotEqual(preAddr, postAddr);
            output.WriteLine($"Breakpoint smoke ready on IPv4 port {harness.Port} with launcher PID {launcherPid} and inferior PID {inferiorPid} (bias 0x{bias:x} pre 0x{preAddr:x} post 0x{postAddr:x}).");
            int outputBeforeList = harness.Output.Count;
            await harness.SendCommandAsync("list", ct).ConfigureAwait(false);
            await WaitForPlayerListAsync(harness, outputBeforeList, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            string[] eventLines = await File.ReadAllLinesAsync(eventsPath, ct).ConfigureAwait(false);
            int preHits = eventLines.Count(static line => line.Contains("\"pre\"", StringComparison.Ordinal));
            int postHits = eventLines.Count(static line => line.Contains("\"post\"", StringComparison.Ordinal));
            Assert.True(eventLines.Length <= 1001);
            int pairedCommits = CountPairedCommits(eventLines);
            output.WriteLine($"Breakpoint smoke observed {preHits} pre hits and {postHits} post hits with {pairedCommits} best-effort pairs (zero hits permitted for this smoke).");
            await harness.StopAsync().ConfigureAwait(false);
            await WaitForExactPidExitAsync(inferiorPid, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            await WaitForExactPidExitAsync(launcherPid, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            string stdoutPath = Path.Combine(harness.EvidenceDirectory, "bds-stdout.log");
            string stdoutSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(stdoutPath, ct).ConfigureAwait(false))).ToLowerInvariant();
            var evidence = new
            {
                version = "1.26.45.1-1",
                protocol = 2169,
                sourceArtifactSha256 = BedrockServerHarness.ExpectedSourceSha256,
                executableSha256 = harness.ExecutableSha256,
                copiedExecutableSha256,
                configSha256 = harness.ConfigSha256,
                scriptSha256,
                harness.Port,
                launcherPid,
                inferiorPid,
                bias = $"0x{bias:x}",
                preAddr = $"0x{preAddr:x}",
                postAddr = $"0x{postAddr:x}",
                breakpointKind = "software break *addr",
                preHits,
                postHits,
                pairedCommits,
                consoleListObserved = true,
                inferiorExited = true,
                launcherExited = true,
                stdoutSha256,
            };
            await File.WriteAllTextAsync(Path.Combine(harness.EvidenceDirectory, "breakpoint-smoke.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
            output.WriteLine($"Breakpoint smoke stopped owned launcher PID {launcherPid} and verified exact inferior PID {inferiorPid} exited with evidence at {harness.EvidenceDirectory}.");
        }
        finally
        {
            try
            {
                if (File.Exists(commandFile))
                    File.Delete(commandFile);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Rejects breakpoint observations with a different thread, tag, or owner and counts a matching pre/post pair only once.</summary>
    [Fact]
    public void PairingRejectsMismatchedAndRepeatedBreakpointEvents()
    {
        string[] events =
        [
            "not-json",
            JsonSerializer.Serialize(new { kind = "error", detail = "unreadable source" }),
            JsonSerializer.Serialize(new { kind = "pre", thread = 1, tag = 0, source = "0xaa", owner = "0xbb", nextEnd = "0x123" }),
            JsonSerializer.Serialize(new { kind = "post", thread = 2, tag = 0, source = "0xaa", owner = "0xbb", actualEnd = "0x123" }),
            JsonSerializer.Serialize(new { kind = "post", thread = 1, tag = 1, source = "0xaa", owner = "0xbb", actualEnd = "0x123" }),
            JsonSerializer.Serialize(new { kind = "post", thread = 1, tag = 0, source = "0xaa", owner = "0xcc", actualEnd = "0x123" }),
            JsonSerializer.Serialize(new { kind = "post", thread = 1, tag = 0, source = "0xaa", owner = "0xbb", actualEnd = "0x123" }),
            JsonSerializer.Serialize(new { kind = "post", thread = 1, tag = 0, source = "0xaa", owner = "0xbb", actualEnd = "0x123" }),
        ];

        Assert.Equal(1, CountPairedCommits(events));
    }

    /// <summary>Builds the temporary GDB script that records the per-launch PIE bias plus both generic append commit addresses and installs two auto-continue software breakpoints.</summary>
    private static string BuildBreakpointSmokeScript()
    {
        string pidVariable = BedrockServerHarness.InferiorPidFileEnvironmentVariable;
        string evidenceVariable = BedrockServerHarness.EvidenceDirectoryEnvironmentVariable;
        string script = $$"""
            set pagination off
            set confirm off
            set debuginfod enabled off
            set disable-randomization off
            starti
            python
            import os
            import json
            import time
            pid_path = os.environ["{{pidVariable}}"]
            evidence_dir = os.environ["{{evidenceVariable}}"]
            pid = gdb.selected_inferior().pid
            with open(pid_path, "w") as marker:
                marker.write(str(pid))
            exe_path = os.readlink('/proc/%d/exe' % pid)
            matches = []
            with open('/proc/%d/maps' % pid, 'r') as maps:
                for line in maps:
                    parts = line.split()
                    if len(parts) < 6:
                        continue
                    if parts[2] != '00000000':
                        continue
                    if parts[5] != exe_path:
                        continue
                    matches.append(int(parts[0].split('-')[0], 16))
            if len(matches) != 1:
                raise gdb.GdbError('UMPK: expected exactly one offset-zero executable mapping for ' + exe_path + ' but found %d' % len(matches))
            bias = matches[0]
            pre_va = 0x6e0f95b
            post_va = 0x6e0f962
            pre_addr = bias + pre_va
            post_addr = bias + post_va
            init_path = os.path.join(evidence_dir, 'breakpoint-init.log')
            events_path = os.path.join(evidence_dir, 'append-events.jsonl')
            MAX_EVENTS = 1000
            event_count = 0
            error_count = 0
            overflow_count = 0
            def read_u64(addr):
                mem = gdb.selected_inferior().read_memory(addr, 8)
                return int.from_bytes(bytes(mem), 'little')
            def read_i32(addr):
                mem = gdb.selected_inferior().read_memory(addr, 4)
                return int.from_bytes(bytes(mem), 'little', signed=True)
            class AppendBreakpoint(gdb.Breakpoint):
                def __init__(self, address, kind):
                    super(AppendBreakpoint, self).__init__('*0x%x' % address, gdb.BP_BREAKPOINT, internal=False)
                    self.kind = kind
                    self.silent = True
                def stop(self):
                    global event_count, error_count, overflow_count
                    try:
                        if event_count >= MAX_EVENTS:
                            overflow_count += 1
                            return False
                        frame = gdb.selected_frame()
                        thread = gdb.selected_thread()
                        thread_num = thread.num if thread is not None else -1
                        rbx = int(frame.read_register('rbx'))
                        r14 = int(frame.read_register('r14'))
                        r15 = int(frame.read_register('r15'))
                        tag = read_i32(r14 + 0x10)
                        record = {'kind': self.kind, 'tMonoNs': time.monotonic_ns(), 'thread': thread_num, 'tag': tag, 'source': '0x%x' % r14, 'owner': '0x%x' % rbx, 'bias': '0x%x' % bias, 'preAddr': '0x%x' % pre_addr, 'postAddr': '0x%x' % post_addr}
                        if self.kind == 'pre':
                            record['oldEnd'] = '0x%x' % read_u64(rbx + 0xf8)
                            record['nextEnd'] = '0x%x' % r15
                            record['base'] = '0x%x' % read_u64(rbx + 0xf0)
                            record['capacity'] = '0x%x' % read_u64(rbx + 0x100)
                        else:
                            record['actualEnd'] = '0x%x' % read_u64(rbx + 0xf8)
                            record['nextEnd'] = '0x%x' % r15
                            record['base'] = '0x%x' % read_u64(rbx + 0xf0)
                            record['capacity'] = '0x%x' % read_u64(rbx + 0x100)
                        with open(events_path, 'a') as events:
                            events.write(json.dumps(record) + '\n')
                        event_count += 1
                    except Exception as ex:
                        try:
                            error_count += 1
                            if event_count < MAX_EVENTS:
                                with open(events_path, 'a') as events:
                                    events.write(json.dumps({'kind': 'error', 'detail': str(ex)[:200]}) + '\n')
                                event_count += 1
                        except Exception:
                            pass
                    return False
            pre_bp = AppendBreakpoint(pre_addr, 'pre')
            post_bp = AppendBreakpoint(post_addr, 'post')
            with open(events_path, 'w') as events:
                events.write('')
            with open(init_path, 'w') as init:
                init.write('bias=0x%x\n' % bias)
                init.write('pre_va=0x%x\n' % pre_va)
                init.write('post_va=0x%x\n' % post_va)
                init.write('pre_addr=0x%x\n' % pre_addr)
                init.write('post_addr=0x%x\n' % post_addr)
                init.write('kind=software break *addr\n')
                init.write('breakpoints=2\n')
                init.write('pre_breakpoint=%s\n' % pre_bp.number)
                init.write('post_breakpoint=%s\n' % post_bp.number)
                init.write('exe=%s\n' % exe_path)
                init.write('pid=%d\n' % pid)
            print('UMPK_BREAKPOINTS ready bias=0x%x pre=0x%x post=0x%x' % (bias, pre_addr, post_addr))
            print('UMPK_BREAKPOINT_KIND software break *addr count=2')
            end
            continue
            """;
        return script.Replace("\n", Environment.NewLine, StringComparison.Ordinal) + Environment.NewLine;
    }

    /// <summary>Parses one hexadecimal key value from the bounded breakpoint initialization log without trusting any other server output.</summary>
    private static long ParseHexValue(string initText, string key)
    {
        string? line = initText.Split('\n').FirstOrDefault(line => line.StartsWith(key, StringComparison.Ordinal));
        Assert.False(string.IsNullOrEmpty(line));
        string hex = line![key.Length..].Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        return Convert.ToInt64(hex, 16);
    }

    /// <summary>Counts pre/post pairs per thread only when tag, source, owner, and the pre next-end to post actual-end transition all match, ignoring error and malformed rows.</summary>
    private static int CountPairedCommits(string[] eventLines)
    {
        var pendingByThread = new Dictionary<long, (long Tag, string Source, string Owner, string NextEnd)>();
        int pairs = 0;
        foreach (string line in eventLines)
        {
            if (line.Length > 4096)
                continue;
            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    continue;
                if (!document.RootElement.TryGetProperty("kind", out JsonElement kindProperty) || kindProperty.ValueKind != JsonValueKind.String)
                    continue;
                string? kind = kindProperty.GetString();
                if (string.Equals(kind, "error", StringComparison.Ordinal))
                    continue;
                bool isPre = string.Equals(kind, "pre", StringComparison.Ordinal);
                bool isPost = string.Equals(kind, "post", StringComparison.Ordinal);
                if (!isPre && !isPost)
                    continue;
                if (!document.RootElement.TryGetProperty("thread", out JsonElement threadProperty) || threadProperty.ValueKind != JsonValueKind.Number || !threadProperty.TryGetInt64(out long thread))
                    continue;
                if (!document.RootElement.TryGetProperty("tag", out JsonElement tagProperty) || tagProperty.ValueKind != JsonValueKind.Number || !tagProperty.TryGetInt64(out long tag))
                    continue;
                if (!TryGetJsonString(document.RootElement, "source", out string? source) || string.IsNullOrEmpty(source))
                    continue;
                if (!TryGetJsonString(document.RootElement, "owner", out string? owner) || string.IsNullOrEmpty(owner))
                    continue;
                if (isPre)
                {
                    if (!TryGetJsonString(document.RootElement, "nextEnd", out string? nextEnd) || string.IsNullOrEmpty(nextEnd))
                        continue;
                    pendingByThread[thread] = (tag, source!, owner!, nextEnd!);
                }
                else
                {
                    if (!pendingByThread.TryGetValue(thread, out var pending))
                        continue;
                    if (!TryGetJsonString(document.RootElement, "actualEnd", out string? actualEnd) || string.IsNullOrEmpty(actualEnd))
                        continue;
                    if (pending.Tag == tag && string.Equals(pending.Source, source, StringComparison.Ordinal) && string.Equals(pending.Owner, owner, StringComparison.Ordinal) && string.Equals(pending.NextEnd, actualEnd, StringComparison.Ordinal))
                    {
                        pairs++;
                        pendingByThread.Remove(thread);
                    }
                }
            }
        }

        return pairs;
    }

    /// <summary>Reads one required JSON string property without accepting numbers, nulls, or missing values.</summary>
    private static bool TryGetJsonString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString();
        return !string.IsNullOrEmpty(value);
    }

    /// <summary>Waits for the exact server to answer the console list command so the smoke test proves stdin reached the BDS child.</summary>
    private static async Task WaitForPlayerListAsync(BedrockServerHarness harness, int outputBeforeCommand, TimeSpan budget, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (harness.Output.Skip(outputBeforeCommand).Any(static line => line.Contains("players online:", StringComparison.Ordinal)))
                return;
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        Assert.Fail("The BDS child did not answer list through the debugger launcher within the smoke-test budget.");
    }

    /// <summary>Polls only the exact owned PID until the operating system reports it exited, never matching a server by process name.</summary>
    private static async Task WaitForExactPidExitAsync(int pid, TimeSpan budget, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using Process probe = Process.GetProcessById(pid);
                if (probe.HasExited)
                    return;
            }
            catch (ArgumentException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        Assert.Fail($"The owned PID {pid} was still present after {budget.TotalSeconds:F0} seconds; no other server was touched.");
    }
}
