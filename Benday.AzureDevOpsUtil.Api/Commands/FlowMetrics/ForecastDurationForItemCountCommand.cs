using Benday.AzureDevOpsUtil.Api.FlowMetrics;
using Benday.CommandsFramework;

namespace Benday.AzureDevOpsUtil.Api.Commands.FlowMetrics;

[Command(
    Category = Constants.Category_FlowMetrics,
    Name = Constants.CommandArgumentNameGetForecastDurationForItemCount,
        Description = "Use throughput data to forecast likely number of weeks to get given number of items done using Monte Carlo simulation")]
public class ForecastDurationForItemCountCommand : AzureDevOpsCommandBase
{
    public ForecastDurationForItemCountCommand(
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
        arguments.AddInt32(Constants.ArgumentNameForecastNumberOfItems)
            .AsRequired()
            .WithDescription("Number of items to forecast duration for");

        arguments.AddString(Constants.ArgumentNameTeamName)
            .AsNotRequired()
            .WithDescription("Team name");

        return arguments;
    }

    protected override async Task OnExecute(CancellationToken cancellationToken)
    {
        _NumberOfItemsToForecast = Arguments.GetInt32Value(Constants.ArgumentNameForecastNumberOfItems);
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
        PopulateForecastPoints();
        if (toJson)
        {
            await WriteJsonOutputAsync(ToDurationForecastResult());
        }
        else if (IsQuietMode == false)
        {
            DisplayForecast(getDataCommand);
        }
    }

    public void DisplayForecast(GetCycleTimeAndThroughputCommand getDataCommand)
    {
        var desc = $"How many weeks will it take us to get {_NumberOfItemsToForecast} item(s) done?";

        WriteThroughputByWeek(getDataCommand);

        DisplayForecast(desc);
    }

    private void WriteThroughputByWeek(GetCycleTimeAndThroughputCommand getDataCommand)
    {
        WriteLine(string.Empty);
        WriteLine($"Throughput for the last {getDataCommand.GroupedByWeek.Count} week(s):");
        
        var keysOrderedByAscending = getDataCommand.GroupedByWeek.Keys.OrderBy(x => x);

        foreach (var key in keysOrderedByAscending)
        {
            WriteThroughputForWeek(getDataCommand.GroupedByWeek[key]);
        }

        WriteLine(string.Empty);
    }

    private void WriteThroughputForWeek(ThroughputIteration throughputIteration)
    {
        var longestString = "mm/dd/yyyy".Length;

        string dateString = throughputIteration.StartOfWeek.ToShortDateString();

        // pad string to length of longest date string
        dateString = dateString.PadRight(longestString);

        WriteLine($"\t{dateString}: {throughputIteration.Items.Count}");
    }

    public void DisplayForecast(string forecastDescription)
    {
        WriteLine(forecastDescription);
        WriteLine(string.Empty);

        if (_distribution == null)
        {
            throw new InvalidOperationException(
                $"{nameof(CreateForecast)} must run before {nameof(DisplayForecast)}.");
        }

        var throughput50PercentChance = _distribution.GetWeeksAtSimulationThreshold(
            Constants.ForecastNumberOfSimulationsFiftyPercent);

        var throughput80PercentChance = _distribution.GetWeeksAtSimulationThreshold(
            Constants.ForecastNumberOfSimulationsEightyPercent);

        var throughput90PercentChance = _distribution.GetWeeksAtSimulationThreshold(
            Constants.ForecastNumberOfSimulationsNinetyPercent);

        var throughput100PercentChance = _distribution.GetWeeksAtSimulationThreshold(
            Constants.ForecastNumberOfSimulationsHundredPercent);

        WeeksAt50Percent = throughput50PercentChance;
        WeeksAt80Percent = throughput80PercentChance;
        WeeksAt90Percent = throughput90PercentChance;
        WeeksAt99Percent = throughput100PercentChance;

        WriteLine($"50% sure it can be done in {throughput50PercentChance} week(s)");
        WriteLine($"80% sure it can be done in {throughput80PercentChance} week(s)");
        WriteLine($"90% sure it can be done in {throughput90PercentChance} week(s)");
        WriteLine($"~99% sure it can be done in {throughput100PercentChance} week(s)");
        WriteLine(string.Empty);
    }

    private void CreateForecast()
    {
        var weeklyThroughputs = DataGroupedByWeek.Values
            .Select(x => x.Items.Count)
            .ToList();

        _distribution = MonteCarloForecaster.SimulateWeeksForItemCount(
            weeklyThroughputs, _NumberOfItemsToForecast);
    }

    private int _NumberOfItemsToForecast;
    private int _NumberOfDaysOfHistory;
    private string _TeamProjectName = string.Empty;
    private string? _TeamName = null;

    public Dictionary<DateTime, ThroughputIteration> DataGroupedByWeek { get; private set; } = new();
    private WeeksForItemCountDistribution? _distribution;
    public int? WeeksAt50Percent { get; private set; }
    public int? WeeksAt80Percent { get; private set; }
    public int? WeeksAt90Percent { get; private set; }
    public int? WeeksAt99Percent { get; private set; }

    private DurationForecastResult ToDurationForecastResult()
    {
        if (_distribution == null || WeeksAt50Percent.HasValue == false || WeeksAt80Percent.HasValue == false ||
            WeeksAt90Percent.HasValue == false || WeeksAt99Percent.HasValue == false)
        {
            throw new InvalidOperationException("Forecast distribution was not generated.");
        }

        var result = new DurationForecastResult
        {
            TeamProject = _TeamProjectName,
            TeamName = _TeamName,
            ItemCount = _NumberOfItemsToForecast,
            DayRange = _NumberOfDaysOfHistory,
            NumberOfWeeksOfHistory = DataGroupedByWeek.Count,
            SimulationCount = _distribution.SimulationCount
        };

        result.WeeksByConfidence.Add(new ForecastConfidencePoint { ConfidencePercent = 50, Value = WeeksAt50Percent.Value });
        result.WeeksByConfidence.Add(new ForecastConfidencePoint { ConfidencePercent = 80, Value = WeeksAt80Percent.Value });
        result.WeeksByConfidence.Add(new ForecastConfidencePoint { ConfidencePercent = 90, Value = WeeksAt90Percent.Value });
        result.WeeksByConfidence.Add(new ForecastConfidencePoint { ConfidencePercent = 99, Value = WeeksAt99Percent.Value });

        result.Summary =
            $"Completing {_NumberOfItemsToForecast} item(s) is 80% likely to take {WeeksAt80Percent.Value} week(s) or less.";

        return result;
    }

    private void PopulateForecastPoints()
    {
        if (_distribution == null)
        {
            throw new InvalidOperationException("Forecast distribution was not generated.");
        }

        WeeksAt50Percent = _distribution.GetWeeksAtSimulationThreshold(Constants.ForecastNumberOfSimulationsFiftyPercent);
        WeeksAt80Percent = _distribution.GetWeeksAtSimulationThreshold(Constants.ForecastNumberOfSimulationsEightyPercent);
        WeeksAt90Percent = _distribution.GetWeeksAtSimulationThreshold(Constants.ForecastNumberOfSimulationsNinetyPercent);
        WeeksAt99Percent = _distribution.GetWeeksAtSimulationThreshold(Constants.ForecastNumberOfSimulationsHundredPercent);
    }
}
