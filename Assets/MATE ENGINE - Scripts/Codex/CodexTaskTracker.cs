using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
        public string AccumulatedThinking;
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
        const int PreviewChars = 240;
        const int MaxAccumulatedThinkingChars = 4096;
        const int MaxAccumulatedResponseChars = 4096;

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
                string raw = meta.SourceSender != null
                    ? meta.SourceSender + ": " + meta.InputPreview
                    : meta.InputPreview;
                string sanitized = Tail(SanitizeOneLine(raw), PreviewChars);
                AddActivityEntry(task, new ActivityEntry
                {
                    Type = "query",
                    Label = sanitized,
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

                // Add / update activity entry.
                // Special-case: user messages are already shown via metadata (query entry).
                // Don't add noisy placeholder items to the activity log.
                if (itemInfo.Type == "UserMessage")
                {
                    if (!string.IsNullOrEmpty(itemInfo.Label))
                    {
                        bool hasQuery = false;
                        for (int i = task.ActivityLog.Count - 1; i >= 0; i--)
                        {
                            if (task.ActivityLog[i].Type == "query")
                            {
                                hasQuery = true;
                                break;
                            }
                        }

                        if (!hasQuery)
                        {
                            AddActivityEntry(task, new ActivityEntry
                            {
                                Type = "query",
                                Label = itemInfo.Label,
                                Status = TaskItemStatus.Completed,
                                ItemId = itemInfo.Id
                            });
                        }
                    }
                }
                // Special-case: agent message items frequently start multiple times; avoid spamming placeholders.
                // Streaming deltas (HandleThinkingDelta/HandleStreamDelta) populate the activity log.
                else if (itemInfo.Type == "Message")
                {
                    // No activity entry here.
                }
                else
                {
                    string activityType = itemInfo.Type == "Thinking" ? "thinking" : "tool";
                    AddActivityEntry(task, new ActivityEntry
                    {
                        Type = activityType,
                        Label = itemInfo.Label,
                        Status = TaskItemStatus.Active,
                        ItemId = itemInfo.Id
                    });
                }

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

            // Keep a copy for UI (and cap to prevent unbounded growth).
            task.AccumulatedThinking = thinkingText;
            if (task.AccumulatedThinking != null && task.AccumulatedThinking.Length > MaxAccumulatedThinkingChars)
                task.AccumulatedThinking = task.AccumulatedThinking.Substring(task.AccumulatedThinking.Length - MaxAccumulatedThinkingChars);

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

            // Only update the preview when we have a complete sentence / line boundary.
            // This avoids showing users a constantly changing half-sentence.
            string stable = BuildStableThinkingPreviewLabel(task.AccumulatedThinking, PreviewChars);
            thinkEntry.Label = string.IsNullOrEmpty(stable) ? "Thinking..." : stable;

            FireActivityThrottled(task);
        }

        public void HandleReasoningDelta(string threadId, string turnId, string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            var task = EnsureTask(threadId, turnId);
            if (task == null) return;

            task.AccumulatedThinking = (task.AccumulatedThinking ?? "") + delta;
            if (task.AccumulatedThinking.Length > MaxAccumulatedThinkingChars)
                task.AccumulatedThinking = task.AccumulatedThinking.Substring(task.AccumulatedThinking.Length - MaxAccumulatedThinkingChars);

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

            thinkEntry.Status = TaskItemStatus.Active;
            string stable = BuildStableThinkingPreviewLabel(task.AccumulatedThinking, PreviewChars);
            thinkEntry.Label = string.IsNullOrEmpty(stable) ? "Thinking..." : stable;

            FireActivityThrottled(task);
        }

        public void HandleStreamDelta(string turnId, string accumulatedText)
        {
            if (!tasks.TryGetValue(turnId, out var task)) return;

            task.AccumulatedResponse = accumulatedText;
            if (task.AccumulatedResponse != null && task.AccumulatedResponse.Length > MaxAccumulatedResponseChars)
                task.AccumulatedResponse = task.AccumulatedResponse.Substring(task.AccumulatedResponse.Length - MaxAccumulatedResponseChars);

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

            responseEntry.Status = TaskItemStatus.Active;
            responseEntry.Label = Tail(SanitizeOneLine(task.AccumulatedResponse), PreviewChars);

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

                case "userMessage":
                    itemType = "UserMessage";
                    label = ExtractContentText(item);
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
                    label = "Agent message";
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
                    label = string.IsNullOrEmpty(type) ? "Working..." : type;
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

        static string Tail(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (maxChars <= 0) return "";
            if (text.Length <= maxChars) return text;
            return text.Substring(text.Length - maxChars);
        }

        static bool IsSentenceBoundaryAt(string text, int index)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (index < 0 || index >= text.Length) return false;

            char c = text[index];
            if (c == '\n' || c == '\r') return true;
            if (c == '。' || c == '！' || c == '？' || c == '；') return true;

            if (c != '.' && c != '!' && c != '?' && c != ';') return false;

            // Small heuristic: treat ASCII punctuation as sentence boundary only if it is followed by
            // whitespace / end-of-text (allowing closing quotes / brackets).
            int j = index + 1;
            while (j < text.Length)
            {
                char n = text[j];
                if (n == '"' || n == '\'' || n == ')' || n == ']' || n == '}' || n == '\u201D' || n == '\u2019')
                {
                    j++;
                    continue;
                }
                break;
            }

            if (j >= text.Length) return true;
            return char.IsWhiteSpace(text[j]);
        }

        static int FindLastSentenceBoundary(string text, int fromIndexInclusive)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            if (fromIndexInclusive >= text.Length) fromIndexInclusive = text.Length - 1;
            for (int i = fromIndexInclusive; i >= 0; i--)
            {
                if (IsSentenceBoundaryAt(text, i))
                    return i;
            }
            return -1;
        }

        static string BuildStableThinkingPreviewLabel(string rawThinking, int maxChars)
        {
            if (string.IsNullOrEmpty(rawThinking)) return "";
            if (maxChars <= 0) return "";

            int lastBoundary = FindLastSentenceBoundary(rawThinking, rawThinking.Length - 1);
            if (lastBoundary < 0) return "";

            // If the boundary is a newline, don't include it in the preview.
            int end = lastBoundary;
            while (end >= 0 && (rawThinking[end] == '\n' || rawThinking[end] == '\r'))
                end--;
            if (end < 0) return "";

            // Show only the last completed sentence / line segment.
            int prevBoundary = FindLastSentenceBoundary(rawThinking, end - 1);
            int start = prevBoundary >= 0 ? prevBoundary + 1 : 0;
            while (start <= end && (rawThinking[start] == '\n' || rawThinking[start] == '\r'))
                start++;
            if (start > end) return "";

            string segment = rawThinking.Substring(start, end - start + 1);
            string sanitized = SanitizeOneLine(segment);
            return Tail(sanitized, maxChars);
        }

        static string StripEmojiLikeChars(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // TMP fonts used by the Task Monitor are not guaranteed to have emoji glyphs. Since the Task Monitor
            // is a 1-line preview/debug UI, we aggressively strip emoji-like sequences to avoid "□" squares and
            // missing-glyph spam in logs.
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // Drop non-BMP code points (most emoji) represented as surrogate pairs in UTF-16.
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                        i++; // skip the pair
                    continue;
                }
                if (char.IsLowSurrogate(c))
                    continue;

                // Drop emoji joiners / variation selectors.
                if (c == '\uFE0E' || c == '\uFE0F' || c == '\u200D' || c == '\u20E3')
                    continue;

                // Drop many remaining BMP "symbol" emoji (dingbats, misc symbols, etc).
                // Keep letters/numbers/punctuation so CJK text remains intact.
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.OtherSymbol)
                    continue;

                sb.Append(c);
            }

            return sb.ToString();
        }

        static string SanitizeOneLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // Hide animation directives from the task monitor preview. They are useful for the avatar,
            // but look like gibberish in a 1-line UI.
            int animIdx = text.IndexOf("<!--anim:", StringComparison.Ordinal);
            if (animIdx >= 0)
                text = text.Substring(0, animIdx);

            text = StripEmojiLikeChars(text);

            // Collapse whitespace/newlines so it reads well in a 1-line TMP field.
            var sb = new StringBuilder(text.Length);
            bool inWs = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n' || c == '\t')
                    c = ' ';

                if (char.IsWhiteSpace(c))
                {
                    if (inWs) continue;
                    inWs = true;
                    sb.Append(' ');
                }
                else
                {
                    inWs = false;
                    sb.Append(c);
                }
            }

            return sb.ToString().Trim();
        }

        static string ExtractContentText(JToken item)
        {
            if (item == null) return "";

            var direct = item["text"]?.Value<string>() ?? "";
            if (!string.IsNullOrEmpty(direct))
                return direct;

            var content = item["content"] as JArray;
            if (content == null || content.Count == 0)
                return "";

            var sb = new StringBuilder();
            foreach (var part in content)
            {
                var t = part?["text"]?.Value<string>() ?? "";
                if (string.IsNullOrEmpty(t)) continue;
                sb.Append(t);
            }

            return sb.ToString();
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
