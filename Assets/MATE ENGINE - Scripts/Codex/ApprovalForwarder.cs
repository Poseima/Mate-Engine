using System;
using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MateEngine.Codex
{
    /// <summary>
    /// Manages IPC-based approval forwarding for WhatsApp human-in-the-loop flows.
    /// Writes approval requests to disk for NanoClaw to pick up as WhatsApp polls,
    /// and polls for responses written back by NanoClaw after the user votes.
    /// </summary>
    public static class ApprovalForwarder
    {
        static string requestsDir;
        static string responsesDir;

        static readonly ConcurrentDictionary<string, PendingApproval> pending = new();

        class PendingApproval
        {
            public string ApprovalId;
            public JToken CodexRequestId;
            public string ChatJid;
        }

        public static void Initialize()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string baseDir = Path.Combine(home, ".mate-engine", "ipc", "whatsapp", "approvals");
            requestsDir = Path.Combine(baseDir, "requests");
            responsesDir = Path.Combine(baseDir, "responses");

            Directory.CreateDirectory(requestsDir);
            Directory.CreateDirectory(responsesDir);

            // Clean stale files from previous sessions
            CleanupStaleFiles(requestsDir, TimeSpan.FromMinutes(10));
            CleanupStaleFiles(responsesDir, TimeSpan.FromMinutes(10));

            Debug.Log("[ApprovalForwarder] Initialized IPC dirs at " + baseDir);
        }

        /// <summary>
        /// Write an approval request to IPC for NanoClaw to pick up.
        /// </summary>
        public static void ForwardApproval(
            JToken codexRequestId, string method, JToken @params,
            string avatarId, string displayName, string groupFolder, string chatJid)
        {
            string approvalId = $"apr_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid().ToString("N").Substring(0, 6)}";

            string approvalType = method switch
            {
                "item/commandExecution/requestApproval" => "commandExecution",
                "item/fileChange/requestApproval" => "fileChange",
                _ => "unknown"
            };

            // Extract useful fields from params for display in WhatsApp poll
            string reason = @params?["reason"]?.Value<string>() ?? "";
            string command = @params?["command"]?.Value<string>()
                          ?? @params?["commandLine"]?.Value<string>() ?? "";
            string cwd = @params?["cwd"]?.Value<string>() ?? "";
            string grantRoot = @params?["grantRoot"]?.Value<string>() ?? "";

            var request = new JObject
            {
                ["id"] = approvalId,
                ["type"] = "approval_request",
                ["approvalType"] = approvalType,
                ["groupFolder"] = groupFolder ?? "",
                ["chatJid"] = chatJid ?? "",
                ["codexRequestId"] = codexRequestId,
                ["reason"] = reason,
                ["command"] = command,
                ["cwd"] = cwd,
                ["grantRoot"] = grantRoot,
                ["avatarDisplayName"] = displayName ?? "",
                ["timestamp"] = DateTime.UtcNow.ToString("o")
            };

            // Atomic write: tmp → rename
            string filename = $"{approvalId}.json";
            string filePath = Path.Combine(requestsDir, filename);
            string tempPath = filePath + ".tmp";

            File.WriteAllText(tempPath, request.ToString(Formatting.Indented));
            File.Move(tempPath, filePath);

            pending[approvalId] = new PendingApproval
            {
                ApprovalId = approvalId,
                CodexRequestId = codexRequestId,
                ChatJid = chatJid ?? ""
            };

            Debug.Log($"[ApprovalForwarder] Forwarded {approvalType} approval {approvalId} for codex request {codexRequestId}");
        }

        /// <summary>
        /// Scan responses dir for completed approvals and invoke callback.
        /// Called from main thread pump.
        /// </summary>
        public static void PollResponses(Action<JToken, string> onResponse)
        {
            if (responsesDir == null) return;

            string[] files;
            try { files = Directory.GetFiles(responsesDir, "*.json"); }
            catch { return; }

            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var obj = JObject.Parse(json);
                    string approvalId = obj["id"]?.Value<string>();
                    string decision = obj["decision"]?.Value<string>() ?? "decline";

                    if (approvalId != null && pending.TryRemove(approvalId, out var pa))
                    {
                        Debug.Log($"[ApprovalForwarder] Got response for {approvalId}: {decision}");
                        onResponse?.Invoke(pa.CodexRequestId, decision);
                    }

                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ApprovalForwarder] Error reading response: " + ex.Message);
                    try { File.Delete(file); } catch { }
                }
            }
        }

        /// <summary>
        /// Resolve pending approvals immediately with a caller-provided decision.
        /// Used to cancel blocked approval requests when a turn is interrupted.
        /// </summary>
        public static int ResolvePendingApprovals(Action<JToken, string> onResponse, string decision, string chatJid = null)
        {
            if (pending.IsEmpty) return 0;

            bool filterByChat = !string.IsNullOrEmpty(chatJid);
            int resolved = 0;

            foreach (var kv in pending)
            {
                var pa = kv.Value;
                if (filterByChat && !string.Equals(pa.ChatJid, chatJid, StringComparison.Ordinal))
                    continue;

                if (pending.TryRemove(kv.Key, out var removed))
                {
                    Debug.Log($"[ApprovalForwarder] Resolved pending approval {removed.ApprovalId} as {decision}");
                    onResponse?.Invoke(removed.CodexRequestId, decision);
                    TryDeleteRequestFile(removed.ApprovalId);
                    resolved++;
                }
            }

            return resolved;
        }

        public static int CancelPendingApprovals(Action<JToken, string> onResponse, string chatJid = null)
        {
            return ResolvePendingApprovals(onResponse, "cancel", chatJid);
        }

        static void TryDeleteRequestFile(string approvalId)
        {
            if (string.IsNullOrEmpty(requestsDir) || string.IsNullOrEmpty(approvalId)) return;
            try
            {
                string reqFile = Path.Combine(requestsDir, approvalId + ".json");
                if (File.Exists(reqFile)) File.Delete(reqFile);
            }
            catch { }
        }

        static void CleanupStaleFiles(string directory, TimeSpan maxAge)
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                var cutoff = DateTime.UtcNow - maxAge;
                foreach (var file in Directory.GetFiles(directory, "*.json"))
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
