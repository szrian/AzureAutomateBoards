using AzureAutomateBoards.WebApi.Misc;
using AzureAutomateBoards.WebApi.Models;
using AzureAutomateBoards.WebApi.Repos.Interfaces;
using AzureAutomateBoards.WebApi.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json.Linq;

namespace AzureAutomateBoards.WebApi.Controllers;

[Route("api/receiver")]
[ApiController]
public class ReceiverController : Controller
{
	private readonly IWorkItemRepo _workItemRepo;
	private readonly IRulesRepo _rulesRepo;
	private readonly IOptions<AppSettings> _appSettings;
	private readonly IHelper _helper;

	public ReceiverController(IWorkItemRepo workItemRepo, IRulesRepo rulesRepo, IHelper helper, IOptions<AppSettings> appSettings)
	{
		_workItemRepo = workItemRepo;
		_rulesRepo = rulesRepo;
		_appSettings = appSettings;
		_helper = helper;
	}

	[HttpPost]
	[Route("webhook/workitem/update")]
	public async Task<IActionResult> Post([FromBody] JObject payload)
	{
		try
		{
			PayloadViewModel vm = BuildPayloadViewModel(payload);

			//make sure pat is not empty, if it is, pull from appsettings
			vm.pat = _appSettings.Value.PersonalAccessToken;

			//if the event type is something other the updated, then lets just return an ok
			if (vm.eventType != "workitem.updated") return new OkResult();

			// create our azure devops connection
			Uri baseUri = new Uri("https://dev.azure.com/" + vm.organization);

			VssCredentials clientCredentials = new VssCredentials(new VssBasicCredential("rian_s_araujo@hotmail.com", vm.pat));
			VssConnection vssConnection = new VssConnection(baseUri, clientCredentials);

			// load the work item posted 
			WorkItem workItem = await _workItemRepo.GetWorkItem(vssConnection, vm.workItemId);

			// this should never happen, but if we can't load the work item from the id, then exit with error
			if (workItem == null) return new StandardResponseObjectResult("Error loading workitem '" + vm.workItemId + "'", StatusCodes.Status500InternalServerError);

			if (workItem.Fields["System.WorkItemType"].ToString().Equals("Feature") && workItem.Fields["System.State"].ToString().Equals("In Progress"))
			{
				await InicializeChilds(workItem, vssConnection);

				return new OkResult();
			}

			if (workItem.Fields["System.WorkItemType"].ToString().Equals("Feature") && workItem.Fields["System.State"].ToString().Equals("Done"))
			{
				await FeatureFinished(workItem, vssConnection);
				return new OkResult();
			}

			if (workItem.Fields["System.WorkItemType"].ToString().Equals("Product Backlog Item") && workItem.Fields["System.State"].ToString().Equals("Done"))
				await BackLogFinished(workItem, vssConnection);

			// get the related parent
			WorkItemRelation parentRelation = workItem.Relations.Where<WorkItemRelation>(x => x.Rel.Equals("System.LinkTypes.Hierarchy-Reverse")).FirstOrDefault();

			// if we don't have any parents to worry about, then just abort
			if (parentRelation == null) return new OkResult();

			Int32 parentId = _helper.GetWorkItemIdFromUrl(parentRelation.Url);
			WorkItem parentWorkItem = await _workItemRepo.GetWorkItem(vssConnection, parentId);

			if (parentWorkItem == null) return new StandardResponseObjectResult("Error loading parent work item '" + parentId.ToString() + "'", StatusCodes.Status500InternalServerError);

			string parentState = parentWorkItem.Fields["System.State"] == null ? string.Empty : parentWorkItem.Fields["System.State"].ToString();

			// load rules for updated work item
			RulesModel rulesModel = await _rulesRepo.ListRules(vm.workItemType);

			//loop through each rule
			foreach (var rule in rulesModel.Rules)
			{
				if (rule.IfChildState.Equals(vm.state))
				{
					if (!rule.AllChildren)
					{
						if (!rule.NotParentStates.Contains(parentState))
						{
							await _workItemRepo.UpdateWorkItemState(vssConnection, parentWorkItem, rule.SetParentStateTo);
						}
					}
					else
					{
						// get a list of all the child items to see if they are all closed or not
						List<WorkItem> childWorkItems = await _workItemRepo.ListChildWorkItemsForParent(vssConnection, parentWorkItem);

						// check to see if any of the child items are not closed, if so, we will get a count > 0
						int count = childWorkItems.Where(x => !x.Fields["System.State"].ToString().Equals(rule.IfChildState)).ToList().Count;

						if (count.Equals(0))
							await _workItemRepo.UpdateWorkItemState(vssConnection, parentWorkItem, rule.SetParentStateTo);
					}
				}
			}

			return new StandardResponseObjectResult("success", StatusCodes.Status200OK);
		}
		catch (Exception ex)
		{
			return new StandardResponseObjectResult(ex.Message, StatusCodes.Status500InternalServerError);
		}

	}

