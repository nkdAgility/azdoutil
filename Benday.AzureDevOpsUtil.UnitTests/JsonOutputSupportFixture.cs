using System.Text.Json;

using Benday.AzureDevOpsUtil.Api;
using Benday.AzureDevOpsUtil.Api.Commands.FlowMetrics;
using Benday.AzureDevOpsUtil.Api.Commands.ProjectAdministration;
using Benday.AzureDevOpsUtil.Api.Commands.WorkItems;
using Benday.CommandsFramework;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Benday.AzureDevOpsUtil.UnitTests;

[TestClass]
public class JsonOutputSupportFixture
{
    [TestMethod]
    public void UpdatedCommands_ExposeJsonAndOutputArguments()
    {
        var commands = new AzureDevOpsCommandBase[]
        {
            new GetAreasCommand(CreateInfo("getareas"), new StringBuilderTextOutputProvider()),
            new GetIterationsCommand(CreateInfo("getiterations"), new StringBuilderTextOutputProvider()),
            new ListTeamsForProjectCommand(CreateInfo("listteams"), new StringBuilderTextOutputProvider()),
            new GetWorkItemTypesCommand(CreateInfo("getworkitemtypes"), new StringBuilderTextOutputProvider()),
            new GetWorkItemStatesCommand(CreateInfo("getworkitemstates"), new StringBuilderTextOutputProvider()),
            new GetCycleTimeAndThroughputCommand(CreateInfo("throughputcycletime"), new StringBuilderTextOutputProvider()),
            new GetAgingWorkItemsCommand(CreateInfo("agingwork"), new StringBuilderTextOutputProvider()),
            new ForecastDurationForItemCountCommand(CreateInfo("forecastdurationforitemcount"), new StringBuilderTextOutputProvider()),
            new ForecastItemCountInWeeksCommand(CreateInfo("forecastitemsinweeks"), new StringBuilderTextOutputProvider()),
            new ForecastWorkItemDeliveryCommand(CreateInfo("forecastworkitem"), new StringBuilderTextOutputProvider()),
            new CalculateSuggestedServiceLevelExpectationCommand(CreateInfo("suggest-sle"), new StringBuilderTextOutputProvider()),
            new CycleTimeConfidenceRangesCommand(CreateInfo("cycletimeconfidence"), new StringBuilderTextOutputProvider())
        };

        foreach (var command in commands)
        {
            var args = command.GetArguments();

            Assert.IsTrue(args.ContainsKey(Constants.CommandArgumentNameToJson), $"{command.GetType().Name} missing --json");
            Assert.IsTrue(args.ContainsKey(Constants.ArgumentNameOutput), $"{command.GetType().Name} missing --output");
        }
    }

    [TestMethod]
    public void OutputWithoutJson_IsRejected()
    {
        var command = new TestJsonOutputCommand(
            CreateInfo("json-output-test", "--output", "result.json"),
            new StringBuilderTextOutputProvider());

        command.ValidateArguments();

        try
        {
            command.InvokeValidateJsonOutputArguments();
            Assert.Fail("Expected KnownException was not thrown.");
        }
        catch (KnownException ex)
        {
            StringAssert.Contains(ex.Message, "--output requires --json");
        }
    }

    [TestMethod]
    public void JsonOutput_WithoutOutputPath_WritesToStdoutAsValidJson()
    {
        var output = new StringBuilderTextOutputProvider();
        var command = new TestJsonOutputCommand(
            CreateInfo("json-output-test", "--json"),
            output);

        command.ValidateArguments();
        command.InvokeWriteJsonOutput(new { Name = "Example", Value = 42 });

        var json = output.GetOutput();
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("Example", doc.RootElement.GetProperty("Name").GetString());
        Assert.AreEqual(42, doc.RootElement.GetProperty("Value").GetInt32());
    }

    [TestMethod]
    public void JsonOutput_WithOutputPath_WritesFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"azdoutil-json-output-{Guid.NewGuid():N}.json");
        try
        {
            var command = new TestJsonOutputCommand(
                CreateInfo("json-output-test", "--json", "--output", tempFile),
                new StringBuilderTextOutputProvider());

            command.ValidateArguments();
            command.InvokeWriteJsonOutput(new { TeamProject = "MyProject", Count = 3 });

            Assert.IsTrue(File.Exists(tempFile));
            var content = File.ReadAllText(tempFile);
            using var doc = JsonDocument.Parse(content);
            Assert.AreEqual("MyProject", doc.RootElement.GetProperty("TeamProject").GetString());
            Assert.AreEqual(3, doc.RootElement.GetProperty("Count").GetInt32());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static CommandExecutionInfo CreateInfo(string commandName, params string[] args)
    {
        var argv = new List<string> { commandName };
        argv.AddRange(args);
        return new ArgumentCollectionFactory().Parse(argv.ToArray());
    }

    private class TestJsonOutputCommand : AzureDevOpsCommandBase
    {
        public TestJsonOutputCommand(
            CommandExecutionInfo info,
            ITextOutputProvider outputProvider) : base(info, outputProvider)
        {
        }

        public override ArgumentCollection GetArguments()
        {
            var arguments = new ArgumentCollection();
            AddCommonArguments(arguments);
            AddJsonOutputArguments(arguments);
            return arguments;
        }

        protected override Task OnExecute(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void InvokeValidateJsonOutputArguments()
        {
            ValidateJsonOutputArguments();
        }

        public void InvokeWriteJsonOutput<T>(T value)
        {
            WriteJsonOutput(value);
        }
    }
}
