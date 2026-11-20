using System.Linq;
using CopilotActivityViewer.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CopilotActivityViewer.Tests
{
    [TestClass]
    public class SessionModelTests
    {
        private const string SubagentA = "aaaaaaaa-0000-0000-0000-000000000001";
        private const string SubagentB = "bbbbbbbb-0000-0000-0000-000000000002";

        [TestMethod]
        public void BuildsNestedAgentTreeAndToolLifecycle()
        {
            var model = new SessionModel();
            Apply(model,
                "{\"type\":\"session.start\",\"data\":{\"sessionId\":\"s1\",\"selectedModel\":\"model-x\",\"context\":{\"cwd\":\"C:\\\\repo\"}},\"timestamp\":\"2026-11-19T10:00:00.000Z\"}",
                "{\"type\":\"user.message\",\"data\":{\"content\":\"Fix the bug\"},\"timestamp\":\"2026-11-19T10:00:01.000Z\"}",
                "{\"type\":\"assistant.turn_start\",\"data\":{},\"timestamp\":\"2026-11-19T10:00:02.000Z\"}",
                "{\"type\":\"assistant.message\",\"data\":{\"content\":\"Delegating.\",\"toolRequests\":[{\"toolCallId\":\"t1\"}]},\"timestamp\":\"2026-11-19T10:00:03.000Z\"}",
                "{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"t1\",\"toolName\":\"task\",\"arguments\":{\"name\":\"fixer\",\"agent_type\":\"general-purpose\",\"description\":\"Fix it\",\"prompt\":\"Do the fix\"}},\"timestamp\":\"2026-11-19T10:00:04.000Z\"}",
                "{\"type\":\"subagent.started\",\"data\":{\"toolCallId\":\"t1\",\"agentName\":\"general-purpose\",\"agentDisplayName\":\"fixer\",\"agentDescription\":\"Fix it\",\"model\":\"model-y\",\"agentType\":\"general-purpose\",\"executionMode\":\"background\"},\"agentId\":\"" + SubagentA + "\",\"timestamp\":\"2026-11-19T10:00:05.000Z\"}",
                "{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"t2\",\"toolName\":\"powershell\",\"toolTitle\":\"Running command\",\"arguments\":{\"command\":\"dotnet build\",\"description\":\"Build\"},\"parentToolCallId\":\"t1\"},\"agentId\":\"" + SubagentA + "\",\"timestamp\":\"2026-11-19T10:00:06.000Z\"}",
                "{\"type\":\"permission.requested\",\"data\":{\"permissionRequest\":{\"kind\":\"shell\",\"toolCallId\":\"t2\"}},\"timestamp\":\"2026-11-19T10:00:06.500Z\"}");

            var subagent = model.FindAgent(SubagentA);
            var build = model.Activities.Single(a => a.ToolCallId == "t2");
            Assert.AreEqual(ActivityStatus.AwaitingApproval, build.Status);
            Assert.AreEqual(AgentStatus.Running, subagent.Status);
            Assert.AreEqual("Build", build.Subtitle);

            Apply(model,
                "{\"type\":\"permission.completed\",\"data\":{\"toolCallId\":\"t2\",\"result\":{\"kind\":\"approved\"}},\"timestamp\":\"2026-11-19T10:00:07.000Z\"}",
                "{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"t3\",\"toolName\":\"task\",\"arguments\":{\"name\":\"explorer\",\"agent_type\":\"explore\"}},\"agentId\":\"" + SubagentA + "\",\"timestamp\":\"2026-11-19T10:00:08.000Z\"}",
                "{\"type\":\"subagent.started\",\"data\":{\"toolCallId\":\"t3\",\"agentName\":\"explore\",\"agentDisplayName\":\"explorer\"},\"agentId\":\"" + SubagentB + "\",\"timestamp\":\"2026-11-19T10:00:09.000Z\"}",
                "{\"type\":\"tool.execution_complete\",\"data\":{\"toolCallId\":\"t2\",\"success\":true,\"result\":{\"content\":\"Build succeeded\"}},\"timestamp\":\"2026-11-19T10:00:16.000Z\"}",
                "{\"type\":\"session.usage_record\",\"data\":{\"usage\":{\"inputTokens\":100,\"outputTokens\":20}},\"agentId\":\"" + SubagentA + "\",\"timestamp\":\"2026-11-19T10:00:17.000Z\"}",
                "{\"type\":\"subagent.completed\",\"data\":{\"toolCallId\":\"t3\",\"totalTokens\":5000},\"agentId\":\"" + SubagentB + "\",\"timestamp\":\"2026-11-19T10:00:20.000Z\"}");

            Assert.AreEqual(ActivityStatus.Running, model.Activities.Single(a => a.ToolCallId == "t3").Status);
            Assert.AreEqual(ActivityStatus.Succeeded, build.Status);
            Assert.AreEqual(10, build.Duration.Value.TotalSeconds, 0.001);
            StringAssert.Contains(build.Details, "Build succeeded");

            CollectionAssert.AreEqual(new[] { subagent }, model.MainAgent.Children.ToArray());
            var nested = model.FindAgent(SubagentB);
            CollectionAssert.AreEqual(new[] { nested }, subagent.Children.ToArray());
            Assert.AreSame(subagent, nested.Parent);
            Assert.AreEqual(AgentStatus.Completed, nested.Status);
            Assert.AreEqual(5000L, nested.ReportedTotalTokens);
            Assert.AreEqual("fixer", subagent.Label);
            Assert.AreEqual("model-y", subagent.Model);
            Assert.AreEqual(2, subagent.ToolCalls);
            Assert.AreEqual(120L, model.InputTokens + model.OutputTokens);
            Assert.AreEqual("model-x", model.CurrentModel);
            Assert.AreEqual(@"C:\repo", model.Cwd);

            Assert.IsTrue(subagent.IsSelfOrAncestorOf(nested));
            Assert.IsTrue(model.MainAgent.IsSelfOrAncestorOf(nested));
            Assert.IsFalse(nested.IsSelfOrAncestorOf(subagent));
        }

        [TestMethod]
        public void AbortCancelsRunningToolsOfThatAgent()
        {
            var model = new SessionModel();
            Apply(model,
                "{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"t1\",\"toolName\":\"grep\",\"arguments\":{\"pattern\":\"foo\"}},\"timestamp\":\"2026-11-19T10:00:00.000Z\"}",
                "{\"type\":\"abort\",\"data\":{\"reason\":\"user_initiated\"},\"timestamp\":\"2026-11-19T10:00:02.000Z\"}");

            var tool = model.Activities.Single(a => a.ToolCallId == "t1");
            Assert.AreEqual(ActivityStatus.Cancelled, tool.Status);
            Assert.AreEqual("foo", tool.Subtitle);
            Assert.AreEqual(AgentStatus.Aborted, model.MainAgent.Status);
            Assert.AreEqual(0, model.MainAgent.RunningTools);
        }

        [TestMethod]
        public void TurnEndWithoutToolRequestsMakesAgentIdle()
        {
            var model = new SessionModel();
            Apply(model,
                "{\"type\":\"user.message\",\"data\":{\"content\":\"hi\"},\"timestamp\":\"2026-11-19T10:00:00.000Z\"}",
                "{\"type\":\"assistant.turn_start\",\"data\":{},\"timestamp\":\"2026-11-19T10:00:01.000Z\"}",
                "{\"type\":\"assistant.message\",\"data\":{\"content\":\"hello\",\"toolRequests\":[]},\"timestamp\":\"2026-11-19T10:00:02.000Z\"}");
            Assert.AreEqual(AgentStatus.Running, model.MainAgent.Status);

            Apply(model, "{\"type\":\"assistant.turn_end\",\"data\":{},\"timestamp\":\"2026-11-19T10:00:03.000Z\"}");

            Assert.AreEqual(AgentStatus.Idle, model.MainAgent.Status);
            CollectionAssert.AreEqual(new[] { ActivityKind.User, ActivityKind.Assistant }, model.Activities.Select(a => a.Kind).ToArray());
        }

        [TestMethod]
        public void FailedToolAndDeniedPermissionAreReported()
        {
            var model = new SessionModel();
            Apply(model,
                "{\"type\":\"tool.execution_start\",\"data\":{\"toolCallId\":\"t1\",\"toolName\":\"edit\",\"arguments\":{\"path\":\"a.cs\"}},\"timestamp\":\"2026-11-19T10:00:00.000Z\"}",
                "{\"type\":\"permission.requested\",\"data\":{\"permissionRequest\":{\"toolCallId\":\"t1\"}},\"timestamp\":\"2026-11-19T10:00:00.100Z\"}",
                "{\"type\":\"permission.completed\",\"data\":{\"toolCallId\":\"t1\",\"result\":{\"kind\":\"denied-interactively-by-user\"}},\"timestamp\":\"2026-11-19T10:00:01.000Z\"}",
                "{\"type\":\"tool.execution_complete\",\"data\":{\"toolCallId\":\"t1\",\"success\":false,\"error\":{\"message\":\"Permission denied\"}},\"timestamp\":\"2026-11-19T10:00:01.100Z\"}");

            var tool = model.Activities.Single();
            Assert.AreEqual(ActivityStatus.Failed, tool.Status);
            StringAssert.Contains(tool.Details, "denied-interactively-by-user");
            StringAssert.Contains(tool.Details, "Permission denied");
        }

        [TestMethod]
        public void MalformedAndUnknownLinesAreIgnored()
        {
            var model = new SessionModel();
            model.ApplyLine("not json");
            model.ApplyLine(string.Empty);
            model.ApplyLine("{\"type\":\"something.new\",\"data\":{\"x\":1}}");
            model.ApplyLine("{\"type\":\"tool.execution_complete\",\"data\":{\"toolCallId\":\"missing\"}}");

            Assert.IsEmpty(model.Activities);
            Assert.AreEqual(2, model.EventCount);
        }

        [TestMethod]
        public void SummarizesTaskArguments()
        {
            var args = System.Text.Json.Nodes.JsonNode.Parse("{\"name\":\"fixer\",\"agent_type\":\"general-purpose\",\"description\":\"Fix it\"}").AsObject();
            Assert.AreEqual("fixer (general-purpose) — Fix it", SessionModel.SummarizeArguments("task", args));
        }

        private static void Apply(SessionModel model, params string[] lines)
        {
            foreach (var line in lines)
            {
                var e = SessionModel.ParseLine(line);
                Assert.IsNotNull(e, "Invalid test JSON: " + line);
                model.Apply(e);
            }
        }
    }
}
