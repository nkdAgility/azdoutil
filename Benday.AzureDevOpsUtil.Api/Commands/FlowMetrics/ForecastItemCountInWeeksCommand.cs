using Benday.AzureDevOpsUtil.Api.FlowMetrics;
using Benday.CommandsFramework;

namespace Benday.AzureDevOpsUtil.Api.Commands.FlowMetrics;

[Command(
    Category = Constants.Category_FlowMetrics,
    Name = Constants.CommandArgumentNameGetForecastItemCountInWeeks,
        Description = "Use throughput data to forecast likely number of items done in given number of weeks using Monte Carlo simulation")]
public class ForecastItemCountInWeeksCommand : AzureDevOpsCommandBase
{
    public ForecastItemCountInWeeksCommand(
        CommandExecutionInfo info, ITextOutputProvider outputProvider) : base(info, outputProvider)
    {
    }

    public override ArgumentCollection GetArguments()
    {
        var arguments = new ArgumentCollection();

        AddCommonArguments(arguments);
        AddJsonOutputArguments(arguments);
        arguments.AddInt32(Constants.ArgumentNameCycleTimeNumberOfDays)
            .AsRequired()
            .WithDescription("Number of days of history to compute");
        arguments.AddString(Constants.ArgumentNameTeamProjectName)
            .AsRequired()
            .WithDescription("Team project name");
        arguments.AddInt32(Constants.ArgumentNameForecastNumberOfWeeks)
            .AsRequired()
            .WithDescription("Number of weeks into the future to forecast");

        arguments.AddString(Constants.ArgumentNameTeamName)
          .AsNotRequired()
          .WithDescription("Team name");

        return arguments;
    }

    protected override async Task OnExecute(CancellationToken cancellationToken)
    {
        _NumberOfWeeksOfForecast = Arguments.GetInt32Value(Constants.ArgumentNameForecastNumberOfWeeks);
        _NumberOfDaysOfHistory = Arguments.GetInt32Value(Constants.ArgumentNameCycleTimeNumberOfDays);
        _TeamProjectName = Arguments.GetStringValue(Constants.ArgumentNameTeamProjectName);
        _TeamName = Arguments.HasValue(Constants.ArgumentNameTeamName)
            ? Arguments.GetStringValue(Constants.ArgumentNameTeamName)
            : null;
        var toJson = IsJsonOutputRequested();
        ValidateJsonOutputArguments();

        var getDataCommand = await ExecuteAzdoCommandAsync<GetCycleTimeAndThroughputCommand>(args =>
        {
            args.Set(Constants.ArgumentNameCycleTimeNumberOfDays, _NumberOfDaysOfHistory);
            args.Set(Constants.ArgumentNameTeamProjectName, _TeamProjectName);

            CopyArgumentIfSupplied(args, Constants.ArgumentNameTeamName);
        });

        if (getDataCommand.Data == null ||
            getDataCommand.Data.Items == null ||
            getDataCommand.Data.Items.Length == 0)
        {
            throw new KnownException("No data");
        }

        DataGroupedByWeek = getDataCommand.GroupedByWeek;

        CreateForecast();
        if (toJson)
        {
            await WriteJsonOutputAsync(ToItemsForecastResult());
        }
        else
        {
            DisplayForecast();
        }
    }

    private void DisplayForecast()
    {
        if (_distribution == null)
        {
            throw new InvalidOperationException(
                $"{nameof(CreateForecast)} must run before {nameof(DisplayForecast)}.");
        }

        WriteLine(string.Empty);
        WriteLine($"How many items will we likely get done in {_NumberOfWeeksOfForecast} week(s)?");
        WriteLine(string.Empty);

        var throughput50PercentChance = _distribution.GetItemsAtSimulationThreshold(
            Constants.ForecastNumberOfSimulationsFiftyPercent);

        var throughput80PercentChance = _distribution.GetItemsAtSimulationThreshold(
            Constants.ForecastNumberOfSimulationsEightyPercent);

        var throughput90PercentChance = _distribution.GetItemsAtSimulationThreshold(
            Constants.ForecastNumberOfSimulationsNinetyPercent);

        var throughput100PercentChance = _distribution.GetItemsAtSimulationThreshold(
            Constants.ForecastNumberOfSimulationsHundredPercent);

        WriteLine($"50% sure {throughput50PercentChance} item(s) can be done");
        WriteLine($"80% sure {throughput80PercentChance} item(s) can be done");
        WriteLine($"90% sure {throughput90PercentChance} item(s) can be done");
        WriteLine($"~99% sure {throughput100PercentChance} item(s) can be done");

        WriteLine(string.Empty);
    }

    private void CreateForecast()
    {
        var weeklyThroughputs = DataGroupedByWeek.Values
            .Select(x => x.Items.Count)
            .ToList();

        _distribution = MonteCarloForecaster.SimulateItemsInWeeks(
            weeklyThroughputs, _NumberOfWeeksOfForecast);
    }

    private int _NumberOfWeeksOfForecast;
    private int _NumberOfDaysOfHistory;
    private string _TeamProjectName = string.Empty;
    private string? _TeamName = null;

    public Dictionary<DateTime, ThroughputIteration> DataGroupedByWeek { get; private set; } = new();
    private ItemsInWeeksDistribution? _distribution;

    private ItemsForecastResult ToItemsForecastResult()
    {
        if (_distribution == null)
        {
            throw new InvalidOperationException("Forecast distribution was not generated.");
        }

        var result = new ItemsForecastResult
        {
            TeamProject = _TeamProjectName,
            TeamName = _TeamName,
            WeekCount = _NumberOfWeeksOfForecast,
            DayRange = _NumberOfDaysOfHistory,
            NumberOfWeeksOfHistory = DataGroupedByWeek.Count,
            SimulationCount = _distribution.SimulationCount
        };

        result.ItemsByConfidence.Add(new ForecastConfidencePoint
        {
            ConfidencePercent = 50,
            Value = _distribution.GetItemsAtSimulationThreshold(Constants.ForecastNumberOfSimulationsFiftyPercent)
        });
        result.ItemsByConfidence.Add(new ForecastConfidencePoint
        {
            ConfidencePercent = 80,
            Value = _distribution.GetItemsAtSimulationThreshold(Constants.ForecastNumberOfSimulationsEightyPercent)
        });
        result.ItemsByConfidence.Add(new ForecastConfidencePoint
        {
            ConfidencePercent = 90,
            Value = _distribution.GetItemsAtSimulationThreshold(Constants.ForecastNumberOfSimulationsNinetyPercent)
        });
        result.ItemsByConfidence.Add(new ForecastConfidencePoint
        {
            ConfidencePercent = 99,
            Value = _distribution.GetItemsAtSimulationThreshold(Constants.ForecastNumberOfSimulationsHundredPercent)
        });

        result.Summary =
            $"Over {_NumberOfWeeksOfForecast} week(s) the team is 80% likely to complete at least " +
            $"{result.ItemsByConfidence.First(x => x.ConfidencePercent == 80).Value} item(s).";

        return result;
    }
}
