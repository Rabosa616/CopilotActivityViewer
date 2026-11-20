using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CopilotActivityViewer.Model
{
    /// <summary>
    /// Builds the agent tree and activity timeline from Copilot agent session events (events.jsonl).
    /// The event format is internal to Copilot and may change, so every field is read defensively.
    /// </summary>
    public sealed class SessionModel : ObservableObject
    {
        internal const int MaxDetailLength = 12000;

        private static readonly Regex TagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        private readonly Dictionary<string, AgentNode> agents = new Dictionary<string, AgentNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActivityItem> toolItems = new Dictionary<string, ActivityItem>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> toolOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<ActivityItem> activeItems = new HashSet<ActivityItem>();

        private string summary = string.Empty;
        private string cwd;
        private string currentModel;
        private DateTimeOffset? lastEventTime;

        public SessionModel()
        {
            MainAgent = new AgentNode(string.Empty, null)
            {
                Name = "main",
                DisplayName = "Main agent",
                AgentType = "main",
                Status = AgentStatus.Idle,
            };
            agents[string.Empty] = MainAgent;
            RootAgents.Add(MainAgent);
        }

        public AgentNode MainAgent { get; }

        public ObservableCollection<AgentNode> RootAgents { get; } = new ObservableCollection<AgentNode>();

        public ObservableCollection<ActivityItem> Activities { get; } = new ObservableCollection<ActivityItem>();

        public IEnumerable<AgentNode> Agents => agents.Values;

        public string SessionId { get; private set; }

        public int EventCount { get; private set; }

        public long InputTokens { get; private set; }

        public long OutputTokens { get; private set; }

        public string Cwd
        {
            get => cwd;
            private set => Set(ref cwd, value);
        }

        public string CurrentModel
        {
            get => currentModel;
            private set => Set(ref currentModel, value);
        }

        public DateTimeOffset? LastEventTime
        {
            get => lastEventTime;
            private set => Set(ref lastEventTime, value);
        }

        public string Summary
        {
            get => summary;
            private set => Set(ref summary, value);
        }

        public AgentNode FindAgent(string agentId) =>
            agents.TryGetValue(agentId ?? string.Empty, out var agent) ? agent : null;

        public static JObject ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            try
            {
                using (var reader = new JsonTextReader(new StringReader(line)) { DateParseHandling = DateParseHandling.None })
                {
                    return JObject.Load(reader);
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public void ApplyLine(string line)
        {
            var e = ParseLine(line);
            if (e != null)
            {
                Apply(e);
            }
        }

        public void Apply(JObject e)
        {
            if (e == null)
            {
                return;
            }

            EventCount++;
            var type = Str(e, "type") ?? string.Empty;
            var data = e["data"] as JObject ?? new JObject();
            var agentId = Str(e, "agentId") ?? string.Empty;
            var timestamp = ParseTime(Str(e, "timestamp")) ?? LastEventTime ?? DateTimeOffset.UtcNow;
            LastEventTime = timestamp;

            switch (type)
            {
                case "session.start":
                    SessionId = Str(data, "sessionId");
                    Cwd = Str(data, "context.cwd") ?? Cwd;
                    SetMainModel(Str(data, "selectedModel"));
                    MainAgent.StartTime = ParseTime(Str(data, "startTime")) ?? timestamp;
                    break;

                case "session.resume":
                    Cwd = Str(data, "context.cwd") ?? Cwd;
                    SetMainModel(Str(data, "selectedModel"));
                    AddActivity(timestamp, MainAgent, ActivityKind.Info, "Session resumed", null, null);
                    break;

                case "session.model_change":
                    SetMainModel(Str(data, "newModel"));
                    break;

                case "user.message":
                    OnUserMessage(timestamp, GetAgent(agentId), data);
                    break;

                case "assistant.turn_start":
                    OnTurnStart(GetAgent(agentId));
                    break;

                case "assistant.message":
                    OnAssistantMessage(timestamp, GetAgent(agentId), data);
                    break;

                case "assistant.turn_end":
                    OnTurnEnd(GetAgent(agentId));
                    break;

                case "tool.execution_start":
                    OnToolStart(timestamp, GetAgent(agentId), data);
                    break;

                case "permission.requested":
                    OnPermissionRequested(data);
                    break;

                case "permission.completed":
                    OnPermissionCompleted(data);
                    break;

                case "tool.execution_complete":
                    OnToolComplete(timestamp, data);
                    break;

                case "subagent.started":
                    OnSubagentStarted(timestamp, agentId, data);
                    break;

                case "subagent.completed":
                    OnSubagentFinished(timestamp, agentId, data, failed: false);
                    break;

                case "subagent.failed":
                    OnSubagentFinished(timestamp, agentId, data, failed: true);
                    break;

                case "abort":
                    OnAbort(timestamp, GetAgent(agentId), data);
                    break;

                case "session.task_complete":
                    {
                        var text = Str(data, "summary");
                        AddActivity(timestamp, MainAgent, ActivityKind.Completion, "Task complete", TextFormat.FirstLine(text, 240), text);
                        MainAgent.Status = AgentStatus.Completed;
                        MainAgent.CurrentActivity = null;
                        break;
                    }

                case "session.error":
                    {
                        var agent = GetAgent(agentId);
                        var message = Str(data, "message");
                        AddActivity(timestamp, agent, ActivityKind.Error, "Error" + Suffix(Str(data, "errorType")), TextFormat.FirstLine(message, 240), message);
                        agent.Status = AgentStatus.Failed;
                        break;
                    }

                case "session.warning":
                    {
                        var message = Str(data, "message");
                        AddActivity(timestamp, GetAgent(agentId), ActivityKind.Warning, "Warning" + Suffix(Str(data, "warningType")), TextFormat.FirstLine(message, 240), message);
                        break;
                    }

                case "system.notification":
                    {
                        var content = TagRegex.Replace(Str(data, "content") ?? string.Empty, string.Empty).Trim();
                        AddActivity(timestamp, GetAgent(agentId), ActivityKind.Notification, "Notification", TextFormat.FirstLine(content, 240), content);
                        break;
                    }

                case "skill.invoked":
                    AddActivity(timestamp, GetAgent(agentId), ActivityKind.Skill, "Skill: " + Str(data, "name"), Str(data, "path"), null);
                    break;

                case "session.compaction_start":
                    AddActivity(timestamp, GetAgent(agentId), ActivityKind.Info, "Compacting context…", null, null);
                    break;

                case "session.compaction_complete":
                    {
                        var removed = Str(data, "messagesRemoved");
                        AddActivity(timestamp, GetAgent(agentId), ActivityKind.Info, "Context compacted", removed != null ? removed + " messages summarized" : null, Str(data, "summaryContent"));
                        break;
                    }

                case "session.usage_record":
                    OnUsage(GetAgent(agentId), data);
                    break;

                case "session.shutdown":
                    CancelActiveItems(null);
                    foreach (var agent in agents.Values.Where(a => a.Status == AgentStatus.Running))
                    {
                        agent.Status = AgentStatus.Idle;
                        agent.CurrentActivity = null;
                    }

                    AddActivity(timestamp, MainAgent, ActivityKind.Info, "Session closed", null, null);
                    break;
            }
        }

        /// <summary>Recomputes the aggregated summary line. Call once per batch of applied events.</summary>
        public void UpdateSummary()
        {
            var subagents = agents.Values.Where(a => !a.IsMain).ToList();
            var running = activeItems.Count;
            var sb = new StringBuilder();
            sb.Append(MainAgent.Status);
            sb.Append(" · ").Append(MainAgent.ToolCalls + subagents.Sum(a => a.ToolCalls)).Append(" tool calls");
            if (running > 0)
            {
                sb.Append(" (").Append(running).Append(" running)");
            }

            sb.Append(" · ").Append(subagents.Count).Append(subagents.Count == 1 ? " subagent" : " subagents");
            var runningAgents = subagents.Count(a => a.IsRunning);
            if (runningAgents > 0)
            {
                sb.Append(" (").Append(runningAgents).Append(" running)");
            }

            if (InputTokens + OutputTokens > 0)
            {
                sb.Append(" · ").Append(TextFormat.Tokens(InputTokens)).Append(" in / ").Append(TextFormat.Tokens(OutputTokens)).Append(" out");
            }

            if (!string.IsNullOrEmpty(CurrentModel))
            {
                sb.Append(" · ").Append(CurrentModel);
            }

            if (LastEventTime.HasValue)
            {
                sb.Append(" · last event ").Append(LastEventTime.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            }

            Summary = sb.ToString();
        }

        /// <summary>Refreshes time-dependent texts (elapsed durations) of running items.</summary>
        public void Tick()
        {
            foreach (var item in activeItems)
            {
                item.RaiseTimeDependent();
            }

            foreach (var agent in agents.Values)
            {
                if (agent.IsRunning)
                {
                    agent.RaiseTimeDependent();
                }
            }
        }

        private void OnUserMessage(DateTimeOffset timestamp, AgentNode agent, JObject data)
        {
            var content = Str(data, "content");
            AddActivity(timestamp, agent, ActivityKind.User, agent.IsMain ? "User" : "Prompt", TextFormat.FirstLine(content, 240), content);
            agent.Status = AgentStatus.Running;
            if (agent.IsMain)
            {
                agent.CurrentActivity = "Thinking…";
            }
        }

        private static void OnTurnStart(AgentNode agent)
        {
            agent.Status = AgentStatus.Running;
            agent.EndTime = null;
            agent.LastMessageHadToolRequests = false;
            if (agent.RunningTools == 0)
            {
                agent.CurrentActivity = "Thinking…";
            }
        }

        private void OnAssistantMessage(DateTimeOffset timestamp, AgentNode agent, JObject data)
        {
            var requests = data["toolRequests"] as JArray;
            agent.LastMessageHadToolRequests = requests != null && requests.Count > 0;
            var content = Str(data, "content");
            if (!string.IsNullOrWhiteSpace(content))
            {
                AddActivity(timestamp, agent, ActivityKind.Assistant, "Message", TextFormat.FirstLine(content, 240), content);
            }
        }

        private static void OnTurnEnd(AgentNode agent)
        {
            if (!agent.LastMessageHadToolRequests && agent.RunningTools == 0 && agent.Status == AgentStatus.Running)
            {
                agent.Status = AgentStatus.Idle;
                agent.CurrentActivity = null;
            }
        }

        private void OnToolStart(DateTimeOffset timestamp, AgentNode agent, JObject data)
        {
            var toolCallId = Str(data, "toolCallId");
            var toolName = Str(data, "toolName") ?? "tool";
            var args = data["arguments"] as JObject;
            var title = Str(data, "toolTitle");
            if (string.IsNullOrEmpty(title))
            {
                title = toolName;
            }

            var item = new ActivityItem(timestamp, agent, ActivityKind.Tool)
            {
                ToolCallId = toolCallId,
                ToolName = toolName,
                Arguments = args,
                Title = title,
                Subtitle = SummarizeArguments(toolName, args),
                Details = "Tool: " + toolName + Environment.NewLine + FormatArguments(args),
                Status = ActivityStatus.Running,
            };

            Activities.Add(item);
            activeItems.Add(item);
            if (!string.IsNullOrEmpty(toolCallId))
            {
                toolItems[toolCallId] = item;
                toolOwners[toolCallId] = agent.Id;
            }

            agent.ToolCalls++;
            agent.RunningTools++;
            agent.Status = AgentStatus.Running;
            agent.CurrentActivity = string.IsNullOrEmpty(item.Subtitle) ? title : title + ": " + item.Subtitle;
        }

        private void OnPermissionRequested(JObject data)
        {
            var toolCallId = Str(data, "permissionRequest.toolCallId") ?? Str(data, "promptRequest.toolCallId");
            if (toolCallId != null && toolItems.TryGetValue(toolCallId, out var item) && item.Status == ActivityStatus.Running)
            {
                item.Status = ActivityStatus.AwaitingApproval;
            }
        }

        private void OnPermissionCompleted(JObject data)
        {
            var toolCallId = Str(data, "toolCallId");
            if (toolCallId == null || !toolItems.TryGetValue(toolCallId, out var item) || item.Status != ActivityStatus.AwaitingApproval)
            {
                return;
            }

            var kind = Str(data, "result.kind") ?? string.Empty;
            if (kind.StartsWith("approved", StringComparison.OrdinalIgnoreCase))
            {
                item.Status = ActivityStatus.Running;
            }
            else
            {
                item.Note = "Permission: " + kind;
                item.Details += Environment.NewLine + Environment.NewLine + item.Note;
            }
        }

        private void OnToolComplete(DateTimeOffset timestamp, JObject data)
        {
            var toolCallId = Str(data, "toolCallId");
            if (toolCallId == null || !toolItems.TryGetValue(toolCallId, out var item))
            {
                return;
            }

            var success = data.SelectToken("success")?.Type == JTokenType.Boolean ? (bool)data["success"] : true;
            var resultText = Str(data, "result.content") ?? Str(data, "error.message") ?? Str(data, "error");
            if (!string.IsNullOrEmpty(resultText))
            {
                item.ResultText = TextFormat.Truncate(resultText, MaxDetailLength);
                item.ResultFailed = !success;
                item.Details = TextFormat.Truncate(
                    item.Details + Environment.NewLine + Environment.NewLine + (success ? "Result:" : "Error:") + Environment.NewLine + resultText,
                    MaxDetailLength);
            }

            CompleteItem(item, timestamp, success ? ActivityStatus.Succeeded : ActivityStatus.Failed);
        }

        private void OnSubagentStarted(DateTimeOffset timestamp, string agentId, JObject data)
        {
            if (string.IsNullOrEmpty(agentId))
            {
                return;
            }

            var toolCallId = Str(data, "toolCallId");
            var parentId = toolCallId != null && toolOwners.TryGetValue(toolCallId, out var owner) ? owner : string.Empty;
            var parent = FindAgent(parentId) ?? MainAgent;
            var node = GetAgent(agentId);
            if (!ReferenceEquals(node.Parent, parent) && !node.IsSelfOrAncestorOf(parent))
            {
                node.Parent?.Children.Remove(node);
                node.Parent = parent;
                parent.Children.Add(node);
            }

            node.Name = Str(data, "agentName") ?? node.Name;
            node.DisplayName = Str(data, "agentDisplayName") ?? node.DisplayName;
            node.Description = Str(data, "agentDescription") ?? node.Description;
            node.AgentType = Str(data, "agentType") ?? Str(data, "agentName") ?? node.AgentType;
            node.Model = Str(data, "model") ?? node.Model;
            node.ExecutionMode = Str(data, "executionMode") ?? node.ExecutionMode;
            node.StartTime = timestamp;
            node.EndTime = null;
            node.Status = AgentStatus.Running;
            node.CurrentActivity = "Starting…";

            var details = new StringBuilder()
                .AppendLine("Agent: " + node.Label)
                .AppendLine("Type: " + node.AgentType)
                .AppendLine("Model: " + node.Model)
                .AppendLine("Mode: " + node.ExecutionMode)
                .AppendLine("Description: " + node.Description)
                .AppendLine("Agent id: " + node.Id);
            ActivityItem taskItem = null;
            if (toolCallId != null && toolItems.TryGetValue(toolCallId, out taskItem))
            {
                details.AppendLine().Append(taskItem.Details);
            }

            var started = AddActivity(timestamp, parent, ActivityKind.Subagent, "Started " + node.Label, Join(node.AgentType, node.Description), TextFormat.Truncate(details.ToString(), MaxDetailLength));
            started.Body = null;
            started.ToolName = taskItem?.ToolName;
            started.Arguments = taskItem?.Arguments;
            started.Properties = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Agent", node.Label),
                new KeyValuePair<string, string>("Type", node.AgentType),
                new KeyValuePair<string, string>("Model", node.Model),
                new KeyValuePair<string, string>("Mode", node.ExecutionMode),
                new KeyValuePair<string, string>("Description", node.Description),
                new KeyValuePair<string, string>("Agent id", node.Id),
            };
        }

        private void OnSubagentFinished(DateTimeOffset timestamp, string agentId, JObject data, bool failed)
        {
            if (string.IsNullOrEmpty(agentId))
            {
                return;
            }

            var node = GetAgent(agentId);
            node.Status = failed ? AgentStatus.Failed : AgentStatus.Completed;
            node.EndTime = timestamp;
            node.CurrentActivity = null;
            if (long.TryParse(Str(data, "totalTokens"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokens))
            {
                node.ReportedTotalTokens = tokens;
            }

            CancelActiveItems(node);
            var error = Str(data, "error.message") ?? Str(data, "error");
            AddActivity(
                timestamp,
                node.Parent ?? MainAgent,
                failed ? ActivityKind.Error : ActivityKind.Subagent,
                (failed ? "Failed " : "Completed ") + node.Label,
                failed ? TextFormat.FirstLine(error, 240) : node.Stats,
                error);
        }

        private void OnAbort(DateTimeOffset timestamp, AgentNode agent, JObject data)
        {
            agent.Status = AgentStatus.Aborted;
            agent.CurrentActivity = null;
            if (!agent.IsMain)
            {
                agent.EndTime = timestamp;
            }

            CancelActiveItems(agent);
            AddActivity(timestamp, agent, ActivityKind.Warning, "Aborted", Str(data, "reason"), null);
        }

        private void OnUsage(AgentNode agent, JObject data)
        {
            var input = Long(data, "usage.inputTokens");
            var output = Long(data, "usage.outputTokens");
            agent.InputTokens += input;
            agent.OutputTokens += output;
            InputTokens += input;
            OutputTokens += output;
            var model = Str(data, "usage.model");
            if (!string.IsNullOrEmpty(model) && string.IsNullOrEmpty(agent.Model))
            {
                agent.Model = model;
            }
        }

        private void CancelActiveItems(AgentNode agent)
        {
            foreach (var item in activeItems.Where(i => agent == null || ReferenceEquals(i.Agent, agent)).ToList())
            {
                CompleteItem(item, LastEventTime ?? item.Timestamp, ActivityStatus.Cancelled);
            }
        }

        private void CompleteItem(ActivityItem item, DateTimeOffset timestamp, ActivityStatus status)
        {
            var wasActive = activeItems.Remove(item);
            item.Duration = timestamp - item.Timestamp;
            item.Status = status;
            var agent = item.Agent;
            if (agent != null && wasActive)
            {
                agent.RunningTools--;
                if (agent.RunningTools == 0 && agent.IsRunning)
                {
                    agent.CurrentActivity = "Thinking…";
                }
            }
        }

        private ActivityItem AddActivity(DateTimeOffset timestamp, AgentNode agent, ActivityKind kind, string title, string subtitle, string details)
        {
            var item = new ActivityItem(timestamp, agent, kind)
            {
                Title = title,
                Subtitle = subtitle,
                Body = string.IsNullOrEmpty(details) ? null : TextFormat.Truncate(details, MaxDetailLength),
                Details = string.IsNullOrEmpty(details) ? Join(title, subtitle) : TextFormat.Truncate(details, MaxDetailLength),
            };
            Activities.Add(item);
            return item;
        }

        private AgentNode GetAgent(string agentId)
        {
            agentId = agentId ?? string.Empty;
            if (agents.TryGetValue(agentId, out var agent))
            {
                return agent;
            }

            agent = new AgentNode(agentId, MainAgent) { Status = AgentStatus.Running };
            agents[agentId] = agent;
            MainAgent.Children.Add(agent);
            return agent;
        }

        private void SetMainModel(string model)
        {
            if (!string.IsNullOrEmpty(model))
            {
                CurrentModel = model;
                MainAgent.Model = model;
            }
        }

        internal static string SummarizeArguments(string toolName, JObject args)
        {
            if (args == null)
            {
                return string.Empty;
            }

            if (toolName == "task")
            {
                return Join(Join(Str(args, "name"), Str(args, "agent_type") is string t ? "(" + t + ")" : null, " "), Str(args, "description"));
            }

            foreach (var key in new[] { "description", "intent", "explanation", "summary" })
            {
                var value = Str(args, key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return TextFormat.FirstLine(value, 200);
                }
            }

            foreach (var key in new[] { "command", "path", "filePath", "filename", "pattern", "query", "url", "skill", "agent_id", "shellId", "message", "prompt", "name" })
            {
                var value = Str(args, key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return TextFormat.FirstLine(value, 200);
                }
            }

            var first = args.Properties().FirstOrDefault(p => p.Value.Type == JTokenType.String);
            return first != null ? TextFormat.FirstLine(first.Name + ": " + (string)first.Value, 200) : string.Empty;
        }

        internal static string FormatArguments(JObject args)
        {
            if (args == null || !args.HasValues)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var property in args.Properties())
            {
                if (property.Value.Type == JTokenType.String)
                {
                    var value = (string)property.Value;
                    if (value.IndexOf('\n') >= 0)
                    {
                        sb.Append(property.Name).AppendLine(":").AppendLine(value.TrimEnd());
                    }
                    else
                    {
                        sb.Append(property.Name).Append(": ").AppendLine(value);
                    }
                }
                else
                {
                    sb.Append(property.Name).Append(": ").AppendLine(property.Value.ToString(Newtonsoft.Json.Formatting.None));
                }
            }

            return TextFormat.Truncate(sb.ToString().TrimEnd(), MaxDetailLength / 2);
        }

        private static string Str(JToken token, string path)
        {
            var value = token?.SelectToken(path);
            if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
            {
                return null;
            }

            return value.Type == JTokenType.String || value is JValue ? value.ToString() : value.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static long Long(JToken token, string path) =>
            long.TryParse(Str(token, path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

        private static DateTimeOffset? ParseTime(string value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result) ? result : (DateTimeOffset?)null;

        private static string Suffix(string value) => string.IsNullOrEmpty(value) ? string.Empty : " (" + value + ")";

        private static string Join(string a, string b, string separator = " — ")
        {
            if (string.IsNullOrWhiteSpace(a))
            {
                return b ?? string.Empty;
            }

            return string.IsNullOrWhiteSpace(b) ? a : a + separator + b;
        }
    }
}
