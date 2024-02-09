using AzureAutomateBoards.WebApi.Models;

namespace AzureAutomateBoards.WebApi.Repos.Interfaces;

public interface IRulesRepo
{
	Task<RulesModel> ListRules(string wit);
}