	private PayloadViewModel BuildPayloadViewModel(JObject body)
	{
		PayloadViewModel vm = new PayloadViewModel();

		string url = body["resource"]["url"] == null ? null : body["resource"]["url"].ToString();
		string org = GetOrganization(url);

		vm.workItemId = body["resource"]["workItemId"] == null ? -1 : Convert.ToInt32(body["resource"]["workItemId"].ToString());
		vm.workItemType = body["resource"]["revision"]["fields"]["System.WorkItemType"] == null ? null : body["resource"]["revision"]["fields"]["System.WorkItemType"].ToString();
		vm.eventType = body["eventType"] == null ? null : body["eventType"].ToString();
		vm.rev = body["resource"]["rev"] == null ? -1 : Convert.ToInt32(body["resource"]["rev"].ToString());
		vm.url = body["resource"]["url"] == null ? null : body["resource"]["url"].ToString();
		vm.organization = org;
		vm.teamProject = body["resource"]["fields"]["System.AreaPath"] == null ? null : body["resource"]["fields"]["System.AreaPath"].ToString();
		vm.state = body["resource"]["fields"]["System.State"]["newValue"] == null ? null : body["resource"]["fields"]["System.State"]["newValue"].ToString();

		return vm;
	}

	private string GetOrganization(string url)
	{
		url = url.Replace("http://", string.Empty);
		url = url.Replace("https://", string.Empty);

		if (url.Contains(value: "visualstudio.com"))
		{
			string[] split = url.Split('.');
			return split[0].ToString();
		}

		if (url.Contains("dev.azure.com"))
		{
			url = url.Replace("dev.azure.com/", string.Empty);
			string[] split = url.Split('/');
			return split[0].ToString();
		}

		return string.Empty;
	}

	private async Task InicializeChilds(WorkItem featureWorkItem, VssConnection vssConnection)
	{
		var featureWorkItemChildRelation = featureWorkItem.Relations.Where<WorkItemRelation>(x => x.Rel.Equals("System.LinkTypes.Hierarchy-Forward")).OrderBy(p => p.Url).FirstOrDefault();

		Int32 backLogId = _helper.GetWorkItemIdFromUrl(featureWorkItemChildRelation.Url);
		WorkItem backLogWorkItem = await _workItemRepo.GetWorkItem(vssConnection, backLogId);

		await _workItemRepo.UpdateWorkItemState(vssConnection, backLogWorkItem, "In Progress");

		var backLogWorkItemChildRelations = backLogWorkItem.Relations.Where<WorkItemRelation>(x => x.Rel.Equals("System.LinkTypes.Hierarchy-Forward"));

		foreach(var relation in backLogWorkItemChildRelations)
		{
			Int32 taskId = _helper.GetWorkItemIdFromUrl(relation.Url);
			var taskWorkItem = await _workItemRepo.GetWorkItem(vssConnection, taskId);

			await _workItemRepo.UpdateWorkItemState(vssConnection, taskWorkItem, "In Progress");
		}
	}

	private async Task BackLogFinished(WorkItem backLogWorkItem, VssConnection vssConnection)
	{
		var backLogWorkItemParentRelation = backLogWorkItem.Relations.Where<WorkItemRelation>(x => x.Rel.Equals("System.LinkTypes.Hierarchy-Reverse")).FirstOrDefault();

		Int32 featureId = _helper.GetWorkItemIdFromUrl(backLogWorkItemParentRelation.Url);
		var featureWorkItem = await _workItemRepo.GetWorkItem(vssConnection, featureId);

		var featureWorkItemChildRelations = featureWorkItem.Relations.Where<WorkItemRelation>(x => x.Rel.Equals("System.LinkTypes.Hierarchy-Forward")).OrderBy(p => p.Url);

		foreach (var relation in featureWorkItemChildRelations)
		{
			Int32 backLogId = _helper.GetWorkItemIdFromUrl(relation.Url);
			var childBackLogWorkItem = await _workItemRepo.GetWorkItem(vssConnection, backLogId);

			if (childBackLogWorkItem.Fields["System.State"].ToString().Equals("New"))
			{
				await _workItemRepo.UpdateWorkItemState(vssConnection, childBackLogWorkItem, "Committed");

				var backLogWorkItemChildRelations = childBackLogWorkItem.Relations.Where<WorkItemRelation>(x => x.Rel.Equals("System.LinkTypes.Hierarchy-Forward")).OrderBy(p => p.Url);

				foreach (var taskRelation in backLogWorkItemChildRelations)
				{
					Int32 taskId = _helper.GetWorkItemIdFromUrl(taskRelation.Url);
					var taskWorkItem = await _workItemRepo.GetWorkItem(vssConnection, taskId);

					await _workItemRepo.UpdateWorkItemState(vssConnection, taskWorkItem, "In Progress");
				}

				break;
			}
		}
	}

	private async Task FeatureFinished(WorkItem featureWorkItem, VssConnection vssConnection)
	{
		var featureWorkItemChildRelation = featureWorkItem.Relations.Where<WorkItemRelation>(x => x.Rel.Equals("System.LinkTypes.Hierarchy-Forward")).FirstOrDefault();
		Int32 backLogId = _helper.GetWorkItemIdFromUrl(featureWorkItemChildRelation.Url);

		var backLogWorkItem = await _workItemRepo.GetWorkItem(vssConnection, backLogId);
		var backLogWorkItemTitle = backLogWorkItem.Fields["System.Title"].ToString();

		var epicState = backLogWorkItemTitle.Substring(backLogWorkItemTitle.Length - 3);
		var featureWorkItemParentRelation = featureWorkItem.Relations.Where<WorkItemRelation>(x => x.Rel.Equals("System.LinkTypes.Hierarchy-Reverse")).FirstOrDefault();
		Int32 epicId = _helper.GetWorkItemIdFromUrl(featureWorkItemParentRelation.Url);

		var epicWorkItem = await _workItemRepo.GetWorkItem(vssConnection, epicId);

		await _workItemRepo.UpdateWorkItemState(vssConnection, epicWorkItem, "In Progress");
	}
}
