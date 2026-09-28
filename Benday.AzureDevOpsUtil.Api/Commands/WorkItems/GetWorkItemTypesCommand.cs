using Benday.AzureDevOpsUtil.Api.Messages;
using Benday.CommandsFramework;

namespace Benday.AzureDevOpsUtil.Api.Commands.WorkItems;

[Command(
    Category = Constants.Category_WorkItems,
    Name = Constants.CommandArgumentNameGetWorkItemTypes,
    Description = "Gets a list of work item types in an Azure DevOps Team Project.")]
public class GetWorkItemTypesCommand : AzureDevOpsCommandBase
{
    public GetWorkItemTypesCommand(
        CommandExecutionInfo info, ITextOutputProvider outputProvider) : base(info, outputProvider)
    {
    }

    public override ArgumentCollection GetArguments()
    {
        var args = new ArgumentCollection();


        AddCommonArguments(args);
        AddJsonOutputArguments(args);
        args.AddString(Constants.ArgumentNameTeamProjectName).AsRequired().
            WithDescription("Team project name that contains the work item types");

        args.AddBoolean(Constants.ArgumentNameNameOnly).
            AsNotRequired().
            WithDefaultValue(false).
            AllowEmptyValue().
            WithDescription("Only show the name of the work item types in the results.");

        return args;
    }

    protected override async Task OnExecute(CancellationToken cancellationToken)
    {
        var projectName = Arguments.GetStringValue(Constants.ArgumentNameTeamProjectName);
        var nameOnly = Arguments.GetBooleanValue(Constants.ArgumentNameNameOnly);
        var toJson = IsJsonOutputRequested();

        ValidateJsonOutputArguments();

        await RunQuery(projectName);

        if (toJson)
        {
            WriteJsonOutput(AllWorkItemTypes?.Types ?? Array.Empty<WorkItemTypeDefinitionResponse>());
            return;
        }
        else if (IsQuietMode == false && AllWorkItemTypes != null)
        {
            foreach (var item in AllWorkItemTypes.Types)
            {
                if (nameOnly == false)
                {
                    WriteLine(string.Empty);

                    WriteLine($"Name: {item.Name}");
                    WriteLine($"ReferenceName: {item.ReferenceName}");
                    WriteLine($"Description: {item.Description}");
                }
                else
                {
                    WriteLine(item.Name);
                }
            }
        }
    }

    private async Task RunQuery(string teamProjectName)
    {
        var requestUrl = $"{teamProjectName}/_apis/wit/workitemtypes?api-version=6.0";

        AllWorkItemTypes = await CallEndpointViaGetAndGetResult<WorkItemTypeDefinitionListResponse>(requestUrl);

    }

    public WorkItemTypeDefinitionListResponse? AllWorkItemTypes { get; private set; }
}
