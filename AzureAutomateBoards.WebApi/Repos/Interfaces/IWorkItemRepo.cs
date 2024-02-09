using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;

namespace AzureAutomateBoards.WebApi.Repos.Interfaces;

public interface IWorkItemRepo
{
	Task<WorkItem> GetWorkItem(VssConnection connection, int id);
	Task<List<WorkItem>> ListChildWorkItemsForParent(VssConnection connection, WorkItem parentWorkItem);
	Task<WorkItem> UpdateWorkItemState(VssConnection connection, WorkItem workItem, string state);
}
