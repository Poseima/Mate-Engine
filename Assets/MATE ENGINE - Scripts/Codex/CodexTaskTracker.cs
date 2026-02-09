using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MateEngine.Codex
{
    public enum TaskStatus { Active, Completed, Cancelled }
    public enum TaskItemStatus { Active, Completed }

    public class TaskSourceMetadata
    {
        public string SourceType;   // "whatsapp" or "chat"
        public string SourceGroup;  // group folder name
        public string SourceSender; // sender display name
        public string InputPreview; // full input text (UI truncates at display time)
    }

    public class ActivityEntry
    {
        public string Type;      // "query", "thinking", "tool", "response"
        public string Label;
        public string Detail;    // duration, exit code
        public TaskItemStatus Status;
        public string ItemId;    // links to TaskItemInfo.Id
    }

    public class PlanStepInfo
    {
        public string Text;
        public string Status; // "pending", "inProgress", "completed"
    }

    public class TaskItemInfo
    {
        public string Id;
        public string Type;     // Command, FileChange, McpTool, WebSearch, Thinking, Message, Plan, Agent
        public string Label;
        public string Detail;
        public float Duration;  // seconds, -1 if unknown
        public TaskItemStatus Status;
        public float StartedAt;
    }

    public class TaskInfo
    {
        public string TurnId;
        public string ThreadId;
        public TaskStatus Status;
        public float StartedAt;
        public float CompletedAt;
        public TaskSourceMetadata Source;
        public string ThreadName;
        public readonly List<TaskItemInfo> Items = new();
        public readonly List<PlanStepInfo> PlanSteps = new();
        public readonly List<ActivityEntry> ActivityLog = new();
        public string PlanExplanation;
        public string AccumulatedResponse;
        public float LastActivityEventTime;
    }

    public class CodexTaskTracker
    {
        const int MaxRetainedTasks = 20;
        const int MaxItemsPerTask = 100;
        const int MaxActivityEntries = 50;
        const float StaleMetadataSeconds = 30f;
        const float ActivityThrottleSeconds = 0.2f;

        // Active + recently completed tasks keyed by turnId
        readonly Dictionary<string, TaskInfo> tasks = new();
        readonly List<string> taskOrder = new(); // insertion order for eviction

        // Thread names
        readonly Dictionary<string, string> threadNames = new();

        // Pending source metadata (FIFO queue, consumed by HandleTurnStarted)
        readonly Queue<(TaskSourceMetadata meta, float time)> pendingMetaQueue = new();

        // Events
        public event Action<TaskInfo> OnTaskCreated;
        public event Action<TaskInfo> OnTaskUpdated;
        public event Action<TaskInfo> OnTaskCompleted;
        public event Action<TaskInfo, TaskItemInfo> OnTaskItemStarted;
        public event Action<TaskInfo, TaskItemInfo> OnTaskItemCompleted;
        public event Action<TaskInfo> OnPlanUpdated;
        public event Action<TaskInfo> OnActivityUpdated;

        public IReadOnlyDictionary<string, TaskInfo> Tasks => tasks;

        TaskInfo EnsureTask(string threadId, string turnId)
        {
            if (string.IsNullOrEmpty(turnId))
                return null;

            if (tasks.TryGetValue(turnId, out var existing))
            {
                // turnId can be reused across different threads; only treat this as a match if threadId matches.
                if (string.IsNullOrEmpty(threadId) || existing.ThreadId == threadId)
                    return existing;
            }

            // Some protocol flows don't emit turn/started. Create on-demand so UI/activity still works.
            HandleTurnStarted(threadId, turnId);

            if (tasks.TryGetValue(turnId, out var created))
            {
                if (string.IsNullOrEmpty(threadId) || created.ThreadId == threadId)
                    return created;
            }

            return null;
        }

        public void RegisterSourceMetadata(string threadId, TaskSourceMetadata meta)
        {
            pendingMetaQueue.Enqueue((meta, Time.unscaledTime));
        }

        public void HandleTurnStarted(string threadId, string turnId)
        {
            if (string.IsNullOrEmpty(turnId))
                return;

            // The protocol may emit "turn start" signals multiple ways (turn/started, item/*, deltas).
            // Avoid clobbering an existing task for the same (threadId, turnId) (would lose items/activity already collected).
            // Note: turnId may be reused across different threads; allow overwrite in that case.
            if (tasks.TryGetValue(turnId, out var existing) && existing.ThreadId == threadId)
                return;

            // Pop first non-expired metadata from queue
            TaskSourceMetadata meta = null;
            while (pendingMetaQueue.Count > 0)
            {
                var (m, t) = pendingMetaQueue.Peek();
                if (Time.unscaledTime - t > StaleMetadataSeconds)
                {
                    pendingMetaQueue.Dequeue(); // expired, discard
                    continue;
                }
                var popped = pendingMetaQueue.Dequeue();
                meta = popped.meta;
                break;
            }

            var task = new TaskInfo
            {
                TurnId = turnId,
                ThreadId = threadId,
                Status = TaskStatus.Active,
                StartedAt = Time.unscaledTime,
                Source = meta,
            };

            if (threadNames.TryGetValue(threadId, out var name))
                task.ThreadName = name;

            // Add query activity entry from metadata
            if (meta != null && !string.IsNullOrEmpty(meta.InputPreview))
            {
                AddActivityEntry(task, new ActivityEntry
                {
                    Type = "query",
                    Label = meta.SourceSender != null
                        ? meta.SourceSender + ": " + meta.InputPreview
                        : meta.InputPreview,
                    Status = TaskItemStatus.Completed
                });
            }

            tasks[turnId] = task;
            taskOrder.Add(turnId);
            EvictOldTasks();

            try { OnTaskCreated?.Invoke(task); } catch (Exception e) { Debug.LogException(e); }
        }

        public void HandleTurnCompleted(string threadId, string turnId)
        {
            if (!tasks.TryGetValue(turnId, out var task))
                return;

            if (task.Status == TaskStatus.Cancelled)
                return; // already cancelled, don't overwrite

            task.Status = TaskStatus.Completed;
            task.CompletedAt = Time.unscaledTime;

            // Mark any still-active items as completed
            foreach (var item in task.Items)
            {
                if (item.Status == TaskItemStatus.Active)
                    item.Status = TaskItemStatus.Completed;
            }

            // Mark active activity entries as completed
            foreach (var entry in task.ActivityLog)
            {
                if (entry.Status == TaskItemStatus.Active)
                    entry.Status = TaskItemStatus.Completed;
            }

            try { OnTaskCompleted?.Invoke(task); } catch (Exception e) { Debug.LogException(e); }
        }

        public void HandleItemStarted(string threadId, string turnId, JToken item)
        {
            if (item == null) return;
            var task = EnsureTask(threadId, turnId);
            if (task == null) return;

            try
            {
                var itemInfo = ParseItem(item);
                itemInfo.Status = TaskItemStatus.Active;
                itemInfo.StartedAt = Time.unscaledTime;

                // Cap items
                if (task.Items.Count >= MaxItemsPerTask)
                    task.Items.RemoveAt(0);
                task.Items.Add(itemInfo);

                // Add activity entry
                string activityType = itemInfo.Type == "Thinking" ? "thinking" : "tool";
                AddActivityEntry(task, new ActivityEntry
                {
                    Type = activityType,
                    Label = itemInfo.Label,
                    Status = TaskItemStatus.Active,
                    ItemId = itemInfo.Id
                });

                try { OnTaskItemStarted?.Invoke(task, itemInfo); } catch (Exception e) { Debug.LogException(e); }
                try { OnTaskUpdated?.Invoke(task); } catch (Exception e) { Debug.LogException(e); }
                FireActivityThrottled(task);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CodexTaskTracker] Failed to parse item/started: " + e.Message);
            }
        }

        public void HandleItemCompleted(string threadId, string turnId, JToken item)
        {
            if (item == null) return;
            var task = EnsureTask(threadId, turnId);
            if (task == null) return;

            try
            {
                var id = item["id"]?.Value<string>() ?? "";
                TaskItemInfo existing = null;

                if (!string.IsNullOrEmpty(id))
                {
                    for (int i = task.Items.Count - 1; i >= 0; i--)
                    {
                        if (task.Items[i].Id == id)
                        {
                            existing = task.Items[i];
                            break;
                        }
                    }
                }

                if (existing != null)
                {
                    existing.Status = TaskItemStatus.Completed;
                    var durationMs = item["durationMs"]?.Value<float>() ?? item["duration_ms"]?.Value<float>() ?? -1f;
                    if (durationMs > 0) existing.Duration = durationMs / 1000f;
                    else existing.Duration = Time.unscaledTime - existing.StartedAt;

                    // Update detail from completion data
                    EnrichCompletedItem(existing, item);

                    // Update matching activity entry
                    UpdateActivityEntryForCompletion(task, existing);

                    try { OnTaskItemCompleted?.Invoke(task, existing); } catch (Exception e) { Debug.LogException(e); }
                }
                else
                {
                    // Defensive: create item we never saw started
                    var itemInfo = ParseItem(item);
                    itemInfo.Status = TaskItemStatus.Completed;
                    var durationMs = item["durationMs"]?.Value<float>() ?? item["duration_ms"]?.Value<float>() ?? -1f;
                    if (durationMs > 0) itemInfo.Duration = durationMs / 1000f;

                    if (task.Items.Count >= MaxItemsPerTask)
                        task.Items.RemoveAt(0);
                    task.Items.Add(itemInfo);

                    try { OnTaskItemCompleted?.Invoke(task, itemInfo); } catch (Exception e) { Debug.LogException(e); }
                }

                try { OnTaskUpdated?.Invoke(task); } catch (Exception e) { Debug.LogException(e); }
                FireActivityThrottled(task);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CodexTaskTracker] Failed to parse item/completed: " + e.Message);
            }
        }

        public void HandleThinkingDelta(string turnId, string thinkingText)
        {
            if (string.IsNullOrEmpty(thinkingText)) return;
            if (!tasks.TryGetValue(turnId, out var task)) return;

            // Find or create a single "thinking" activity entry
            ActivityEntry thinkEntry = null;
            for (int i = task.ActivityLog.Count - 1; i >= 0; i--)
            {
                if (task.ActivityLog[i].Type == "thinking")
                {
                    thinkEntry = task.ActivityLog[i];
                    break;
                }
            }

            if (thinkEntry == null)
            {
                thinkEntry = new ActivityEntry
                {
                    Type = "thinking",
                    Label = "",
                    Status = TaskItemStatus.Active
                };
                AddActivityEntry(task, thinkEntry);
            }

            // Show last ~120 chars of thinking
            if (thinkingText.Length > 120)
                thinkEntry.Label = thinkingText.Substring(thinkingText.Length - 120);
            else
                thinkEntry.Label = thinkingText;

            FireActivityThrottled(task);
        }

        public void HandleStreamDelta(string turnId, string accumulatedText)
        {
            if (!tasks.TryGetValue(turnId, out var task)) return;

            task.AccumulatedResponse = accumulatedText;

            // Find or create a single "response" activity entry
            ActivityEntry responseEntry = null;
            for (int i = task.ActivityLog.Count - 1; i >= 0; i--)
            {
                if (task.ActivityLog[i].Type == "response")
                {
                    responseEntry = task.ActivityLog[i];
                    break;
                }
            }

            if (responseEntry == null)
            {
                responseEntry = new ActivityEntry
                {
                    Type = "response",
                    Label = "",
                    Status = TaskItemStatus.Active
                };
                AddActivityEntry(task, responseEntry);
            }

            // Show last ~120 chars
            if (accumulatedText.Length > 120)
                responseEntry.Label = accumulatedText.Substring(accumulatedText.Length - 120);
            else
                responseEntry.Label = accumulatedText;

            FireActivityThrottled(task);
        }

        public void CancelTask(string turnId)
        {
            if (!tasks.TryGetValue(turnId, out var task)) return;
            if (task.Status != TaskStatus.Active) return;

            task.Status = TaskStatus.Cancelled;
            task.CompletedAt = Time.unscaledTime;

            // Mark active items as completed
            foreach (var item in task.Items)
            {
                if (item.Status == TaskItemStatus.Active)
                    item.Status = TaskItemStatus.Completed;
            }

            // Mark active activity entries as completed
            foreach (var entry in task.ActivityLog)
            {
                if (entry.Status == TaskItemStatus.Active)
                    entry.Status = TaskItemStatus.Completed;
            }

            // Add cancelled activity entry
            AddActivityEntry(task, new ActivityEntry
            {
                Type = "tool",
                Label = "Cancelled",
                Status = TaskItemStatus.Completed
            });

            try { OnTaskCompleted?.Invoke(task); } catch (Exception e) { Debug.LogException(e); }
        }

        public void HandlePlanUpdated(string threadId, string turnId, string explanation, JArray planSteps)
        {
            var task = EnsureTask(threadId, turnId);
            if (task == null) return;

            try
            {
                task.PlanExplanation = explanation;
                task.PlanSteps.Clear();

                if (planSteps != null)
                {
                    foreach (var step in planSteps)
                    {
                        task.PlanSteps.Add(new PlanStepInfo
                        {
                            Text = step["text"]?.Value<string>() ?? step["step"]?.Value<string>() ?? step.Value<string>() ?? "",
                            Status = step["status"]?.Value<string>() ?? "pending"
                        });
                    }
                }

                try { OnPlanUpdated?.Invoke(task); } catch (Exception e) { Debug.LogException(e); }
                try { OnTaskUpdated?.Invoke(task); } catch (Exception e) { Debug.LogException(e); }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CodexTaskTracker] Failed to parse plan update: " + e.Message);
            }
        }

        public void HandleThreadNameUpdated(string threadId, string name)
        {
            threadNames[threadId] = name;

            // Update any active tasks on this thread
            foreach (var task in tasks.Values)
            {
                if (task.ThreadId == threadId)
                    task.ThreadName = name;
            }
        }

        void AddActivityEntry(TaskInfo task, ActivityEntry entry)
        {
            if (task.ActivityLog.Count >= MaxActivityEntries)
                task.ActivityLog.RemoveAt(0);
            task.ActivityLog.Add(entry);
        }

        void UpdateActivityEntryForCompletion(TaskInfo task, TaskItemInfo item)
        {
            if (string.IsNullOrEmpty(item.Id)) return;

            for (int i = task.ActivityLog.Count - 1; i >= 0; i--)
            {
                var entry = task.ActivityLog[i];
                if (entry.ItemId == item.Id)
                {
                    entry.Status = TaskItemStatus.Completed;
                    if (item.Duration > 0)
                        entry.Detail = FormatDuration(item.Duration);
                    if (!string.IsNullOrEmpty(item.Detail))
                        entry.Detail = (entry.Detail != null ? entry.Detail + " " : "") + item.Detail;
                    break;
                }
            }
        }

        void FireActivityThrottled(TaskInfo task)
        {
            if (Time.unscaledTime - task.LastActivityEventTime > ActivityThrottleSeconds)
            {
                task.LastActivityEventTime = Time.unscaledTime;
                try { OnActivityUpdated?.Invoke(task); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        static string FormatDuration(float seconds)
        {
            if (seconds < 0) return "";
            if (seconds < 60) return Mathf.FloorToInt(seconds) + "s";
            int m = Mathf.FloorToInt(seconds / 60);
            int s = Mathf.FloorToInt(seconds % 60);
            return m + "m" + (s > 0 ? s + "s" : "");
        }

        TaskInfo FindTask(string turnId, string threadId)
        {
            // Try direct lookup by turnId first
            if (!string.IsNullOrEmpty(turnId) && tasks.TryGetValue(turnId, out var task))
                return task;

            // Fallback: find most recent active task on this thread
            if (!string.IsNullOrEmpty(threadId))
            {
                for (int i = taskOrder.Count - 1; i >= 0; i--)
                {
                    if (tasks.TryGetValue(taskOrder[i], out var t) && t.ThreadId == threadId && t.Status == TaskStatus.Active)
                        return t;
                }
            }

            return null;
        }

        TaskItemInfo ParseItem(JToken item)
        {
            var id = item["id"]?.Value<string>() ?? "";
            var type = item["type"]?.Value<string>() ?? "";

            string itemType;
            string label;

            switch (type)
            {
                case "commandExecution":
                    itemType = "Command";
                    var cmd = item["command"]?.Value<string>() ?? "";
                    label = "bash: " + (cmd.Length > 60 ? cmd.Substring(0, 60) + "..." : cmd);
                    break;

                case "mcpToolCall":
                    itemType = "McpTool";
                    var server = item["serverName"]?.Value<string>() ?? item["server"]?.Value<string>() ?? "";
                    var tool = item["toolName"]?.Value<string>() ?? item["tool"]?.Value<string>() ?? "";
                    label = "mcp: " + (string.IsNullOrEmpty(server) ? tool : server + "/" + tool);
                    break;

                case "fileChange":
                    itemType = "FileChange";
                    var changes = item["changes"] as JArray;
                    var path = changes?[0]?["path"]?.Value<string>() ?? item["path"]?.Value<string>() ?? "";
                    label = "file: " + path;
                    break;

                case "webSearch":
                    itemType = "WebSearch";
                    var query = item["query"]?.Value<string>() ?? "";
                    label = "search: " + query;
                    break;

                case "reasoning":
                    itemType = "Thinking";
                    label = "Thinking...";
                    break;

                case "agentMessage":
                    itemType = "Message";
                    label = "Responding...";
                    break;

                case "plan":
                    itemType = "Plan";
                    label = "Planning...";
                    break;

                case "collabAgentToolCall":
                    itemType = "Agent";
                    var agentTool = item["toolName"]?.Value<string>() ?? item["tool"]?.Value<string>() ?? "";
                    label = "Agent: " + agentTool;
                    break;

                default:
                    itemType = type;
                    label = "Working...";
                    break;
            }

            return new TaskItemInfo
            {
                Id = id,
                Type = itemType,
                Label = label,
                Duration = -1f
            };
        }

        void EnrichCompletedItem(TaskItemInfo info, JToken item)
        {
            var type = item["type"]?.Value<string>() ?? "";
            switch (type)
            {
                case "commandExecution":
                    var exitCode = item["exitCode"]?.Value<int>() ?? item["exit_code"]?.Value<int>();
                    if (exitCode.HasValue)
                        info.Detail = "exit " + exitCode.Value;
                    break;

                case "fileChange":
                    var status = item["status"]?.Value<string>() ?? "";
                    if (!string.IsNullOrEmpty(status))
                        info.Detail = status;
                    break;
            }
        }

        void EvictOldTasks()
        {
            while (taskOrder.Count > MaxRetainedTasks)
            {
                var oldest = taskOrder[0];
                taskOrder.RemoveAt(0);
                tasks.Remove(oldest);
            }
        }
    }
}
