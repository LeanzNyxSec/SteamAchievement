// SAM Enhanced modifications, 2026. Distributed under the included zlib license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SAM.Enhanced;

namespace SAM.Picker
{
    public sealed class BatchRunner
    {
        private readonly string session;
        private readonly int parentId;
        private readonly long parentTicks;
        public BatchRunner()
        {
            this.session = Path.Combine(Path.GetTempPath(), "SAM-Enhanced", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.session);
            using var parent = Process.GetCurrentProcess();
            this.parentId = parent.Id; this.parentTicks = parent.StartTime.ToUniversalTime().Ticks;
        }

        public async Task<List<BatchResult>> RunAsync(IEnumerable<BatchRequest> requests, bool apply, Action<BatchResult> completed, CancellationToken cancellation)
        {
            using var slots = new SemaphoreSlim(2);
            var jobs = requests.Select(async request =>
            {
                try { await slots.WaitAsync(cancellation); }
                catch (OperationCanceledException) { return Cancelled(request); }
                try
                {
                    if (cancellation.IsCancellationRequested) return Cancelled(request);
                    var result = await Task.Run(() => this.RunOne(request, apply, cancellation));
                    completed(result);
                    return result;
                }
                finally { slots.Release(); }
            }).ToArray();
            return (await Task.WhenAll(jobs)).ToList();
        }

        private BatchResult RunOne(BatchRequest request, bool apply, CancellationToken cancellation)
        {
            request.RequestId = Guid.NewGuid().ToString("N");
            request.ParentId = this.parentId; request.ParentStartTicks = this.parentTicks;
            string input = Path.Combine(this.session, request.RequestId + ".request.xml");
            request.ResultPath = Path.Combine(this.session, request.RequestId + ".result.xml");
            var failure = new BatchResult { RequestId = request.RequestId, Inspection = new GameInspection { GameId = request.GameId, Availability = Availability.Unavailable }, Outcome = apply ? SaveOutcome.Unconfirmed : SaveOutcome.Failed };
            Process process = null;
            try
            {
                Protocol.Write(input, request);
                process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SAM.Game.exe"))
                {
                    Arguments = (apply ? "--apply" : "--probe") + " \"" + input + "\"",
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                } };
                process.Start();
                var elapsed = Stopwatch.StartNew();
                while (!process.WaitForExit(100))
                {
                    // A cancelled write batch lets active saves finish and cancels only queued jobs.
                    if ((!apply && cancellation.IsCancellationRequested) || elapsed.Elapsed.TotalSeconds > 150)
                    {
                        process.Kill(); process.WaitForExit(3000);
                        failure.Outcome = !apply && cancellation.IsCancellationRequested ? SaveOutcome.Cancelled : apply ? SaveOutcome.Unconfirmed : SaveOutcome.Failed;
                        failure.Detail = apply ? "Worker timed out. Save status is unknown; Steam may retry staged changes." : cancellation.IsCancellationRequested ? "Check cancelled." : "Read-only check timed out. Editability is unknown.";
                        return failure;
                    }
                }
                if (!File.Exists(request.ResultPath)) { failure.Detail = "Worker exited without a result (exit " + process.ExitCode + ")."; return failure; }
                var result = Protocol.Read<BatchResult>(request.ResultPath);
                if (result.RequestId != request.RequestId || result.Inspection.GameId != request.GameId)
                { failure.Detail = "Mismatched worker response ignored."; return failure; }
                return result;
            }
            catch (Exception e) { failure.Detail = e.Message; return failure; }
            finally
            {
                if (process != null)
                {
                    try { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } } catch { }
                    process.Dispose();
                }
                // Delete only the specific per-job files, not an arbitrary folder tree.
                foreach (string file in new[] { input, input + ".tmp", request.ResultPath, request.ResultPath + ".tmp" })
                    try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
        }

        private static BatchResult Cancelled(BatchRequest request) => new() { Inspection = new GameInspection { GameId = request.GameId }, Outcome = SaveOutcome.Cancelled, Detail = "Queued game cancelled before it started." };
    }
}
